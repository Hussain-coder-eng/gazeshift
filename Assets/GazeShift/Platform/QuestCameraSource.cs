using System;
using Meta.XR;
using UnityEngine;

namespace GazeShift
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PassthroughCameraAccess))]
    public sealed class QuestCameraSource : MonoBehaviour, ICameraFrameSource
    {
        private readonly CameraFrameGate freshness = new CameraFrameGate();
        private PassthroughCameraAccess cameraAccess;
        private Pose framePose;
        private Texture frameTexture;
        private bool supported;
        private bool permissionGranted;
        private bool paused;
        private string cameraError;

        public bool IsReady => isActiveAndEnabled && !paused && supported && permissionGranted
            && cameraAccess != null && cameraAccess.isActiveAndEnabled && cameraAccess.IsPlaying
            && frameTexture != null && freshness.IsFresh(Time.realtimeSinceStartupAsDouble);

        public Texture FrameTexture => IsReady ? frameTexture : null;

        public string StatusInstruction
        {
            get
            {
                if (cameraError != null)
                    return "Camera unavailable. Close and reopen GazeShift.";
                if (!supported)
                    return "Use Quest 3 or 3S with Horizon OS v81 or newer.";
                if (!permissionGranted)
                    return "Allow camera access in Quest settings, then reopen GazeShift.";
                if (cameraAccess == null || !cameraAccess.isActiveAndEnabled)
                    return "Camera unavailable. Close and reopen GazeShift.";
                return "Keep GazeShift open and wait for the camera.";
            }
        }

        private void Awake()
        {
            cameraAccess = GetComponent<PassthroughCameraAccess>();
        }

        private void OnEnable()
        {
            ClearFrame();
            supported = false;
            permissionGranted = false;
            cameraError = null;
            try
            {
                supported = PassthroughCameraAccess.IsSupported;
                if (!supported)
                {
                    Debug.LogWarning(StatusInstruction, this);
                    return;
                }
                permissionGranted = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
                OVRPermissionsRequester.Request(new[] { OVRPermissionsRequester.Permission.PassthroughCameraAccess });
            }
            catch (Exception exception) { ReportCameraError(exception); }
        }

        private void Update()
        {
            if (paused || !supported || cameraAccess == null)
            {
                ClearFrame();
                return;
            }
            try
            {
                permissionGranted = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
                if (!permissionGranted || !cameraAccess.isActiveAndEnabled || !cameraAccess.IsPlaying)
                {
                    ClearFrame();
                    return;
                }
                if (!cameraAccess.IsUpdatedThisFrame) return;
                // Cache one image-timestamp pose so every polygon vertex uses the same capture projection.
                framePose = cameraAccess.GetCameraPose();
                frameTexture = cameraAccess.GetTexture();
                if (frameTexture == null) { ClearFrame(); return; }
                freshness.ObserveUpdate(Time.realtimeSinceStartupAsDouble);
                cameraError = null;
            }
            catch (Exception exception) { ReportCameraError(exception); }
        }

        public bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv)
        {
            uv = default;
            if (!IsReady || !IsFinite(worldPoint.x) || !IsFinite(worldPoint.y) || !IsFinite(worldPoint.z))
                return false;
            if (Vector3.Dot(worldPoint - framePose.position, framePose.rotation * Vector3.forward) <= 0)
                return false;
            Vector2 projected = cameraAccess.WorldToViewportPoint(worldPoint, framePose);
            if (!IsFinite(projected.x) || !IsFinite(projected.y)
                || projected.x < 0 || projected.x > 1 || projected.y < 0 || projected.y > 1)
                return false;
            uv = projected;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            ClearFrame();
        }

        private void OnDisable() => ClearFrame();

        private void ClearFrame()
        {
            freshness.Clear();
            frameTexture = null;
        }

        private void ReportCameraError(Exception exception)
        {
            if (cameraError == null) Debug.LogError($"GazeShift camera unavailable: {exception}", this);
            cameraError = exception.Message;
            ClearFrame();
        }
    }
}
