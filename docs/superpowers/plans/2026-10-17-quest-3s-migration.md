# GazeShift Quest 3S Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the iOS prototype with a controller-driven Quest 3S passthrough application that blurs drawn polygons and relocates sharp live copies.

**Architecture:** A Meta SDK camera adapter feeds a narrow live-frame/projection interface. The interaction layer owns head-relative polygons, controller hit-testing, GPU blur and sharp copies; a deterministic Editor setup creates the rig/scene and build configuration.

**Tech Stack:** Unity 6000.4.1f1, C#, Meta XR Core/MRUK 85.0.0, OpenXR 1.16.1, XR Management 4.5.4, Android ARM64 IL2CPP.

**Spec:** `docs/superpowers/specs/2026-10-17-quest-3s-migration-design.md`

## Global Constraints

- Auto-start full-color Quest 3S passthrough; no front-camera setup or gaze tracking.
- Only short instructions and **Accept regions** may appear as text in the headset.
- Right trigger draws/hits UI; A accepts; B undoes an unaccepted region; grip drags; right thumbstick resizes.
- Multiple head-relative polygon regions; original live pixels feed both blur and sharp copy.
- Unity 6000.4.1f1; Meta Core/MRUK 85.0.0; OpenXR 1.16.1; XR Management 4.5.4.
- All writes on `feat-quest-3s-migration`; independent Superpowers review before authorized merge/push.
- No headset evidence, clinical benefit, latency, or APK success may be inferred from a compile/test result.

## Review Focus

- Camera permission denial, frame loss, and pause/resume remove effect textures until a fresh source is available; Task 1 readiness tests and Task 3 checklist.
- Concave/reversed/self-crossing drawings preserve their polygon or reject cleanly; Task 2 geometry tests.
- A trigger hit on Accept never adds an accidental polygon point; Task 2 hit-priority check.
- Controller tracking loss during drawing never bridges disconnected strokes; Task 2 stroke invalidation test.
- Drag/resize beyond bounds or over any source mask keeps the previous valid placement; Task 2 placement and scale tests.

---

### Task 1: Unity platform, live camera, and build setup

**Files:**
- Create: `Assets/GazeShift/Platform/ICameraFrameSource.cs`, `QuestCameraSource.cs`, `GazeShiftBootstrap.cs`, runtime assembly definition.
- Create: `Assets/GazeShift/Editor/QuestProjectSetup.cs`, Editor assembly definition, generated `Assets/GazeShift/Scenes/GazeShift.unity`.
- Create: `Packages/manifest.json`, package lock, required `ProjectSettings/`, platform readiness tests, `.github/workflows/unity.yml`.
- Modify: `.gitignore`, `README.md`, `PROJECT_CONTEXT.md`.
- Remove together: Apple directories and obsolete phone/gaze docs listed in the spec scope map.

**Interfaces:**
- Produces: `public interface ICameraFrameSource { bool IsReady { get; } Texture FrameTexture { get; } bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv); }` in namespace `GazeShift`.
- Produces: `QuestCameraSource : MonoBehaviour, ICameraFrameSource`; the live MRUK texture and calibrated UVs. Reject missing texture, stopped/disabled source, nonfinite projection and UV outside `[0,1]`.
- Consumes in final integration: `RegionInteraction.Initialize(ICameraFrameSource source, Transform centerEye)` from Task 2; bootstrap can defer that binding until Task 3 while Task 2 is in flight.
- Produces: deterministic `GazeShift.Editor.QuestProjectSetup` menu/setup entry point and batch Android build entry point; exact invocation documented in README.

