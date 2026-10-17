# GazeShift for Meta Quest 3S

A manual mixed-reality image-relocation experiment. Draw polygons with the right controller over full-color passthrough, accept the regions, and move/rescale sharp live camera-image copies while the source polygons remain blurred. The app opens directly into passthrough; there is no front-camera setup or eye tracking.

## Controls

1. Hold the right trigger and trace a closed loop. Release near the start. Repeat for more regions; B undoes the latest unaccepted region.
2. Point at **Accept regions** and press the trigger, or press A.
3. Point at an accepted region or copy and hold grip to move its sharp copy. Move the right thumbstick up/down to resize the selected copy.

Source regions stay fixed relative to the headset. Invalid copies outside the working area or over a source region retain their last valid placement. Camera loss removes blur/copies until fresh imagery returns. Only instructions and **Accept regions** appear as text; diagnostics stay in logs.

## Setup and build

Use **Unity 6000.3.19f1**, Android Build Support with SDK/NDK and OpenJDK, an active Unity license, and **Quest 3 or Quest 3S with Horizon OS v81 or newer**. An Intel editor on Apple Silicon also needs Rosetta. Grant headset camera permission when asked.

Open the repository root as a Unity project. Versions are pinned: Meta XR Core/MRUK **85.0.0**, OpenXR **1.15.1**, XR Management **4.5.4**. Packages resolve through Unity's registry and the official Meta registry.

Choose **GazeShift → Configure Quest project** to regenerate the scene, Android/OpenXR settings, camera permission configuration, and required shader inclusion. Choose **GazeShift → Build Quest APK** to build Android ARM64 IL2CPP to `Builds/GazeShift.apk`. `GAZESHIFT_APK_PATH` may override the APK output path.

With the Unity CLI installed:

```sh
unity run . -- -executeMethod GazeShift.Editor.QuestProjectSetup.Configure
unity test . --mode EditMode --output test-results.xml
unity run . -- -executeMethod GazeShift.Editor.QuestProjectSetup.BuildAndroid
```

The CLI launches the pinned editor and manages batch/quit flags. A direct editor invocation also works:

```sh
Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod GazeShift.Editor.QuestProjectSetup.Configure
Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults test-results.xml
Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod GazeShift.Editor.QuestProjectSetup.BuildAndroid
```

The manual GitHub Actions workflow requires repository Unity license credentials; their presence is not assumed. Never commit license files, signing keys, captured images, or APKs.

## Verification and limits

Tests cover concave polygon triangulation, winding, invalid strokes, overlap/bounds rejection, scale limits, controller action priority, camera projection failure, and camera freshness recovery. Tests and compilation cannot verify headset alignment or physical interaction.

Headset acceptance: launch straight into passthrough; grant/deny camera access; draw multiple regions; select Accept using the controller; verify source-only live blur; drag/resize live sharp copies; move the head; suspend/resume and interrupt camera input. Report which checks actually ran. An APK build is separate from installation and this interaction sequence.

The app duplicates camera pixels and covers destination content. Its head-relative masks are assumed regions, not retinal measurements. Camera coverage is smaller than headset passthrough, so effects disappear where source pixels cannot be sampled. The fixed plane is not scene reconstruction or validated binocular registration. No diagnosis, treatment, vision benefit, clinical result, measured latency, or frame-rate claim is established.

The retired Swift/ARKit/Metal prototype remains recoverable in Git history at `5a405be6b6fc1b7c6b5e9405bb2d044cb619e470`; the active tree contains the Quest app only. See the [migration design](docs/superpowers/specs/2026-10-17-quest-3s-migration-design.md) and [plan](docs/superpowers/plans/2026-10-17-quest-3s-migration.md).

SDK references: [Meta camera overview](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/), [MRUK camera reference](https://developers.meta.com/vr/reference/mruk/v85/class_meta_x_r_passthrough_camera_access/), [official camera samples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples).
