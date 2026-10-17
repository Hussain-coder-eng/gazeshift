using UnityEngine;

namespace GazeShift
{
    public sealed class GazeShiftBootstrap : MonoBehaviour
    {
        private const int CameraWidth = 1280;
        private const int CameraHeight = 960;
        private const int CameraFramerate = 60;
        private const float NearClipMeters = 0.05f;

        private void Awake()
        {
            RenderSettings.skybox = null;
            var rigObject = new GameObject("Quest camera rig");
            rigObject.SetActive(false);
            var manager = rigObject.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            manager.trackingOriginType = OVRManager.TrackingOrigin.Stage;
            manager.usePositionTracking = true;
            manager.useRotationTracking = true;
            var rig = rigObject.AddComponent<OVRCameraRig>();
            rig.EnsureGameObjectIntegrity();
            foreach (Camera eye in rig.GetComponentsInChildren<Camera>(true))
            {
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = Color.clear;
                eye.nearClipPlane = NearClipMeters;
            }
            rigObject.AddComponent<OVRPassthroughLayer>().overlayType = OVROverlay.OverlayType.Underlay;
            var source = rigObject.AddComponent<QuestCameraSource>();
            var cameraAccess = rigObject.GetComponent<Meta.XR.PassthroughCameraAccess>();
            cameraAccess.enabled = false;
            cameraAccess.CameraPosition = Meta.XR.PassthroughCameraAccess.CameraPositionType.Left;
            cameraAccess.RequestedResolution = new Vector2Int(CameraWidth, CameraHeight);
            cameraAccess.MaxFramerate = CameraFramerate;
            cameraAccess.enabled = true;
            rigObject.SetActive(true);
            gameObject.AddComponent<RegionInteraction>().Initialize(source, rig.centerEyeAnchor);
        }
    }
}