- [ ] **Step 1:** Verify Unity license/import and Android modules before a long implementation. Resolve packages at their pinned versions; confirm MRUK API signatures from installed package source.
- [ ] **Step 2:** Write readiness/UV validation tests covering null texture, nonfinite/out-of-range coordinates, suspension, and recovery. Run EditMode tests to expose missing adapter/validation.
- [ ] **Step 3:** Implement camera adapter, passthrough rig/bootstrap, explicit Android permission/platform setup, deterministic scene creation, and build method. Keep diagnostics in logs and actionable instructions in the single instruction surface.
- [ ] **Step 4:** Retire Apple runtime/config/tests/obsolete docs together; replace README/context with Quest setup, exact checks, evidence limits, and source references. Update Unity ignore rules and license-secret CI.
- [ ] **Step 5:** Run Unity import/compile and platform tests. Run Android build if supported; record exact license/module blockers otherwise. Confirm source checkout unchanged; commit only scoped platform/retirement files.

### Task 2: Polygon regions, controller interaction, and live rendering

**Files:**
- Create: `Assets/GazeShift/Interaction/RegionGeometry.cs`, `RegionInteraction.cs`, and focused helpers only when needed.
- Create: `Assets/GazeShift/Shaders/LiveRegion.shader` (live blur and sharp sample paths).
- Create: `Assets/GazeShift/Tests/EditMode/RegionGeometryTests.cs`, interaction/state tests, test assembly definition.

**Interfaces:**
- Consumes: `ICameraFrameSource` exactly as Task 1 defines; `FrameTexture` is the current original RGB image, not a snapshot.
- Produces: `public void RegionInteraction.Initialize(ICameraFrameSource source, Transform centerEye)` in namespace `GazeShift`; use `OVRCameraRig.rightControllerAnchor` and `OVRInput` for the controller.
- Produces: polygon validation/triangulation, hit-test, and placement/scale helpers with pure testable inputs; tests pin chosen signatures inside this task.

- [ ] **Step 1:** Write failing geometry tests for a convex shape, concave L polygon, reversed winding, duplicate closing point, self-intersection, nonfinite point, degenerate area, point on edge, and sum-of-triangle area equal to polygon area.
- [ ] **Step 2:** Implement simple-polygon validation and ear-clipping triangulation; retain the original boundary. Run geometry tests; preserve named existing thresholds of 20 distinct samples and 0.03 canvas-diagonal closure.
- [ ] **Step 3:** Write failing state/placement tests for trigger UI priority, interrupted stroke invalidation, multiple source regions, destination rejection, and resize limits; implement the minimal state model.
- [ ] **Step 4:** Implement head-relative one-meter canvas, right-controller drawing, floating Accept/A acceptance, B undo, grip selection/drag, and thumbstick resize. Use a single instruction surface and **Accept regions** button text.
- [ ] **Step 5:** Render live polygon-only source blur and distinct sharp copy meshes. Update original-source UVs each frame with `TryWorldToTextureUv`; hide the paired blur/copy if any source vertex fails projection, clear all effects when `IsReady` is false, and never sample a prior composite.
- [ ] **Step 6:** Run EditMode tests and Unity compile with Task 1 interface available; commit only Interaction/Shaders/Tests files. Report exact verification limits.

### Task 3: Integration, build, and review

**Files:** modify the Task 1 bootstrap/scene only as needed to bind Task 2; update README evidence and this plan's ignored ledger.

**Interfaces:** consumes `QuestCameraSource` and `RegionInteraction.Initialize` above; no additional subsystem is introduced.

- [ ] **Step 1:** Bind interaction to the real camera source and center eye. Verify rig, passthrough layer order, camera permission, and scene inclusion in builds.
- [ ] **Step 2:** Run the full Unity EditMode suite, project compile, and Android build/dry-run. Record actual outputs and any environment gate without overstating success.
- [ ] **Step 3:** Verify no active Apple runtime/build references or obsolete on-screen diagnostics remain. Inspect current scene/config and package lock for complete reproducibility.
- [ ] **Step 4:** If a Quest is connected, execute the spec's headset acceptance sequence and record observations; otherwise explicitly mark it unverified.
- [ ] **Step 5:** Commit integration, generate BASE/HEAD review package, dispatch mandatory independent Superpowers reviewer, and fix/re-review every Critical/Important finding.
- [ ] **Step 6:** After clean review, perform the authorized merge/push, preserve/enable GitHub main protections where available, and report commit/build/test/headset evidence separately.
