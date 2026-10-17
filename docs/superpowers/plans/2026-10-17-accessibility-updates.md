# Accessibility Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking.

**Goal:** Apply the smallest coherent changes from three simulated accessibility reviews.

**Architecture:** Extend the existing RegionSession model for undo, closure, overflow and bounded scale-1 placement. RegionInteraction retains all controller/UI orchestration and owns paired outline resources; LiveRegionView clears hidden bindings and restores only through coverage checks.

**Tech Stack:** Unity 6.4, Meta XR85, C#, NUnit. No new dependencies.

**Spec:** docs/superpowers/specs/2026-10-17-accessibility-updates.md

## Global Constraints

- Automatic AR startup, no front setup; only short instructions and Accept regions in the headset.
- Keep 20 samples and 0.03 canvas-diagonal closure, bounded collision work, app-owned font queue3020.
- Preserve scale1 at acceptance and all source geometry; controller right stick explicitly resizes.
- Single integration worker owns the Unity editor. No real user or device validation claims.

## Review Focus

- Pending-first undo across multiple accepted regions disposes removed paired resources.
- Crowded canvas fails atomically rather than silently shrinking; dense valid shapes still fit.
- Moving HUD before camera permission keeps Accept hit priority and visible bounds.
- Peek/tracking loss cannot revive stale camera bindings.
- Repeated blocked movement cannot continuously vibrate; interruption stops an active pulse.

### Task 1: State and bounded placement

**Files:** Interaction/RegionSession.cs; Tests/EditMode/RegionSessionTests.cs.
**Interfaces:** UndoLast returns removed RegionData; IsClosureReady and StrokeOverflowed expose stroke state; initial placement ranks a bounded candidate set by distance to center.

- [ ] Add failing tests for undo order, crowded-scale1 atomic failure, center-first placement, exact closure/overflow, HUD clamping and rejection interval.
- [ ] Run existing run-pure-tests.sh and record expected RED.
- [ ] Implement minimum state changes and retain dense-placement/work-budget regression tests; run GREEN.

### Task 2: Controller feedback and UI lifecycle

**Files:** Interaction/RegionInteraction.cs, Interaction/LiveRegionView.cs; Tests/EditMode/RegionInteractionUiTests.cs, LiveRegionViewTests.cs.
**Interfaces:** Initialize remains unchanged; internal MoveHud, SetOriginalView, UndoLastRegion and AcceptRegions are production controller actions exercised by native tests.

- [ ] Author native tests for moved Accept hit priority, selected copy and cleanup, fresh coverage after peek and paired outlines.
- [ ] Reuse owned materials/lines; add source/copy grip offsets, backed larger text, start/endpoint cues, haptic pulse timeout and persistent actionable errors.
- [ ] Hand native suite to the sole integration editor worker; run pure suite and diff checks locally.

### Task 3: Evidence and handoff

**Files:** docs/ACCESSIBILITY_SCENARIOS.md; ignored accessibility-report.md.

- [ ] Record each simulated profile, actual advice implemented/skipped and test evidence, with primary source links and hardware limits.
- [ ] Self-review scoped diff and notify root ready for staging/commit turn; root dispatches review before merge.
