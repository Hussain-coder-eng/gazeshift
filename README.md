# GazeShift for Meta Quest 3S

A manual mixed-reality image-relocation experiment. Draw polygons with the right controller over full-color passthrough, accept the regions, and move/rescale sharp live camera-image copies while the source polygons remain blurred. The app opens directly into passthrough; there is no front-camera setup or eye tracking.

## Controls

1. Hold the right trigger and trace a loop, moving outside the adaptive start ring and returning inside it before release. A valid loop needs at least three distinct vertices and sufficient area. A failed release keeps the trace visible: hold trigger to resume, or press B to discard it. With no trace, B undoes the newest pending region, then the newest accepted region.
2. Point at **Accept regions** and press the trigger, or press A.
3. Point at an accepted region or copy and hold grip to move its sharp copy. Move the right thumbstick up/down to resize the selected **copied image**; source polygons stay fixed. Accepting selects the newest copy. Initial placement prefers room for 1.25× enlargement when valid, with a scale-1 fallback. A blocked resize keeps the last valid placement and asks you to move the copy into open space.
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

The interaction fixes are reviewed and backed up on `fix-quest-headset-interactions` at source commit `4ebe87a0404332b131a8744b0f583fb5c2fe808f`. Main remains at the previously delivered baseline `c41b5928cb48cd2a275dc9b606b2a1d065f0d3d5`; these fixes are not merged and physical acceptance remains open.

On October 18, 2026, the final source passed **96/96 native Unity 6000.4.1f1 Metal EditMode tests**, zero failures/skips and process exit 0, retaining all 77 preceding tests and adding 19 regressions. Whole-branch Superpowers review and the consolidated scoped re-review passed; the actual CodeRabbit scoped review completed with zero findings after one fix wave. Tests cover adaptive excursion-and-return closure, retained-trace recovery, copy placement/resize safety, measured HUD bounds, camera freshness and configuration.

The exact source then completed the canonical `GazeShift.Editor.QuestProjectSetup.BuildAndroid` entrypoint with **process exit 0 / Build Report Success** in **1,743.5 seconds**, using the normal Android SDK/Gradle pipeline. The final ARM64 IL2CPP graph evaluated 1,505 native nodes successfully, with 1 updated; target Vulkan compilation also passed. Minimum observed free space was 11,227,750,400 bytes. All 92 pre-build source/configuration/package hashes match after narrowly proven SDK cleanup.

The verified test APK is **46,254,214 bytes**, SHA256 `6ccd2308d1eb1a0bb64b4fa014a4dd54f4ead80c22e24aaaf1b9f331bf92b71f`. ZIP integrity, APK v2 signature, all 12 installable ARM64 libraries, package/Quest targets, API 32 minimum and headset-camera permission passed checks. Meta’s 28 unchanged metrics resources under `assets/lib` match the pinned SDK, including its ARM64 and armeabi-v7a resources; the installable app ABI is ARM64 only. The APK uses the Android Debug certificate. GitHub Actions has not run.

Explicit `LandscapeLeft` configuration (serialized orientation 3) is packaged as Android landscape (0). This supersedes only the earlier orientation restoration; the independent desktop quality/vSync restoration remains. The HUD is smaller and lower: native rendered bounds are at most **0.594 × 0.0792 m** for instructions and **0.2376 × 0.039879 m** for the Accept label. These measurements and the bounded one-shot startup XR diagnostics do not establish a physical zoom fix; projection/FOV settings are unchanged.

**This exact final APK installed on Quest 3S with `adb install -r` returning Success / exit 0 at 19:08:12 UTC on October 18.** Existing headset-camera grants were preserved. At 19:08:26 UTC, the freshly resolved launcher component was intercepted by Quest’s **Switch to Controllers** OS dialog (`LaunchCheckControllerRequiredDialogActivity`). The app had no PID, no app foreground, no active camera client and no startup numeric XR snapshot. A launch-command “Status: ok” therefore does not establish app startup or rendering. The earlier `f9f7d947…` APK had a live app process and 2,827 camera-service frames, followed by wearer-reported whole-view zoom before drawing, oversized/clipped text, a disappearing near-closed loop and an unresizable yellow copied image. Those observations are historical. The intermediate `af209161…` APK installed but launch was intercepted by Quest’s **Switch to Controllers** OS gate. Neither result establishes physical acceptance of the final APK.

Headset acceptance remains required: clear the OS controller gate; compare the pre-drawing view and lower HUD; draw normal and near-closed loops, resume/discard an invalid trace, accept regions, and move/resize the yellow copied image. Then check camera permission grant/denial, visible fresh blur/sharp copies, haptics, head movement, suspend/resume and camera interruption recovery. Record only checks actually completed. Startup/service logs and desktop tests cannot verify optical alignment or physical interaction.

The [simulated accessibility scenarios](docs/ACCESSIBILITY_SCENARIOS.md) explain the controller feedback and visual cues. These are hypothetical reviews, not feedback collected from people or measured accessibility results.

The app duplicates camera pixels and covers destination content. Its head-relative masks are assumed regions, not retinal measurements. Camera coverage is smaller than headset passthrough, so effects disappear where source pixels cannot be sampled. The fixed plane is not scene reconstruction or validated binocular registration. No diagnosis, treatment, vision benefit, clinical result, measured latency, or frame-rate claim is established.

The retired Swift/ARKit/Metal prototype remains recoverable in Git history at `5a405be6b6fc1b7c6b5e9405bb2d044cb619e470`; the active tree contains the Quest app only. See the [migration design](docs/superpowers/specs/2026-10-17-quest-3s-migration-design.md) and [plan](docs/superpowers/plans/2026-10-17-quest-3s-migration.md).

SDK references: [Meta camera overview](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/), [MRUK camera reference](https://developers.meta.com/vr/reference/mruk/v85/class_meta_x_r_passthrough_camera_access/), [official camera samples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples).
