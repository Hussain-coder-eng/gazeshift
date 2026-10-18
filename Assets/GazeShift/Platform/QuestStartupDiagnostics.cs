using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR;

namespace GazeShift
{
    /// <summary>One bounded, read-only startup snapshot of the actual XR view.</summary>
    public sealed class QuestStartupDiagnostics : MonoBehaviour
    {
        private const double TimeoutSeconds = 15d;
        private const int NameLimit = 100;
        private const int RequiredDistinctFrames = 2;
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private OVRCameraRig rig;
        private OVRPassthroughLayer layer;
        private Camera centerCamera;
        private double startedAt;
        private int lastEligibleFrame = -1;
        private int eligibleFrames;
        private bool subscribed;
        private bool completed;
        private bool errorLogged;
        private string prefix;

        public void Initialize(OVRCameraRig cameraRig, OVRPassthroughLayer passthrough)
        {
            if (subscribed || completed) return;
            rig = cameraRig; layer = passthrough;
            centerCamera = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : null;
            startedAt = Time.realtimeSinceStartupAsDouble;
            Subscribe();
        }
        private void Subscribe()
        {
            if (subscribed || completed) return;
            Camera.onPreRender += HandleCameraPreRender; subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            Camera.onPreRender -= HandleCameraPreRender; subscribed = false;
        }
        private void Complete()
        {
            completed = true; Unsubscribe();
            prefix = FormattableString.Invariant($"[GazeShift XR startup frame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F6}] ");
        }
        private bool TryCompleteFrame(int frame, bool eligible)
        {
            if (completed || !eligible || frame == lastEligibleFrame) return false;
            lastEligibleFrame = frame;
            if (++eligibleFrames < RequiredDistinctFrames) return false;
            Complete(); return true;
        }
        private void Update()
        {
            if (completed || !subscribed || Time.realtimeSinceStartupAsDouble - startedAt < TimeoutSeconds) return;
            Complete();
            try { Log("timeout after 15s; " + Readiness()); }
            catch (Exception exception) { LogError(exception); }
        }
        private void HandleCameraPreRender(Camera camera)
        {
            if (completed || camera != centerCamera || centerCamera == null) return;
            try
            {
                SubsystemManager.GetSubsystems(displays);
                bool running = displays.Exists(display => display.running);
                bool eligible = Application.isFocused && camera.isActiveAndEnabled && camera.stereoEnabled &&
                    OVRManager.OVRManagerinitialized && OVRManager.hasVrFocus && running && OVRManager.IsInsightPassthroughInitialized();
                if (!TryCompleteFrame(Time.frameCount, eligible)) return;
                Log("ready; " + Readiness()); Capture();
            }
            catch (Exception exception)
            {
                if (!completed) Complete();
                LogError(exception);
            }
        }
        private string Readiness()
        {
            SubsystemManager.GetSubsystems(displays);
            return FormattableString.Invariant($"focused={Application.isFocused} exactCameraPresent={centerCamera != null} cameraActive={centerCamera != null && centerCamera.isActiveAndEnabled} stereo={centerCamera != null && centerCamera.stereoEnabled} managerInitialized={OVRManager.OVRManagerinitialized} vrFocus={OVRManager.hasVrFocus} runningDisplay={displays.Exists(display => display.running)} passthroughInitialized={OVRManager.IsInsightPassthroughInitialized()} eligibleDistinctFrames={eligibleFrames}");
        }
        private void Capture()
        {
            Log(FormattableString.Invariant($"screen width={Screen.width} height={Screen.height} orientation={Screen.orientation}"));
            LogPose("rig", rig.transform); LogPose("tracking", rig.trackingSpace); LogPose("centerEye", rig.centerEyeAnchor);
            foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true)) LogCamera(camera);
            foreach (Camera.StereoscopicEye eye in new[] { Camera.StereoscopicEye.Left, Camera.StereoscopicEye.Right })
            {
                LogMatrix("camera " + eye + " projection", centerCamera.GetStereoProjectionMatrix(eye), true);
                LogMatrix("camera " + eye + " view", centerCamera.GetStereoViewMatrix(eye), false);
            }
            CaptureDisplays();
            Log(FormattableString.Invariant($"passthrough present={layer != null} active={layer != null && layer.gameObject.activeInHierarchy} enabled={layer != null && layer.enabled} initialized={OVRManager.IsInsightPassthroughInitialized()} managerInitialized={OVRManager.OVRManagerinitialized}"));
            if (layer != null) Log(FormattableString.Invariant($"passthrough overlayType={layer.overlayType} projectionSurfaceType={layer.projectionSurfaceType} hidden={layer.hidden} textureOpacity={layer.textureOpacity:F6}"));
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                string name = Name(renderer.name);
                Log("renderer " + name + " localBounds=" + BoundsText(renderer.localBounds) + " worldBounds=" + BoundsText(renderer.bounds) + " localScale=" + Vector(renderer.transform.localScale) + " lossyScale=" + Vector(renderer.transform.lossyScale));
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                    Log(FormattableString.Invariant($"renderer {name} material[{index}] shader={Name(materials[index] != null && materials[index].shader != null ? materials[index].shader.name : "unavailable")}"));
            }
            LogFrustum(OVRPlugin.Node.EyeLeft); LogFrustum(OVRPlugin.Node.EyeRight);
        }
        private void CaptureDisplays()
        {
            SubsystemManager.GetSubsystems(displays);
            for (int displayIndex = 0; displayIndex < displays.Count; displayIndex++)
            {
                XRDisplaySubsystem display = displays[displayIndex];
                if (!display.running) continue;
                int count = display.GetRenderPassCount();
                string id = FormattableString.Invariant($"display[{displayIndex}]");
                Log(FormattableString.Invariant($"{id} running=true viewportScale={display.scaleOfAllViewports:F6} renderTargetScale={display.scaleOfAllRenderTargets:F6} passes={count}"));
                for (int passIndex = 0; passIndex < count; passIndex++)
                {
                    display.GetRenderPass(passIndex, out XRDisplaySubsystem.XRRenderPass pass);
                    var descriptor = pass.renderTargetDesc;
                    int parameters = pass.GetRenderParameterCount();
                    string passId = FormattableString.Invariant($"{id} pass[{passIndex}]");
                    Log(FormattableString.Invariant($"{passId} target width={descriptor.width} height={descriptor.height} dimension={descriptor.dimension} volumeDepth={descriptor.volumeDepth} parameters={parameters}"));
                    for (int index = 0; index < parameters; index++)
                    {
                        pass.GetRenderParameter(centerCamera, index, out XRDisplaySubsystem.XRRenderParameter parameter);
                        string parameterId = FormattableString.Invariant($"{passId} parameter[{index}]");
                        Log(FormattableString.Invariant($"{parameterId} viewport={parameter.viewport.ToString("F6", Culture)} textureArraySlice={parameter.textureArraySlice}"));
                        LogMatrix(parameterId + " projection", parameter.projection, true);
                        LogMatrix(parameterId + " view", parameter.view, false);
                    }
                }
            }
        }
        private void LogCamera(Camera camera)
        {
            Log(FormattableString.Invariant($"camera {Name(camera.name)} exactCenter={camera == centerCamera} active={camera.gameObject.activeInHierarchy} enabled={camera.enabled} stereo={camera.stereoEnabled} targetEyes={camera.stereoTargetEye} rect={camera.rect.ToString("F6", Culture)} pixelRect={camera.pixelRect.ToString("F6", Culture)} near={camera.nearClipPlane:F6} far={camera.farClipPlane:F6} targetTexture={Name(camera.targetTexture != null ? camera.targetTexture.name : "none")} lossyScale={Vector(camera.transform.lossyScale)}"));
        }
        private void LogPose(string name, Transform transform)
        {
            if (transform == null) { Log(name + " unavailable"); return; }
            Log(name + " localPosition=" + Vector(transform.localPosition) + " localRotation=" + transform.localRotation.ToString("F6", Culture) +
                " worldPosition=" + Vector(transform.position) + " worldRotation=" + transform.rotation.ToString("F6", Culture) +
                " localScale=" + Vector(transform.localScale) + " lossyScale=" + Vector(transform.lossyScale));
        }
        private void LogMatrix(string name, Matrix4x4 matrix, bool projection)
        {
            Log(name + " raw=" + matrix.ToString("F6", Culture).Replace('\n', ';'));
            if (projection) Log(name + " firstTwoRows=" + matrix.GetRow(0).ToString("F6", Culture) + ";" + matrix.GetRow(1).ToString("F6", Culture));
        }
        private void LogFrustum(OVRPlugin.Node eye)
        {
            bool available = OVRPlugin.GetNodeFrustum2(eye, out OVRPlugin.Frustumf2 frustum);
            if (!available) { Log("frustum " + eye + " return=false unavailable"); return; }
            Log(FormattableString.Invariant($"frustum {eye} return=true LeftTan={frustum.Fov.LeftTan:F6} RightTan={frustum.Fov.RightTan:F6} UpTan={frustum.Fov.UpTan:F6} DownTan={frustum.Fov.DownTan:F6} zNear={frustum.zNear:F6} zFar={frustum.zFar:F6}"));
        }
        private static string Vector(Vector3 vector) => vector.ToString("F6", Culture);
        private static string BoundsText(Bounds bounds) => "center=" + Vector(bounds.center) + " size=" + Vector(bounds.size);
        private static string Name(string name) => name.Length <= NameLimit ? name : name.Substring(0, NameLimit);
        private void Log(string record) => Debug.Log(prefix + record, this);
        private void LogError(Exception exception)
        {
            if (errorLogged) return;
            errorLogged = true;
            Debug.LogError(prefix + "diagnostic boundary error=" + exception.GetType().Name + " " + Name(exception.Message), this);
        }
        private void OnDisable() { completed = true; Unsubscribe(); }
        private void OnDestroy() { completed = true; Unsubscribe(); }
    }
}
