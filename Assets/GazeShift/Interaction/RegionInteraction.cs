using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace GazeShift
{
    public sealed class RegionInteraction : MonoBehaviour
    {
        private const float CanvasDistance = 1f;
        private const float OutlineWidth = 0.003f;
        private const float HoverWidth = 0.005f;
        private const float SelectedWidth = 0.007f;
        private const float ContrastBorderWidth = 0.004f;
        private const float ResizeSpeed = 0.7f;
        private const float HudSpeed = 0.45f;
        private const float HudBound = 0.3f;
        private const float StickDeadzone = 0.2f;
        private const float InstructionDuration = 3f;
        private const float RayLength = 4f;
        private const float AcceptPulseDuration = 0.1f;
        private const float ClosurePulseDuration = 0.06f;
        private const int TextRenderQueue = 3020;
        private const int InstructionColumns = 38;
        private const int RingSegments = 24;
        private const string CoverageInstruction = "Draw nearer the center of the view.\nHold left X: original view.";
        private enum BlockedAction { None, Stroke, Acceptance, Movement }
        private readonly RegionSession session = new RegionSession();
        private readonly InteractionFeedback feedback = new InteractionFeedback();
        private readonly Dictionary<RegionData, OutlinePair> outlines = new Dictionary<RegionData, OutlinePair>();
        private readonly Dictionary<RegionData, OutlinePair> copyOutlines = new Dictionary<RegionData, OutlinePair>();
        private readonly Dictionary<RegionData, LiveRegionView> views = new Dictionary<RegionData, LiveRegionView>();
        private ICameraFrameSource source;
        private Transform centerEye;
        private Transform controller;
        private Transform plane;
        private Transform hud;
        private TextMesh instruction;
        private BoxCollider acceptCollider;
        private OutlinePair strokeLine;
        private OutlinePair endpoint;
        private OutlinePair startRing;
        private LineRenderer rayLine;
        private Material liveMaterial;
        private Material outlineMaterial;
        private Material darkOutlineMaterial;
        private Material buttonMaterial;
        private Material textMaterial;
        private Font textFont;
        private RegionData selected;
        private RegionData hovered;
        private Vector2 dragOffset;
        private bool dragging;
        private bool paused;
        private bool focused = true;
        private bool originalView;
        private bool closureSignaled;
        private bool hapticsActive;
        private string temporaryInstruction;
        private string actionInstruction;
        private string displayedInstruction;
        private BlockedAction blockedAction;
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
            outlineMaterial = new Material(overlayShader) { name = "Light region outlines", enableInstancing = true, renderQueue = 3011 };
            outlineMaterial.SetColor("_Color", Color.white);
            darkOutlineMaterial = new Material(overlayShader) { name = "Dark outline borders", enableInstancing = true };
            darkOutlineMaterial.SetColor("_Color", Color.black);
            buttonMaterial = new Material(overlayShader) { name = "HUD backplates", enableInstancing = true, renderQueue = 3012 };
            buttonMaterial.SetColor("_Color", Color.black);
            plane = new GameObject("Head-relative drawing plane").transform;
            plane.SetParent(centerEye, false); plane.localPosition = Vector3.forward * CanvasDistance;
            hud = new GameObject("Movable instructions and Accept").transform;
            hud.SetParent(plane, false); hud.localPosition = new Vector3(0, 0.29f, -0.015f);
            CreateBackplate("Instruction backplate", new Vector3(0, 0.055f, 0), new Vector3(0.76f, 0.15f, 1));
            instruction = CreateText("Instructions", StageInstruction(), new Vector3(0, 0.055f, -0.005f), 0.027f);
            CreateAcceptButton();
            strokeLine = CreateOutline("Current stroke");
            startRing = CreateOutline("Stroke start ring"); endpoint = CreateOutline("Controller endpoint");
            rayLine = CreateLine("Right controller ray", true, outlineMaterial);
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null) controller = rig.rightControllerAnchor;
        }

        private void Update()
        {
            if (plane == null) return;
            if (hapticsActive && !feedback.IsPulsing(Time.unscaledTime)) StopHaptics();
            // This remains available during camera permission/errors and right-controller loss.
            if (!paused && focused)
            {
                Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
                if (stick.sqrMagnitude > StickDeadzone * StickDeadzone) MoveHud(stick * HudSpeed * Time.unscaledDeltaTime);
            }
            if (!UpdateAvailability(HeadTracked(), controller != null && ControllerTracked(), OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch))) return;
            Ray ray = new Ray(controller.position, controller.forward);
            bool canvasHit = TryCanvasHit(ray, out Vector2 hit);
            rayLine.enabled = true; rayLine.positionCount = 2;
            rayLine.SetPosition(0, ray.origin);
            rayLine.SetPosition(1, canvasHit ? plane.TransformPoint(new Vector3(hit.x, hit.y, 0)) : ray.GetPoint(CanvasDistance));
            HandleButtons(ray); HandleStroke(canvasHit, hit); HandleCopyMovement(canvasHit, hit);
            bool allCovered = UpdateLiveRegions();
            UpdateStrokeOutline(); UpdateRing(endpoint, canvasHit, hit, 0.009f, OutlineWidth);
            SetInstruction(!allCovered ? CoverageInstruction : actionInstruction ?? (Time.unscaledTime < instructionUntil ? temporaryInstruction : StageInstruction()));
        }

        private bool UpdateAvailability(bool headTracked, bool rightControllerTracked, bool showOriginal)
        {
            if (paused || !focused || !headTracked)
            {
                SuspendInteraction();
                SetInstruction(paused || !focused ? "Keep GazeShift open; wait for camera." : "Wear headset to resume the live view.\nLeft stick: move this panel.");
                return false;
            }
            if (!source.IsReady || source.FrameTexture == null)
            {
                SuspendInteraction();
                SetInstruction((source is QuestCameraSource quest ? quest.StatusInstruction : "Allow camera access in Quest settings; reopen GazeShift.") + "\nLeft stick: move this panel.");
                return false;
            }
            if (!rightControllerTracked)
            {
                session.CancelStroke(); hovered = null; dragging = false; closureSignaled = false;
                StopHaptics(); UpdateStrokeOutline(); endpoint.SetEnabled(false); rayLine.enabled = false;
            }
            SetOriginalView(showOriginal);
            if (originalView) { SetInstruction("Original view: release left X to return.\nLeft stick: move this panel."); return false; }
            if (!rightControllerTracked)
            {
                // Controller sleep cancels input; fresh head/camera data still drives accepted imagery.
                bool allCovered = UpdateLiveRegions();
                SetInstruction(!allCovered ? CoverageInstruction : "Connect right controller to draw or move.\nLeft stick: panel. Hold left X: original.");
                return false;
            }
            return true;
        }

        private void MoveHud(Vector2 delta)
        {
            if (hud == null || !RegionGeometry.IsFinite(delta)) return;
            Vector3 position = hud.localPosition;
            position.x = Mathf.Clamp(position.x + delta.x, -HudBound, HudBound);
            position.y = Mathf.Clamp(position.y + delta.y, -HudBound, HudBound);
            hud.localPosition = position;
        }
        private void HandleButtons(Ray ray)
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) AcceptRegions();
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) UndoLastRegion();
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)) BeginTrigger(ray);
        }
        private void BeginTrigger(Ray ray)
        {
            Physics.SyncTransforms();
            bool hitAccept = acceptCollider.Raycast(ray, out _, RayLength);
            session.BeginTrigger(hitAccept); closureSignaled = false;
            if (hitAccept) AcceptRegions();
        }
        private void HandleStroke(bool canvasHit, Vector2 hit)
        {
            if (session.IsDrawing && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {
                Vector3 world = plane.TransformPoint(new Vector3(hit.x, hit.y, 0));
                if (!canvasHit || !source.TryWorldToTextureUv(world, out _))
                { session.CancelStroke(); ShowActionInstruction(BlockedAction.Stroke, CoverageInstruction); }
                else
                {
                    session.AddPoint(hit);
                    if (session.StrokeOverflowed) ShowActionInstruction(BlockedAction.Stroke, "Loop too long. Trace a shorter loop.\nPress trigger again to restart.");
                    if (session.IsClosureReady && !closureSignaled) { Pulse(0.3f, ClosurePulseDuration); closureSignaled = true; }
                }
            }
            if (!OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) || !session.IsDrawing) return;
            if (!session.FinishStroke()) ShowActionInstruction(BlockedAction.Stroke, "Trace a larger loop away from copies.\nReturn to start, then release.");
            else ClearActionInstruction(BlockedAction.Stroke);
            SyncOutlines();
        }
        private void HandleCopyMovement(bool canvasHit, Vector2 hit)
        {
            hovered = canvasHit ? FindRegion(hit) : null;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
            { if (canvasHit) BeginDrag(hit); else { selected = null; dragging = false; session.CancelStroke(); } }
            if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) dragging = false;
            if (selected == null) return;
            if (dragging && canvasHit && OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) DragTo(hit);
            float stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).y;
            if (Mathf.Abs(stick) > StickDeadzone)
            {
                float scale = RegionSession.ClampScale(selected.Scale, stick * ResizeSpeed * Time.unscaledDeltaTime);
                if (scale != selected.Scale) MoveSelected(selected.Destination, scale);
            }
        }
        private RegionData FindRegion(Vector2 hit)
        {
            for (int i = session.Accepted.Count - 1; i >= 0; i--)
            {
                RegionData region = session.Accepted[i];
                if (views[region].IsVisible && (region.ContainsCopy(hit) || RegionGeometry.ContainsPoint(region.Source, hit))) return region;
            }
            return null;
        }
        private void BeginDrag(Vector2 hit)
        {
            session.CancelStroke(); selected = FindRegion(hit); dragging = selected != null;
            if (selected != null) dragOffset = selected.ContainsCopy(hit) ? selected.Destination - hit : Vector2.zero;
        }
        private void DragTo(Vector2 hit) { if (selected != null) MoveSelected(hit + dragOffset, selected.Scale); }
        private void MoveSelected(Vector2 destination, float scale)
        {
            if (session.TryPlace(selected, destination, scale)) { ClearActionInstruction(BlockedAction.Movement); return; }
            ShowActionInstruction(BlockedAction.Movement, "Keep copies inside the view and apart.\nMove elsewhere or right stick down.");
            if (feedback.TryReject(Time.unscaledTime)) Pulse(0.25f, AcceptPulseDuration);
        }
        private void UndoLastRegion()
        {
            session.UndoLast(); selected = null; hovered = null; dragging = false;
            actionInstruction = null; blockedAction = BlockedAction.None;
            SyncOutlines(); UpdateStrokeOutline();
        }
        private void AcceptRegions()
        {
            if (session.Pending.Count == 0) return;
            for (int i = 0; i < session.Pending.Count; i++)
                for (int j = 0; j < session.Pending[i].Source.Length; j++)
                {
                    Vector2 point = session.Pending[i].Source[j];
                    if (source.TryWorldToTextureUv(plane.TransformPoint(new Vector3(point.x, point.y, 0)), out _)) continue;
                    session.CancelStroke(); ShowActionInstruction(BlockedAction.Acceptance, CoverageInstruction); return;
                }
            if (!session.AcceptPending())
            { ShowActionInstruction(BlockedAction.Acceptance, "No room for a full-size copy.\nB: undo; trace a smaller region."); return; }
            for (int i = 0; i < session.Accepted.Count; i++)
            {
                RegionData region = session.Accepted[i];
                if (!views.ContainsKey(region)) views.Add(region, new LiveRegionView(region, plane, liveMaterial));
            }
            selected = session.Accepted[session.Accepted.Count - 1]; dragging = false;
            ClearActionInstruction(BlockedAction.Acceptance); SyncOutlines();
            ShowTemporaryInstruction("Copy selected: " + CopyDirection(selected.Destination) + ".\nRight stick: resize. Grip: move.");
            Pulse(0.55f, AcceptPulseDuration);
        }
        private static string CopyDirection(Vector2 point)
        {
            const float centerThreshold = 0.1f;
            string horizontal = point.x < -centerThreshold ? "left" : point.x > centerThreshold ? "right" : "";
            string vertical = point.y < -centerThreshold ? "lower" : point.y > centerThreshold ? "upper" : "";
            return vertical.Length == 0 ? horizontal.Length == 0 ? "center" : horizontal : horizontal.Length == 0 ? vertical : vertical + " " + horizontal;
        }
        private bool UpdateLiveRegions()
        {
            if (originalView) { HideEffects(); return true; }
            bool covered = true;
            foreach (KeyValuePair<RegionData, LiveRegionView> pair in views)
            {
                bool visible = pair.Value.UpdateFrame(source);
                outlines[pair.Key].SetEnabled(visible); copyOutlines[pair.Key].SetEnabled(visible);
                OutlinePair copy = copyOutlines[pair.Key]; copy.SetCount(pair.Key.Source.Length);
                for (int i = 0; i < pair.Key.Source.Length; i++)
                { Vector2 point = pair.Key.CopyPoint(i); copy.SetPoint(i, new Vector3(point.x, point.y, -0.006f)); }
                float width = pair.Key == selected ? SelectedWidth : pair.Key == hovered ? HoverWidth : OutlineWidth;
                copy.SetStyle(width, pair.Key == selected ? Color.yellow : Color.white);
                outlines[pair.Key].SetStyle(pair.Key == hovered ? HoverWidth : OutlineWidth, Color.white);
                if (!visible) covered = false;
            }
            for (int i = 0; i < session.Pending.Count; i++) outlines[session.Pending[i]].SetEnabled(true);
            return covered;
        }
        private void SyncOutlines()
        {
            var removed = new List<RegionData>();
            foreach (RegionData region in outlines.Keys)
                if (!session.Pending.Contains(region) && !session.Accepted.Contains(region)) removed.Add(region);
            for (int i = 0; i < removed.Count; i++)
            {
                RegionData region = removed[i]; outlines[region].Dispose(); outlines.Remove(region);
                if (copyOutlines.TryGetValue(region, out OutlinePair copy)) { copy.Dispose(); copyOutlines.Remove(region); }
                if (views.TryGetValue(region, out LiveRegionView view)) { view.Dispose(); views.Remove(region); }
            }
            for (int i = 0; i < session.Pending.Count + session.Accepted.Count; i++)
            {
                RegionData region = i < session.Pending.Count ? session.Pending[i] : session.Accepted[i - session.Pending.Count];
                if (!outlines.ContainsKey(region))
                {
                    OutlinePair line = CreateOutline("Source boundary"); line.SetLoop(true); line.SetCount(region.Source.Length);
                    for (int j = 0; j < region.Source.Length; j++) line.SetPoint(j, new Vector3(region.Source[j].x, region.Source[j].y, -0.004f));
                    outlines.Add(region, line);
                }
                if (session.Accepted.Contains(region) && !copyOutlines.ContainsKey(region))
                { OutlinePair copy = CreateOutline("Copy boundary"); copy.SetLoop(true); copyOutlines.Add(region, copy); }
            }
        }
        private void UpdateStrokeOutline()
        {
            strokeLine.SetEnabled(session.IsDrawing); strokeLine.SetCount(session.Stroke.Count);
            for (int i = 0; i < session.Stroke.Count; i++) strokeLine.SetPoint(i, new Vector3(session.Stroke[i].x, session.Stroke[i].y, -0.004f));
            UpdateRing(startRing, session.IsDrawing && session.Stroke.Count > 0, session.Stroke.Count > 0 ? session.Stroke[0] : Vector2.zero, 0.014f, session.IsClosureReady ? SelectedWidth : OutlineWidth);
        }
        private static void UpdateRing(OutlinePair ring, bool visible, Vector2 center, float radius, float width)
        {
            ring.SetEnabled(visible); if (!visible) return;
            ring.SetLoop(true); ring.SetCount(RingSegments); ring.SetStyle(width, Color.white);
            for (int i = 0; i < RingSegments; i++)
            { float angle = i * Mathf.PI * 2 / RingSegments; ring.SetPoint(i, new Vector3(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, -0.009f)); }
        }
        private void SetOriginalView(bool showOriginal)
        {
            if (originalView == showOriginal) return;
            originalView = showOriginal; session.CancelStroke(); dragging = false; hovered = null;
            StopHaptics(); HideEffects();
            // Release deliberately does not enable a mesh; UpdateFrame must validate fresh coverage.
        }
        private bool TryCanvasHit(Ray ray, out Vector2 point) => RegionGeometry.TryCanvasHit(plane.InverseTransformPoint(ray.origin), plane.InverseTransformDirection(ray.direction), RegionSession.CanvasHalfExtent, out point);
        private static bool HeadTracked() => InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        private static bool ControllerTracked() => OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch) && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
        private string StageInstruction()
        {
            if (session.IsDrawing) return session.IsClosureReady ? "Loop ready: release right trigger.\nB: undo. Hold left X: original view." : "Return to the start ring; release.\nB: undo. Hold left X: original view.";
            if (session.Pending.Count > 0) return "Press A or Accept regions.\nB: undo. Left stick: move this panel.";
            if (selected != null) return "Right stick: resize. Grip: move.\nB: undo. Hold left X: original view.";
            if (session.Accepted.Count > 0) return "Grip a source or copy to move it.\nLeft stick: panel. Hold left X: original.";
            return "Hold right trigger: trace a loop.\nLeft stick: move this panel.";
        }
        private void ShowTemporaryInstruction(string text) { temporaryInstruction = text; instructionUntil = Time.unscaledTime + InstructionDuration; }
        private void ShowActionInstruction(BlockedAction action, string text) { blockedAction = action; actionInstruction = text; }
        private void ClearActionInstruction(BlockedAction action) { if (blockedAction == action) { blockedAction = BlockedAction.None; actionInstruction = null; } }
        private void SetInstruction(string text)
        {
            if (instruction == null || displayedInstruction == text) return;
            displayedInstruction = text;
            var wrapped = new StringBuilder();
            foreach (string paragraph in text.Split('\n'))
            {
                if (wrapped.Length > 0) wrapped.Append('\n');
                int columns = 0;
                foreach (string word in paragraph.Split(' '))
                {
                    if (columns > 0 && columns + 1 + word.Length > InstructionColumns) { wrapped.Append('\n'); columns = 0; }
                    if (columns > 0) { wrapped.Append(' '); columns++; }
                    wrapped.Append(word); columns += word.Length;
                }
            }
            instruction.text = wrapped.ToString();
        }
        private void Pulse(float amplitude, float duration)
        {
            feedback.BeginPulse(Time.unscaledTime, duration); hapticsActive = true;
            if (Application.isPlaying) OVRInput.SetControllerVibration(0.5f, amplitude, OVRInput.Controller.RTouch);
        }
        private void StopHaptics()
        {
            feedback.Stop();
            if (hapticsActive && Application.isPlaying) OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
            hapticsActive = false;
        }
        private void HideEffects()
        {
            if (strokeLine != null) { strokeLine.SetCount(0); strokeLine.SetEnabled(false); }
            if (startRing != null) startRing.SetEnabled(false);
            if (endpoint != null) endpoint.SetEnabled(false);
            if (rayLine != null) rayLine.enabled = false;
            foreach (LiveRegionView view in views.Values) view.SetVisible(false);
            foreach (OutlinePair line in outlines.Values) line.SetEnabled(false);
            foreach (OutlinePair line in copyOutlines.Values) line.SetEnabled(false);
        }
        private void SuspendInteraction()
        {
            session.CancelStroke(); selected = null; hovered = null; dragging = false;
            StopHaptics(); HideEffects();
        }
        private void OnApplicationPause(bool pause) { paused = pause; if (pause) SuspendInteraction(); }
        private void OnApplicationFocus(bool focus) { focused = focus; if (!focus) SuspendInteraction(); }
        private void OnDisable() => SuspendInteraction();
        private void OnDestroy()
        {
            StopHaptics(); foreach (LiveRegionView view in views.Values) view.Dispose();
            Font.textureRebuilt -= RefreshFontTexture;
            if (plane != null) DestroyOwned(plane.gameObject);
            DestroyOwned(liveMaterial); DestroyOwned(outlineMaterial); DestroyOwned(darkOutlineMaterial); DestroyOwned(buttonMaterial); DestroyOwned(textMaterial);
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
        private LineRenderer CreateLine(string name, bool worldSpace, Material material)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(plane, false); line.useWorldSpace = worldSpace;
            line.sharedMaterial = material; line.widthMultiplier = OutlineWidth;
            line.numCapVertices = 2; line.positionCount = 0; line.enabled = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        private OutlinePair CreateOutline(string name) => new OutlinePair(CreateLine(name + " outer", false, darkOutlineMaterial), CreateLine(name + " inner", false, outlineMaterial));
        private sealed class OutlinePair
        {
            private readonly LineRenderer outer;
            private readonly LineRenderer inner;
            public OutlinePair(LineRenderer border, LineRenderer line) { outer = border; inner = line; SetStyle(OutlineWidth, Color.white); }
            public void SetEnabled(bool value) { outer.enabled = value; inner.enabled = value; }
            public void SetCount(int count) { outer.positionCount = count; inner.positionCount = count; }
            public void SetLoop(bool value) { outer.loop = value; inner.loop = value; }
            public void SetPoint(int index, Vector3 point) { outer.SetPosition(index, point); inner.SetPosition(index, point); }
            public void SetStyle(float width, Color color) { outer.widthMultiplier = width + ContrastBorderWidth; inner.widthMultiplier = width; inner.startColor = color; inner.endColor = color; }
            public void Dispose() { DestroyOwned(outer.gameObject); DestroyOwned(inner.gameObject); }
        }
        private TextMesh CreateText(string name, string text, Vector3 position, float size)
        {
            var label = new GameObject(name, typeof(TextMesh)).GetComponent<TextMesh>();
            label.transform.SetParent(hud, false); label.transform.localPosition = position;
            label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            if (textFont == null)
            {
                textFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                textMaterial = new Material(textFont.material) { name = "GazeShift text", renderQueue = TextRenderQueue };
                Font.textureRebuilt += RefreshFontTexture;
            }
            label.font = textFont; label.GetComponent<MeshRenderer>().sharedMaterial = textMaterial;
            label.fontSize = 64; label.characterSize = size; label.color = Color.white;
            return label;
        }
        private GameObject CreateBackplate(string name, Vector3 position, Vector3 scale)
        {
            GameObject backplate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backplate.name = name; backplate.transform.SetParent(hud, false);
            backplate.transform.localPosition = position; backplate.transform.localScale = scale;
            DestroyOwned(backplate.GetComponent<Collider>()); backplate.GetComponent<MeshRenderer>().sharedMaterial = buttonMaterial;
            return backplate;
        }
        private void CreateAcceptButton()
        {
            Vector3 position = new Vector3(0, -0.08f, -0.001f);
            GameObject button = CreateBackplate("Accept regions", position, new Vector3(0.28f, 0.07f, 1));
            acceptCollider = button.AddComponent<BoxCollider>(); acceptCollider.size = new Vector3(1, 1, 0.01f);
            CreateText("Accept regions label", "Accept regions", position + Vector3.back * 0.005f, 0.031f);
        }
    }
}
