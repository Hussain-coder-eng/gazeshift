# GazeShift Quest 3S migration

## Intent

Replace the phone prototype with a Unity application for Meta Quest 3S. The headset opens directly into full-color passthrough. A controller draws the assumed blurry areas, the user accepts the regions, then moves and resizes sharp live image copies while the original regions remain blurred. This is a manual image-relocation experiment; there is no front-camera setup, gaze tracking, retinal measurement, or clinical claim.

The user requested the complete migration and GitHub commit/push. The Gang uses an isolated clone, feature branch, independent review, and only then the authorized merge/push. The original iOS checkout remains untouched; Git history preserves the retired implementation.

## Interaction

- Passthrough starts automatically. Keep only short instructions and the floating **Accept regions** button as on-screen text; route diagnostics to logs. Permission/source errors are actionable instructions in that same instruction surface.
- Draw on a head-relative plane one meter ahead. Hold the right trigger and move the controller ray to accumulate a visible polygon outline; release closes a valid stroke. Trigger hit-testing checks the Accept button before starting a stroke. The right A button also accepts; B undoes the newest unaccepted region, or the newest accepted region when none are pending.
- Support multiple independent regions. Acceptance freezes their source polygons in head-relative coordinates, preserving the old screen-fixed intent. Source polygons do not follow the eyes or world objects.
- Grip and point at an accepted source region or copy to select and drag its sharp copy. Right-thumbstick vertical motion resizes the selected copy about its center. The source mask stays in place. Keep the last valid destination when a new drag/resize would leave the canvas or overlap a source region.
- The sharp copy uses the original live camera texture, never the blurred overlay or another copy. Apply a live blur only inside each accepted source polygon. Preserve the drawn polygon instead of replacing it with the old bounding ellipse. Destination copies cover real destination content.
- Camera loss, denied permission, or app suspension removes blur/copy effects; passthrough and actionable instructions remain. A fresh camera frame is required before effects resume. If any source vertex cannot project into the sampled camera image, hide both source blur and its copy. Never display a frozen camera patch as live.

The [simulated accessibility updates](2026-10-17-accessibility-updates.md) extend this baseline with controller feedback, HUD movement, original-view peek, and undo after acceptance.

## Engineering decisions

These are implementation choices made by the Gang, rather than additional user requirements.

- Unity **6000.4.1f1**, Meta XR Core and MR Utility Kit **85.0.0**, OpenXR **1.16.1**, XR Management **4.5.4**. Use the official Meta registry `https://npm.developer.oculus.com` with scope `com.meta`. MRUK's `Meta.XR.PassthroughCameraAccess` supplies an actual live RGB camera texture and calibrated `WorldToViewportPoint` projection. Passthrough rendering alone does not expose image pixels.
- Use an `OVRCameraRig`, controller input, and a background `OVRPassthroughLayer`; no backend, account flow, models, or custom camera capture plugin. Camera access requires the supported Quest OS/API and user permission. Project configuration and docs state the actual requirement.
- The runtime lives in namespace `GazeShift`. `ICameraFrameSource` exposes `bool IsReady { get; }`, `Texture FrameTexture { get; }`, and `bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv)`. `QuestCameraSource` implements it with MRUK. `RegionInteraction.Initialize(ICameraFrameSource source, Transform centerEye)` consumes it and creates all drawing/region/controller UI state.
- Recalculate source-camera UVs for the source polygon's world positions each frame. A patch's display transform changes its destination vertices, never its source sampling coordinates. This is a camera-calibrated image overlay at a fixed plane, not reconstructed scene geometry or medically validated binocular registration.
- Carry forward the existing minimum 20 distinct stroke samples and closure distance of 0.03 canvas diagonals as provisional drawing thresholds. Reject nonfinite, self-intersecting, open, zero-area, and untriangulatable polygons. Triangulation must handle concave polygons in either winding. Limit thumbstick copy scale to a named practical range and test the bounds.
- Build Android ARM64 using IL2CPP with Quest/OpenXR configuration and camera permission metadata. Create the scene and settings deterministically through an Editor menu/build entry point, and commit the generated scene/project configuration. Missing Unity license or Android tooling must be reported as a build gate, not disguised as an APK success.

## Scope map

Retire the active Apple runtime and packages: `GazeShift/`, `GazeShift.xcodeproj/`, `GazeShiftTests/`, `GazeShiftCore/`, and `GazeShiftFlow/`. Their callers include SwiftUI screens, ARKit capture and Vision tracing, Swift flow adapters, Metal compositors, package fixtures and tests, and Xcode build configuration; remove them together. Retire obsolete phone/gaze instructions in `BUILD_PROMPTS.md`, `SIX_HOUR_PLAN.md`, `PROJECT_CONTEXT.md`, and the historical diagnostic/flow documents under `docs/`. Replace the root README and current context with the Quest path and accurate evidence boundaries. There was no existing CI workflow.

Create `Assets/GazeShift/Platform/`, `Assets/GazeShift/Interaction/`, `Assets/GazeShift/Shaders/`, `Assets/GazeShift/Tests/`, `Assets/GazeShift/Editor/`, `Packages/`, and `ProjectSettings/`. Platform owns camera/rig/bootstrap/settings/build tooling; interaction owns polygon validation, triangulation, input, rendering, and tests. Use assembly definitions to make EditMode tests independent of headset availability. Keep Unity caches, SDK downloads, generated binaries, and the plan ledger ignored.

## Acceptance and evidence

Automated checks cover polygon validation, concave triangulation and area conservation, point-in-polygon boundaries, source/destination separation, scale limits, and camera readiness failure. Unity must import and compile the project without script errors; run its EditMode suite and the Android build when the installed license/modules permit it. CI may repeat these using repository Unity license secrets, without committing credentials.

A headset test is distinct: start without setup; grant/deny permission; draw multiple shapes; select Accept with the controller; verify source-only live blur; drag and resize a live sharp copy; move the head; interrupt/resume the source. If no Quest is connected, report those interactions and visual alignment as unverified even if tests and APK build pass. Do not claim a measured frame rate, latency, vision benefit, or end-to-end headset success without evidence.
