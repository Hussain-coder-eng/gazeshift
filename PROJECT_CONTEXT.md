# GazeShift project context

The active platform is **Meta Quest 3S / Quest 3**, implemented in Unity 6000.4.1f1 C# with Meta XR Core/MR Utility Kit 85 and OpenXR 1.16.1. The [migration design](docs/superpowers/specs/2026-10-17-quest-3s-migration-design.md) and [accessibility updates](docs/superpowers/specs/2026-10-17-accessibility-updates.md) are the maintained requirements; the [README](README.md) contains setup and controls.

The app starts in full-color passthrough, accepts multiple controller-drawn polygons, blurs their original live camera image, and displays separate sharp live copies dragged with grip and resized with the right thumbstick. Regions are head-relative at a one-meter plane. Only instructions and **Accept regions** appear in the headset. No front setup, gaze tracking, diagnostics panel, cloud backend, or model inference belongs in this flow.

`QuestCameraSource` adapts the live MRUK texture and calibrated UV projection. `RegionInteraction` owns geometry, input and rendering. Original pixels feed blur and copies independently; copies never sample a previous composite. Stale input or invalid source-camera projection removes effects. This manual approximation is not retinal measurement, scene reconstruction, or validated vision assistance.

Use feature branches and the Gang's write-path agents. Commit and run meaningful checks, then obtain independent Superpowers review of final BASE/HEAD before merging/pushing. Report test, compile, APK, install and physical-headset evidence separately. Keep credentials and large media outside Git. The retired phone implementation and diagnostics remain in Git history at final iOS baseline `5a405be6b6fc1b7c6b5e9405bb2d044cb619e470`.
