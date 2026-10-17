using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GazeShift
{
    public sealed class RegionInteraction : MonoBehaviour
    {
        private const float CanvasDistance = 1f;
        private const float OutlineWidth = 0.003f;
        private const float ResizeSpeed = 0.7f;
        private const float StickDeadzone = 0.2f;
        private const float InstructionDuration = 3f;
        private const float RayLength = 4f;
        private const int TextRenderQueue = 3020;
        private const string Instructions = "Hold trigger: trace a closed region. Accept or A. B: undo.\nGrip: select and drag a copy. Right stick: resize.";
        private const string CoverageInstruction = "Draw closer to the center; the camera view is narrower.";
        private readonly RegionSession session = new RegionSession();
        private readonly Dictionary<RegionData, LineRenderer> outlines = new Dictionary<RegionData, LineRenderer>();
        private readonly Dictionary<RegionData, LiveRegionView> views = new Dictionary<RegionData, LiveRegionView>();
        private ICameraFrameSource source;
        private Transform centerEye;
        private Transform controller;
        private Transform plane;
        private TextMesh instruction;
        private BoxCollider acceptCollider;
        private LineRenderer strokeLine;
        private LineRenderer rayLine;
        private Material liveMaterial;
        private Material outlineMaterial;
        private Material buttonMaterial;
        private Material textMaterial;
        private Font textFont;
        private RegionData selected;
        private Vector2 dragOffset;
        private bool dragging;
        private bool paused;
        private bool focused = true;
        private string temporaryInstruction;
        private float instructionUntil;

        public void Initialize(ICameraFrameSource frameSource, Transform eye)
        {
            if (plane != null) return;
            source = frameSource; centerEye = eye;
            if (source == null || centerEye == null)
            {
                Debug.LogError("GazeShift requires a camera source and center-eye transform.", this);
                enabled = false; return;
            }
            Shader liveShader = Shader.Find("GazeShift/LiveRegion"), overlayShader = Shader.Find("GazeShift/OverlayColor");
            if (liveShader == null || overlayShader == null)
            {
                Debug.LogError("GazeShift shaders are missing from the build.", this);
                enabled = false; return;
            }
            liveMaterial = new Material(liveShader) { name = "Live camera regions", enableInstancing = true };
            outlineMaterial = new Material(overlayShader) { name = "Region outlines", enableInstancing = true };
            outlineMaterial.SetColor("_Color", new Color(0.2f, 0.9f, 1, 0.9f));
            buttonMaterial = new Material(overlayShader) { name = "Accept button", enableInstancing = true };
            buttonMaterial.SetColor("_Color", new Color(0.03f, 0.16f, 0.2f, 0.95f));
            plane = new GameObject("Head-relative drawing plane").transform;
            plane.SetParent(centerEye, false); plane.localPosition = Vector3.forward * CanvasDistance;
            instruction = CreateText("Instructions", Instructions, new Vector3(0, 0.47f, -0.015f), 0.021f);
            CreateAcceptButton();
            strokeLine = CreateLine("Current stroke", false);
            rayLine = CreateLine("Right controller ray", true);
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null) controller = rig.rightControllerAnchor;
        }

        private void Update()
        {
            if (plane == null) return;
            if (controller == null || paused || !focused || !HeadTracked() || !ControllerTracked())
            {
                SuspendInteraction();
                SetInstruction(paused || !focused ? "Keep GazeShift open and wait for the camera." : "Wear the headset and reconnect the right controller.");
                return;
            }
            Ray ray = new Ray(controller.position, controller.forward);
            bool canvasHit = TryCanvasHit(ray, out Vector2 hit);
            rayLine.enabled = true; rayLine.positionCount = 2;
            rayLine.SetPosition(0, ray.origin);
            rayLine.SetPosition(1, canvasHit ? plane.TransformPoint(new Vector3(hit.x, hit.y, 0)) : ray.GetPoint(CanvasDistance));
            if (!source.IsReady || source.FrameTexture == null)
            {
                SuspendInteraction();
                SetInstruction(source is QuestCameraSource quest ? quest.StatusInstruction : "Allow camera access in Quest settings, then reopen GazeShift.");
                return;
            }

            HandleButtons(ray);
            HandleStroke(canvasHit, hit);
            HandleCopyMovement(canvasHit, hit);
            bool allCovered = UpdateLiveRegions();
            UpdateStrokeOutline();
            SetInstruction(!allCovered ? CoverageInstruction : Time.unscaledTime < instructionUntil ? temporaryInstruction : Instructions);
        }

        private void HandleButtons(Ray ray)
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) AcceptRegions();
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) { session.UndoPending(); SyncOutlines(); }
            if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)) return;
            Physics.SyncTransforms();
            bool hitAccept = acceptCollider.Raycast(ray, out _, RayLength);
            session.BeginTrigger(hitAccept);
            if (hitAccept) AcceptRegions();
        }

        private void HandleStroke(bool canvasHit, Vector2 hit)
        {
            if (session.IsDrawing && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {
                Vector3 world = plane.TransformPoint(new Vector3(hit.x, hit.y, 0));
                if (!canvasHit || !source.TryWorldToTextureUv(world, out _))
                { session.CancelStroke(); ShowTemporaryInstruction(CoverageInstruction); }
                else session.AddPoint(hit);
            }
            if (!OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) || !session.IsDrawing) return;
            if (!session.FinishStroke()) ShowTemporaryInstruction("Trace a larger closed region away from copies, then release.");
            SyncOutlines();
        }

        private void HandleCopyMovement(bool canvasHit, Vector2 hit)
        {
            if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
            {
                session.CancelStroke(); selected = null; dragging = false;
                if (canvasHit)
                    for (int i = session.Accepted.Count - 1; i >= 0; i--)
                    {
                        RegionData candidate = session.Accepted[i];
                        if (!views[candidate].IsVisible || (!candidate.ContainsCopy(hit) && !RegionGeometry.ContainsPoint(candidate.Source, hit))) continue;
                        selected = candidate; dragOffset = candidate.Destination - hit; dragging = true; break;
                    }
            }
            if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) dragging = false;
            if (selected == null) return;
            if (dragging && canvasHit && OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
                session.TryPlace(selected, hit + dragOffset, selected.Scale);
            float stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).y;
            if (Mathf.Abs(stick) > StickDeadzone)
                session.TryPlace(selected, selected.Destination, RegionSession.ClampScale(selected.Scale, stick * ResizeSpeed * Time.unscaledDeltaTime));
        }

        private void AcceptRegions()
        {
            for (int i = 0; i < session.Pending.Count; i++)
                for (int j = 0; j < session.Pending[i].Source.Length; j++)
                {
                    Vector2 point = session.Pending[i].Source[j];
                    if (source.TryWorldToTextureUv(plane.TransformPoint(new Vector3(point.x, point.y, 0)), out _)) continue;
                    session.CancelStroke(); ShowTemporaryInstruction(CoverageInstruction); return;
                }
            if (!session.AcceptPending())
            { ShowTemporaryInstruction("Leave room around the regions for sharp copies. B: undo."); return; }
            for (int i = 0; i < session.Accepted.Count; i++)
            {
                RegionData region = session.Accepted[i];
                if (!views.ContainsKey(region)) views.Add(region, new LiveRegionView(region, plane, liveMaterial));
            }
            SyncOutlines();
        }

        private bool UpdateLiveRegions()
        {
            bool covered = true;
            foreach (KeyValuePair<RegionData, LiveRegionView> pair in views)
            {
                bool visible = pair.Value.UpdateFrame(source);
                outlines[pair.Key].enabled = visible;
                if (!visible) covered = false;
            }
            for (int i = 0; i < session.Pending.Count; i++) outlines[session.Pending[i]].enabled = true;
            return covered;
        }

        private void SyncOutlines()
        {
            var removed = new List<RegionData>();
            foreach (RegionData region in outlines.Keys)
                if (!session.Pending.Contains(region) && !session.Accepted.Contains(region)) removed.Add(region);
            for (int i = 0; i < removed.Count; i++)
            { Destroy(outlines[removed[i]].gameObject); outlines.Remove(removed[i]); }
            for (int i = 0; i < session.Pending.Count; i++)
            {
                RegionData region = session.Pending[i];
                if (outlines.ContainsKey(region)) continue;
                LineRenderer line = CreateLine("Region boundary", false); line.loop = true; line.positionCount = region.Source.Length;
                for (int j = 0; j < region.Source.Length; j++) line.SetPosition(j, new Vector3(region.Source[j].x, region.Source[j].y, -0.004f));
                outlines.Add(region, line);
            }
        }

        private void UpdateStrokeOutline()
        {
            strokeLine.enabled = session.IsDrawing; strokeLine.positionCount = session.Stroke.Count;
            for (int i = 0; i < session.Stroke.Count; i++) strokeLine.SetPosition(i, new Vector3(session.Stroke[i].x, session.Stroke[i].y, -0.004f));
        }
        private bool TryCanvasHit(Ray ray, out Vector2 point) => RegionGeometry.TryCanvasHit(plane.InverseTransformPoint(ray.origin), plane.InverseTransformDirection(ray.direction), RegionSession.CanvasHalfExtent, out point);
        private static bool HeadTracked() => InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        private static bool ControllerTracked() => OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch) && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
        private void ShowTemporaryInstruction(string text) { temporaryInstruction = text; instructionUntil = Time.unscaledTime + InstructionDuration; }
        private void SetInstruction(string text) { if (instruction != null && instruction.text != text) instruction.text = text; }
        private void SuspendInteraction()
        {
            session.CancelStroke(); selected = null; dragging = false;
            if (strokeLine != null) { strokeLine.positionCount = 0; strokeLine.enabled = false; }
            if (rayLine != null) rayLine.enabled = false;
            foreach (LiveRegionView view in views.Values) view.SetVisible(false);
            foreach (LineRenderer line in outlines.Values) line.enabled = false;
        }
        private void OnApplicationPause(bool pause) { paused = pause; if (pause) SuspendInteraction(); }
        private void OnApplicationFocus(bool focus) { focused = focus; if (!focus) SuspendInteraction(); }
        private void OnDisable() => SuspendInteraction();
        private void OnDestroy()
        {
            foreach (LiveRegionView view in views.Values) view.Dispose();
            Font.textureRebuilt -= RefreshFontTexture;
            if (plane != null) DestroyOwned(plane.gameObject);
            DestroyOwned(liveMaterial); DestroyOwned(outlineMaterial); DestroyOwned(buttonMaterial); DestroyOwned(textMaterial);
        }

        private static void DestroyOwned(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
        private void RefreshFontTexture(Font font)
        {
            if (font == textFont && textMaterial != null) textMaterial.mainTexture = font.material.mainTexture;
        }

        private LineRenderer CreateLine(string name, bool worldSpace)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(plane, false); line.useWorldSpace = worldSpace;
            line.sharedMaterial = outlineMaterial; line.widthMultiplier = OutlineWidth;
            line.numCapVertices = 2; line.positionCount = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        private TextMesh CreateText(string name, string text, Vector3 position, float size)
        {
            var label = new GameObject(name, typeof(TextMesh)).GetComponent<TextMesh>();
            label.transform.SetParent(plane, false); label.transform.localPosition = position;
            label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            if (textFont == null)
            {
                textFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                textMaterial = new Material(textFont.material) { name = "GazeShift text", renderQueue = TextRenderQueue };
                Font.textureRebuilt += RefreshFontTexture;
            }
            label.font = textFont;
            label.GetComponent<MeshRenderer>().sharedMaterial = textMaterial;
            label.fontSize = 64; label.characterSize = size; label.color = Color.white;
            return label;
        }
        private void CreateAcceptButton()
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Quad);
            button.name = "Accept regions"; button.transform.SetParent(plane, false);
            button.transform.localPosition = new Vector3(0, -0.47f, -0.01f);
            button.transform.localScale = new Vector3(0.25f, 0.065f, 1);
            DestroyOwned(button.GetComponent<Collider>());
            button.GetComponent<MeshRenderer>().sharedMaterial = buttonMaterial;
            acceptCollider = button.AddComponent<BoxCollider>(); acceptCollider.size = new Vector3(1, 1, 0.01f);
            CreateText("Accept regions label", "Accept regions", new Vector3(0, -0.47f, -0.02f), 0.027f);
        }
    }
}
