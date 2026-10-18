# GazeShift for Meta Quest 3S

A manual mixed-reality image-relocation experiment. Draw polygons with the right controller over full-color passthrough, accept the regions, and move/rescale sharp live camera-image copies while the source polygons remain blurred. The app opens directly into passthrough; there is no front-camera setup or eye tracking.

## Controls

1. Hold the right trigger and trace a closed loop. Release near the start. Repeat for more regions; B undoes the newest unaccepted region, or the newest accepted region when none are pending.
2. Point at **Accept regions** and press the trigger, or press A.
3. Point at an accepted region or copy and hold grip to move its sharp copy. Move the right thumbstick up/down to resize the selected copy. Accepting regions selects the newest copy.
4. Move the left thumbstick to reposition the instructions and Accept button. Hold left X to peek at the original view; release to restore effects after fresh camera coverage returns.

Source regions stay fixed relative to the headset. Invalid copies outside the working area or over a source region retain their last valid placement. Camera loss removes blur/copies until fresh imagery returns. Only instructions and **Accept regions** appear as text; diagnostics stay in logs.

## Setup and build

Use **Unity 6000.4.1f1**, Android Build Support with SDK/NDK and OpenJDK, an active Unity license, and **Quest 3 or Quest 3S with Horizon OS v81 or newer**. Use the native ARM64 editor on Apple Silicon. Grant headset camera permission when asked.

Open the repository root as a Unity project. Versions are pinned: Meta XR Core/MRUK **85.0.0**, OpenXR **1.16.1**, XR Management **4.5.4**. Packages resolve through Unity's registry and the official Meta registry.

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
Unity -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults test-results.xml
Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod GazeShift.Editor.QuestProjectSetup.BuildAndroid
```

Configuration also works without the Android module, so committed XR settings can be inspected and tested. Building an APK requires the Android module and its SDK/NDK/OpenJDK toolchain.

The manual GitHub Actions workflow requires repository Unity license credentials; their presence is not assumed. Never commit license files, signing keys, captured images, or APKs.

## Verification and limits

Local verification on October 18, 2026: a fresh native Unity 6000.4.1f1 run passed **58/58 EditMode tests**, with zero failures/skips and process exit 0, including forced desktop Metal shader compilation.

The canonical Unity `GazeShift.Editor.QuestProjectSetup.BuildAndroid` entrypoint subsequently completed **with process exit 0 and Build Report Success**, using the normal Android SDK/Gradle pipeline. All 1,506 native build nodes were evaluated successfully, including ARM64 IL2CPP and Vulkan shaders. This closes the full Unity build gate after the earlier disk-guard interruption and separate Gradle packaging recovery.

ZIP integrity, ARM64-only installable native libraries, APK v2 signature, package/Quest targets, API 32 minimum and headset-camera permission passed checks. Meta’s unchanged `assets/lib` metrics resources include both ARM64 and armeabi-v7a; the installable app ABI is ARM64 only. The APK uses the Android Debug certificate. Its SHA256 is `f9f7d9479171518602cf69e1c1daf86453b5c4797b1d464aa41b9cac272fd1d6`. Headset installation and physical interaction remain separate, pending checks; GitHub Actions has not run.

Post-test SDK Editor utility changes to orientation and the current desktop quality level were restored to the verified APK baseline; the suite does not establish physical headset orientation. Opening the project interactively may reapply these SDK preferences; saving them requires a fresh APK build and verification. The canonical build preserved those verified source settings; only scene local IDs, empty-icon whitespace and generated SDK helpers were cleaned afterward.

Tests cover concave polygon triangulation, winding, invalid strokes, overlap/bounds rejection, scale limits, controller action priority, camera projection failure, and camera freshness recovery. Tests and compilation cannot verify headset alignment or physical interaction.

Headset acceptance: launch straight into passthrough; grant/deny camera access; draw multiple regions; select Accept using the controller; verify source-only live blur; drag/resize live sharp copies; move the head; suspend/resume and interrupt camera input. Report which checks actually ran. An APK build is separate from installation and this interaction sequence.

The [simulated accessibility scenarios](docs/ACCESSIBILITY_SCENARIOS.md) explain the controller feedback and visual cues. These are hypothetical reviews, not feedback collected from people or measured accessibility results.

The app duplicates camera pixels and covers destination content. Its head-relative masks are assumed regions, not retinal measurements. Camera coverage is smaller than headset passthrough, so effects disappear where source pixels cannot be sampled. The fixed plane is not scene reconstruction or validated binocular registration. No diagnosis, treatment, vision benefit, clinical result, measured latency, or frame-rate claim is established.

The retired Swift/ARKit/Metal prototype remains recoverable in Git history at `5a405be6b6fc1b7c6b5e9405bb2d044cb619e470`; the active tree contains the Quest app only. See the [migration design](docs/superpowers/specs/2026-10-17-quest-3s-migration-design.md) and [plan](docs/superpowers/plans/2026-10-17-quest-3s-migration.md).

SDK references: [Meta camera overview](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/), [MRUK camera reference](https://developers.meta.com/vr/reference/mruk/v85/class_meta_x_r_passthrough_camera_access/), [official camera samples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples).
