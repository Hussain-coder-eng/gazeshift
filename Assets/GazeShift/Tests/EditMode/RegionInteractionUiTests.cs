using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GazeShift.Tests
{
    public sealed class RegionInteractionUiTests
    {
        [Test] public void InstructionAndAcceptTextRenderAfterButtonWithoutChangingSharedFont()
        {
            var owner = new GameObject("Interaction UI test");
            var eye = new GameObject("Test eye");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Material sharedFont = font.material;
            int originalQueue = sharedFont.renderQueue;
            Material ownedFont = null;
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>();
                interaction.Initialize(new UnavailableSource(), eye.transform);
                TextMesh[] labels = eye.GetComponentsInChildren<TextMesh>();
                Assert.AreEqual(2, labels.Length);
                MeshRenderer button = null;
                foreach (MeshRenderer renderer in eye.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.gameObject.name == "Accept regions") button = renderer;
                Assert.IsNotNull(button);
                foreach (TextMesh label in labels)
                {
                    Material textMaterial = label.GetComponent<MeshRenderer>().sharedMaterial;
                    Assert.AreNotSame(sharedFont, textMaterial);
                    Assert.Greater(textMaterial.renderQueue, button.sharedMaterial.renderQueue);
                    Assert.AreSame(sharedFont.mainTexture, textMaterial.mainTexture);
                    if (ownedFont == null) ownedFont = textMaterial;
                    else Assert.AreSame(ownedFont, textMaterial);
                }
                Assert.AreEqual(originalQueue, sharedFont.renderQueue);
                Assert.AreSame(Read<Material>(interaction, "textMaterial"), ownedFont);
                DestroyInteraction(owner);
                Assert.IsTrue(ownedFont == null);
                Assert.IsNotNull(sharedFont);
            }
            finally
            {
                DestroyInteraction(owner);
                Object.DestroyImmediate(eye);
            }
        }
        [Test] public void MovedHudStaysInBoundsAndAcceptHitWinsOverDrawingWithoutCamera()
        {
            var owner = new GameObject("Moved UI test"); var eye = new GameObject("Test eye");
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>();
                interaction.Initialize(new UnavailableSource(), eye.transform);
                Call(interaction, "MoveHud", new Vector2(9, -9));
                Transform hud = Read<Transform>(interaction, "hud");
                Assert.AreEqual(0.3f, hud.localPosition.x, 0.00001f);
                Assert.AreEqual(-0.3f, hud.localPosition.y, 0.00001f);
                Call(interaction, "MoveHud", new Vector2(-9, 9));
                Assert.AreEqual(-0.3f, hud.localPosition.x, 0.00001f);
                Assert.AreEqual(0.3f, hud.localPosition.y, 0.00001f);
                Call(interaction, "MoveHud", new Vector2(0.3f, -0.3f));
                BoxCollider button = Read<BoxCollider>(interaction, "acceptCollider");
                Physics.SyncTransforms();
                var ray = new Ray(Vector3.zero, button.transform.position.normalized);
                Assert.IsTrue(button.Raycast(ray, out _, 4));
                Call(interaction, "BeginTrigger", ray);
                Assert.IsFalse(Read<RegionSession>(interaction, "session").IsDrawing);
                Assert.IsNotNull(hud.Find("Instruction backplate"));
                Assert.Greater(Read<TextMesh>(interaction, "instruction").GetComponent<Renderer>().bounds.size.y, 0f);
            }
            finally { DestroyInteraction(owner); UnityEngine.Object.DestroyImmediate(eye); }
        }
        [Test] public void AcceptanceSelectsNewestCopyAndUndoDisposesItsPairedResources()
        {
            var owner = new GameObject("Undo UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new LiveSource(texture), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session");
                DrawCircle(session, new Vector2(-0.25f, 0)); DrawCircle(session, new Vector2(0.25f, 0));
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions");
                RegionData newest = session.Accepted[1]; Assert.AreSame(newest, Read<RegionData>(interaction, "selected"));
                float smallestCopyWidth = float.PositiveInfinity, largestCopyWidth = 0;
                foreach (LineRenderer line in eye.GetComponentsInChildren<LineRenderer>())
                    if (line.gameObject.name == "Copy boundary inner")
                    {
                        smallestCopyWidth = Mathf.Min(smallestCopyWidth, line.widthMultiplier);
                        largestCopyWidth = Mathf.Max(largestCopyWidth, line.widthMultiplier);
                    }
                Assert.Greater(largestCopyWidth, smallestCopyWidth);
                foreach (LineRenderer line in eye.GetComponentsInChildren<LineRenderer>())
                    if (line.gameObject.name == "Copy boundary outer")
                        Assert.AreEqual(Color.black, line.sharedMaterial.GetColor("_Color"));
                var views = Read<Dictionary<RegionData, LiveRegionView>>(interaction, "views");
                LiveRegionView removed = views[newest];
                Mesh sourceMesh = Read<Mesh>(removed, "sourceMesh"), copyMesh = Read<Mesh>(removed, "copyMesh");
                GameObject blur = Read<GameObject>(removed, "sourceObject"), copy = Read<GameObject>(removed, "copyObject");
                int lineCount = eye.GetComponentsInChildren<LineRenderer>().Length;
                Call(interaction, "UndoLastRegion");
                Assert.IsNull(Read<RegionData>(interaction, "selected")); Assert.AreEqual(1, views.Count);
                Assert.IsTrue(sourceMesh == null); Assert.IsTrue(copyMesh == null); Assert.IsTrue(blur == null); Assert.IsTrue(copy == null);
                Assert.AreEqual(lineCount - 4, eye.GetComponentsInChildren<LineRenderer>().Length);
                Assert.AreEqual(1, session.Accepted.Count);
            }
            finally { DestroyInteraction(owner); UnityEngine.Object.DestroyImmediate(eye); UnityEngine.Object.DestroyImmediate(texture); }
        }
        [Test] public void SourceGripBringsCopyToPointerWhileCopyGripPreservesOffsetAndInvalidMoveRollsBack()
        {
            var owner = new GameObject("Grip UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new LiveSource(texture), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, new Vector2(-0.25f, 0));
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions"); RegionData region = session.Accepted[0];
                Call(interaction, "BeginDrag", region.SourceCenter); Call(interaction, "DragTo", new Vector2(0.35f, 0));
                Assert.AreEqual(0.35f, region.Destination.x, 0.00001f);
                Call(interaction, "BeginDrag", new Vector2(0.37f, 0)); Call(interaction, "DragTo", new Vector2(0.42f, 0));
                Assert.AreEqual(0.4f, region.Destination.x, 0.00001f);
                Call(interaction, "DragTo", region.SourceCenter);
                Assert.AreEqual(0.4f, region.Destination.x, 0.00001f);
                Assert.IsNotEmpty(Read<string>(interaction, "actionInstruction"));
                Call(interaction, "DragTo", new Vector2(0.42f, 0));
                Assert.IsNull(Read<string>(interaction, "actionInstruction"));
            }
            finally { DestroyInteraction(owner); UnityEngine.Object.DestroyImmediate(eye); UnityEngine.Object.DestroyImmediate(texture); }
        }
        [Test] public void OriginalViewCancelsGesturesAndRestoresOnlyFreshCoveredFrames()
        {
            var owner = new GameObject("Peek UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                var source = new LiveSource(texture);
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(source, eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, new Vector2(-0.25f, 0));
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions");
                LiveRegionView view = Read<Dictionary<RegionData, LiveRegionView>>(interaction, "views")[session.Accepted[0]];
                Vector2 destination = session.Accepted[0].Destination;
                Call(interaction, "BeginDrag", destination); session.BeginTrigger(false); session.AddPoint(Vector2.zero);
                Call(interaction, "SetOriginalView", true);
                Assert.IsFalse(view.IsVisible); Assert.IsFalse(session.IsDrawing); Assert.IsFalse(Read<bool>(interaction, "dragging"));
                foreach (MeshRenderer renderer in eye.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.gameObject.name == "Source blur" || renderer.gameObject.name == "Live sharp copy")
                    {
                        var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                        Assert.IsFalse(renderer.enabled); Assert.IsNull(properties.GetTexture("_MainTex"));
                    }
                source.Ready = false; Call(interaction, "SetOriginalView", false); Call(interaction, "UpdateLiveRegions"); Assert.IsFalse(view.IsVisible);
                source.Ready = true; source.Covered = false; Call(interaction, "UpdateLiveRegions"); Assert.IsFalse(view.IsVisible);
                source.Covered = true; Call(interaction, "UpdateLiveRegions"); Assert.IsTrue(view.IsVisible);
                Assert.AreEqual(destination, session.Accepted[0].Destination); Assert.AreEqual(1, session.Accepted.Count);
                Call(interaction, "OnApplicationFocus", false); Assert.IsFalse(view.IsVisible);
                Assert.IsFalse(Read<InteractionFeedback>(interaction, "feedback").IsPulsing(Time.unscaledTime));
            }
            finally { DestroyInteraction(owner); UnityEngine.Object.DestroyImmediate(eye); UnityEngine.Object.DestroyImmediate(texture); }
        }
        [Test] public void RightControllerLossKeepsAcceptedPairLiveAndCancelsGestures()
        {
            var owner = new GameObject("Controller loss UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                var source = new LiveSource(texture);
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(source, eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, new Vector2(-0.25f, 0));
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions");
                RegionData region = session.Accepted[0];
                LiveRegionView view = Read<Dictionary<RegionData, LiveRegionView>>(interaction, "views")[region];
                float originalUv = Read<Mesh>(view, "sourceMesh").uv[0].x;
                Call(interaction, "BeginDrag", region.Destination); session.BeginTrigger(false); session.AddPoint(Vector2.zero);
                Call(interaction, "UpdateStrokeOutline"); Write(interaction, "closureSignaled", true);
                Read<LineRenderer>(interaction, "rayLine").enabled = true;
                Call(interaction, "Pulse", 0.3f, 1f);
                eye.transform.position = Vector3.right * 0.5f;

                Assert.IsFalse((bool)Call(interaction, "UpdateAvailability", true, false, false));
                Assert.IsTrue(view.IsVisible, "Resting the right controller must keep accepted imagery visible.");
                Assert.AreEqual(originalUv + 0.05f, Read<Mesh>(view, "sourceMesh").uv[0].x, 0.00001f);
                Assert.AreEqual(originalUv + 0.05f, Read<Mesh>(view, "copyMesh").uv[0].x, 0.00001f);
                Assert.AreSame(region, Read<RegionData>(interaction, "selected")); Assert.AreEqual(1, session.Accepted.Count);
                Assert.IsFalse(session.IsDrawing); Assert.IsFalse(Read<bool>(interaction, "dragging"));
                Assert.IsNull(Read<RegionData>(interaction, "hovered")); Assert.IsFalse(Read<bool>(interaction, "closureSignaled"));
                Assert.IsFalse(Read<bool>(interaction, "hapticsActive"));
                Assert.IsFalse(Read<InteractionFeedback>(interaction, "feedback").IsPulsing(Time.unscaledTime));
                foreach (LineRenderer line in eye.GetComponentsInChildren<LineRenderer>())
                    if (line.gameObject.name.StartsWith("Current stroke") || line.gameObject.name.StartsWith("Stroke start ring") ||
                        line.gameObject.name.StartsWith("Controller endpoint") || line.gameObject.name == "Right controller ray")
                        Assert.IsFalse(line.enabled, line.gameObject.name);
                StringAssert.Contains("Connect right controller", Read<TextMesh>(interaction, "instruction").text);
                Assert.IsTrue((bool)Call(interaction, "UpdateAvailability", true, true, false));
                Assert.IsFalse(session.IsDrawing); Assert.IsFalse(Read<bool>(interaction, "dragging"));
                source.Covered = false;
                Call(interaction, "UpdateAvailability", true, false, false);
                AssertPairHiddenAndUnbound(view);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        [Test] public void LeftOriginalViewAndHudRemainAvailableWithoutRightController()
        {
            var owner = new GameObject("Independent left input UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new LiveSource(texture), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, new Vector2(-0.25f, 0));
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions");
                RegionData region = session.Accepted[0];
                LiveRegionView view = Read<Dictionary<RegionData, LiveRegionView>>(interaction, "views")[region];
                Assert.IsFalse((bool)Call(interaction, "UpdateAvailability", true, false, true));
                Assert.IsTrue(Read<bool>(interaction, "originalView")); AssertPairHiddenAndUnbound(view);
                Call(interaction, "MoveHud", new Vector2(0.1f, -0.1f));
                Assert.AreEqual(0.1f, Read<Transform>(interaction, "hud").localPosition.x, 0.00001f);
                Assert.IsFalse((bool)Call(interaction, "UpdateAvailability", true, false, false));
                Assert.IsFalse(Read<bool>(interaction, "originalView")); Assert.IsTrue(view.IsVisible);
                Assert.AreSame(region, Read<RegionData>(interaction, "selected"));
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        [TestCase("head")] [TestCase("camera")] [TestCase("focus")] [TestCase("pause")]
        public void InvalidLiveViewStillHidesBothMeshesAndClearsBindings(string unavailable)
        {
            var owner = new GameObject("View validity UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                var source = new LiveSource(texture);
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(source, eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, new Vector2(-0.25f, 0));
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions");
                LiveRegionView view = Read<Dictionary<RegionData, LiveRegionView>>(interaction, "views")[session.Accepted[0]];
                Assert.IsTrue(view.IsVisible);
                if (unavailable == "camera") source.Ready = false;
                if (unavailable == "focus") Write(interaction, "focused", false);
                if (unavailable == "pause") Write(interaction, "paused", true);
                Assert.IsFalse((bool)Call(interaction, "UpdateAvailability", unavailable != "head", false, false));
                AssertPairHiddenAndUnbound(view); Assert.IsNull(Read<RegionData>(interaction, "selected"));
                Assert.IsFalse(Read<bool>(interaction, "hapticsActive")); Assert.AreEqual(1, session.Accepted.Count);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        [Test] public void RetainedTraceRemainsVisibleAndBDiscardsItBeforeUndoingSelectedCopy()
        {
            var owner = new GameObject("Recovery UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new LiveSource(texture), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, new Vector2(-0.25f, 0));
                Call(interaction, "AcceptRegions"); RegionData accepted = session.Accepted[0];
                session.BeginTrigger(false); session.AddPoint(Vector2.zero); session.AddPoint(new Vector2(0.1f, 0));
                Assert.IsFalse(session.FinishStroke()); Call(interaction, "UpdateStrokeOutline");
                LineRenderer stroke = FindLineRenderer(eye.transform, "Current stroke inner");
                Assert.IsTrue(stroke.enabled); Assert.AreEqual(2, stroke.positionCount);
                StringAssert.Contains("resume", (string)Call(interaction, "StageInstruction"));
                Call(interaction, "UndoLastRegion"); Assert.AreEqual(1, session.Accepted.Count); Assert.AreSame(accepted, Read<RegionData>(interaction, "selected"));
                Assert.IsEmpty(session.Stroke); Call(interaction, "UndoLastRegion"); Assert.IsEmpty(session.Accepted);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        [Test] public void RejectedTriggerReleaseShowsRecoveryGuidanceInDisplayedFailureInstruction()
        {
            var owner = new GameObject("Release instruction UI test"); var eye = new GameObject("Test eye");
            Type inputType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((inputType = assembly.GetType("OVRInput")) != null) break;
            Assert.IsNotNull(inputType);
            FieldInfo controllersField = inputType.GetField("controllers", BindingFlags.Public | BindingFlags.Static);
            object originalControllers = controllersField.GetValue(null);
            try
            {
                // Use the real SDK maps without adding an SDK reference to the test assembly or polling native input.
                Type rightType = inputType.GetNestedType("OVRControllerRTouch", BindingFlags.NonPublic);
                object right = Activator.CreateInstance(rightType, true);
                FieldInfo previousStateField = rightType.GetField("previousState");
                object previousState = previousStateField.GetValue(right);
                Type rawButtonType = inputType.GetNestedType("RawButton");
                uint triggerBit = Convert.ToUInt32(Enum.Parse(rawButtonType, "RIndexTrigger"));
                previousState.GetType().GetField("Buttons").SetValue(previousState, triggerBit);
                previousStateField.SetValue(right, previousState);
                var controllers = (System.Collections.IList)Activator.CreateInstance(controllersField.FieldType);
                controllers.Add(right); controllersField.SetValue(null, controllers);
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); session.BeginTrigger(false);
                session.AddPoint(Vector2.zero); session.AddPoint(new Vector2(0.1f, 0));
                Call(interaction, "HandleStroke", true, new Vector2(0.1f, 0));
                Assert.IsFalse(session.IsDrawing); Assert.AreEqual(2, session.Stroke.Count);
                string failureInstruction = Read<string>(interaction, "actionInstruction");
                Assert.IsNotNull(failureInstruction);
                Call(interaction, "SetInstruction", failureInstruction);
                StringAssert.Contains("resume", Read<TextMesh>(interaction, "instruction").text);
                StringAssert.Contains("B: discard", Read<TextMesh>(interaction, "instruction").text);
            }
            finally { controllersField.SetValue(null, originalControllers); DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        // Leaving Stroke feedback set on resume masks real HUD readiness despite a valid candidate.
        [Test] public void FailedReleaseThenTriggerResumeDisplaysCurrentDrawingAndReadyGuidance()
        {
            var owner = new GameObject("Resume HUD test"); var eye = new GameObject("Test eye");
            Type inputType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((inputType = assembly.GetType("OVRInput")) != null) break;
            Assert.IsNotNull(inputType);
            FieldInfo controllersField = inputType.GetField("controllers", BindingFlags.Public | BindingFlags.Static);
            object originalControllers = controllersField.GetValue(null);
            try
            {
                Type rightType = inputType.GetNestedType("OVRControllerRTouch", BindingFlags.NonPublic);
                object right = Activator.CreateInstance(rightType, true);
                FieldInfo previousStateField = rightType.GetField("previousState");
                object previousState = previousStateField.GetValue(right);
                uint triggerBit = Convert.ToUInt32(Enum.Parse(inputType.GetNestedType("RawButton"), "RIndexTrigger"));
                previousState.GetType().GetField("Buttons").SetValue(previousState, triggerBit);
                previousStateField.SetValue(right, previousState);
                var controllers = (System.Collections.IList)Activator.CreateInstance(controllersField.FieldType);
                controllers.Add(right); controllersField.SetValue(null, controllers);
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); session.BeginTrigger(false);
                session.AddPoint(Vector2.zero); session.AddPoint(new Vector2(0.1f, 0));
                Call(interaction, "HandleStroke", true, new Vector2(0.1f, 0));
                Assert.IsFalse(session.IsDrawing); Assert.AreEqual(2, session.Stroke.Count);
                DisplayCurrentInstruction(interaction);
                StringAssert.Contains("resume", Read<TextMesh>(interaction, "instruction").text);
                Call(interaction, "BeginTrigger", new Ray(Vector3.zero, Vector3.forward));
                Assert.IsTrue(session.IsDrawing); Assert.AreEqual(2, session.Stroke.Count);
                DisplayCurrentInstruction(interaction);
                string drawingInstruction = Read<TextMesh>(interaction, "instruction").text;
                session.AddPoint(new Vector2(0.1f, 0.1f)); session.AddPoint(new Vector2(0, 0.1f)); session.AddPoint(Vector2.zero);
                Assert.IsTrue(session.IsClosureReady); Call(interaction, "UpdateStrokeOutline");
                DisplayCurrentInstruction(interaction);
                StringAssert.Contains("Loop ready", Read<TextMesh>(interaction, "instruction").text);
                StringAssert.Contains("Return to the start ring", drawingInstruction);
            }
            finally { controllersField.SetValue(null, originalControllers); DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        [Test] public void SmallLoopRingRetainsItsActualRadiusAcrossReleaseAndResume()
        {
            var owner = new GameObject("Small closure ring test"); var eye = new GameObject("Test eye");
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); session.BeginTrigger(false);
                foreach (Vector2 point in new[] { Vector2.zero, new Vector2(0.025f, 0), new Vector2(0.025f, 0.025f) }) session.AddPoint(point);
                Call(interaction, "UpdateStrokeOutline");
                LineRenderer ring = FindLineRenderer(eye.transform, "Stroke start ring inner");
                Assert.AreEqual(0.008838835f, ring.GetPosition(0).x, 0.000001f);
                Assert.AreEqual(session.ClosureRadius, ring.GetPosition(0).x, 0.000001f);
                Assert.IsFalse(session.FinishStroke()); Call(interaction, "UpdateStrokeOutline");
                Assert.IsTrue(ring.enabled); Assert.AreEqual(0.008838835f, ring.GetPosition(0).x, 0.000001f);
                Assert.AreEqual(session.ClosureRadius, ring.GetPosition(0).x, 0.000001f);
                Call(interaction, "BeginTrigger", new Ray(Vector3.zero, Vector3.forward));
                session.AddPoint(new Vector2(0, 0.025f)); session.AddPoint(Vector2.zero); Call(interaction, "UpdateStrokeOutline");
                Assert.IsTrue(session.IsClosureReady); Assert.AreEqual(0.008838835f, ring.GetPosition(0).x, 0.000001f);
                Assert.AreEqual(session.ClosureRadius, ring.GetPosition(0).x, 0.000001f);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        [TestCase("cancel")] [TestCase("discard")] [TestCase("acceptHit")] [TestCase("overflow")]
        [TestCase("peek")] [TestCase("focus")] [TestCase("pause")] [TestCase("head")]
        [TestCase("camera")] [TestCase("controller")] [TestCase("finished")]
        public void FreshStrokeAfterResetHasSmallRingAndNoPreviousCandidate(string reset)
        {
            var owner = new GameObject("Closure reset test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                var source = new LiveSource(texture);
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(source, eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); session.BeginTrigger(false);
                foreach (Vector2 point in new[] { Vector2.zero, new Vector2(0.025f, 0), new Vector2(0.025f, 0.025f), new Vector2(0, 0.025f), Vector2.zero }) session.AddPoint(point);
                Assert.IsTrue(session.IsClosureReady);
                if (reset == "cancel") session.CancelStroke();
                if (reset == "discard") Call(interaction, "UndoLastRegion");
                if (reset == "acceptHit") session.BeginTrigger(true);
                if (reset == "finished") Assert.IsTrue(session.FinishStroke());
                if (reset == "overflow")
                {
                    for (int i = 0; i < RegionSession.MaximumSamples && session.IsDrawing; i++) session.AddPoint(new Vector2((i % 2) * 0.01f, -0.02f));
                    Assert.IsTrue(session.StrokeOverflowed);
                }
                if (reset == "peek") Call(interaction, "SetOriginalView", true);
                if (reset == "focus") Call(interaction, "OnApplicationFocus", false);
                if (reset == "pause") Call(interaction, "OnApplicationPause", true);
                if (reset == "head") Call(interaction, "UpdateAvailability", false, true, false);
                if (reset == "camera") { source.Ready = false; Call(interaction, "UpdateAvailability", true, true, false); }
                if (reset == "controller") Call(interaction, "UpdateAvailability", true, false, false);
                Assert.IsFalse(session.IsDrawing); Assert.IsEmpty(session.Stroke); Assert.IsFalse(session.IsClosureReady);
                session.BeginTrigger(false); session.AddPoint(new Vector2(-0.1f, 0)); Call(interaction, "UpdateStrokeOutline");
                Assert.IsFalse(session.IsClosureReady); Assert.AreEqual(1, session.Stroke.Count);
                Assert.AreEqual(-0.095f, FindLineRenderer(eye.transform, "Stroke start ring inner").GetPosition(0).x, 0.000001f,
                    "Reset must clear the previous excursion as well as its cached polygon.");
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        [TestCase("Movement", false)] [TestCase("Acceptance", false)] [TestCase("Stroke", true)]
        public void TriggerBeginKeepsUnrelatedFeedbackAndAcceptHitFeedback(string action, bool hitAccept)
        {
            var owner = new GameObject("Scoped feedback test"); var eye = new GameObject("Test eye");
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                Type actionType = typeof(RegionInteraction).GetNestedType("BlockedAction", BindingFlags.NonPublic);
                Call(interaction, "ShowActionInstruction", Enum.Parse(actionType, action), "Existing action feedback");
                Ray ray = hitAccept ? new Ray(Vector3.zero, Read<BoxCollider>(interaction, "acceptCollider").transform.position.normalized) : new Ray(Vector3.zero, Vector3.forward);
                Call(interaction, "BeginTrigger", ray);
                Assert.AreEqual("Existing action feedback", Read<string>(interaction, "actionInstruction"));
                Assert.AreEqual(!hitAccept, Read<RegionSession>(interaction, "session").IsDrawing);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        // Exercise the real HUD setter with Update's action-before-stage display priority.
        private static void DisplayCurrentInstruction(RegionInteraction interaction)
        {
            Call(interaction, "SetInstruction", Read<string>(interaction, "actionInstruction") ?? (string)Call(interaction, "StageInstruction"));
        }
        [Test] public void ClosureRingMatchesThresholdAndPulseCanReenterAfterLeavingZone()
        {
            var owner = new GameObject("Closure UI test"); var eye = new GameObject("Test eye");
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); session.BeginTrigger(false);
                foreach (Vector2 point in new[] { Vector2.zero, new Vector2(0.1f, 0), new Vector2(0.1f, 0.1f), new Vector2(0, 0.1f), new Vector2(0, 0.03f) }) session.AddPoint(point);
                Call(interaction, "UpdateStrokeOutline"); Assert.IsTrue(Read<bool>(interaction, "closureSignaled"));
                LineRenderer ring = FindLineRenderer(eye.transform, "Stroke start ring inner");
                Assert.AreEqual(0.03535534f, ring.GetPosition(0).x, 0.000001f);
                Assert.AreEqual(session.ClosureRadius, ring.GetPosition(0).x, 0.000001f);
                session.AddPoint(new Vector2(-0.05f, 0.05f)); Call(interaction, "UpdateStrokeOutline"); Assert.IsFalse(Read<bool>(interaction, "closureSignaled"));
                session.AddPoint(new Vector2(-0.02f, 0.02f)); Call(interaction, "UpdateStrokeOutline"); Assert.IsTrue(Read<bool>(interaction, "closureSignaled"));
                Assert.IsTrue(Read<InteractionFeedback>(interaction, "feedback").IsPulsing(Time.unscaledTime));
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        [Test] public void BlockedEnlargementExplainsOpenSpaceWhileDragKeepsMovementInstructionAndUvs()
        {
            var owner = new GameObject("Resize UI test"); var eye = new GameObject("Test eye"); var texture = new Texture2D(2, 2);
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new LiveSource(texture), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); DrawCircle(session, Vector2.zero);
                Call(interaction, "AcceptRegions"); Call(interaction, "UpdateLiveRegions"); RegionData region = session.Accepted[0];
                LiveRegionView view = Read<Dictionary<RegionData, LiveRegionView>>(interaction, "views")[region];
                Vector2[] sourceUvs = Read<Mesh>(view, "sourceMesh").uv;
                Vector2 center = region.Destination; Call(interaction, "MoveSelected", center, 1.25f); Assert.AreEqual(1.25f, region.Scale);
                Call(interaction, "UpdateLiveRegions"); CollectionAssert.AreEqual(sourceUvs, Read<Mesh>(view, "sourceMesh").uv); CollectionAssert.AreEqual(sourceUvs, Read<Mesh>(view, "copyMesh").uv);
                Call(interaction, "MoveSelected", new Vector2(0.56f, 0), 1f); Call(interaction, "MoveSelected", region.Destination, 1.25f);
                Assert.AreEqual("Move copy into open space to enlarge.\nStick down: shrink.", Read<string>(interaction, "actionInstruction"));
                Call(interaction, "MoveSelected", region.SourceCenter, region.Scale);
                StringAssert.Contains("Keep copies", Read<string>(interaction, "actionInstruction"));
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        // Removing fit/refit or restoring the old glyph size must fail on real rendered bounds.
        [Test] public void MeasuredHudFitsAllInstructionsAndFontRefreshWithoutScalingDrift()
        {
            var owner = new GameObject("Measured HUD test"); var eye = new GameObject("Test eye");
            try
            {
                Camera camera = eye.AddComponent<Camera>(); camera.enabled = false;
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>();
                interaction.Initialize(new UnavailableSource(), eye.transform);
                TextMesh instruction = Read<TextMesh>(interaction, "instruction");
                TextMesh accept = eye.transform.Find("Head-relative drawing plane/Movable instructions and Accept/Accept regions label").GetComponent<TextMesh>();
                var failures = new List<string>();
                var samples = new List<string>(InstructionSamples);
                foreach (string direction in new[] { "center", "upper", "lower", "left", "right", "upper left", "upper right", "lower left", "lower right" })
                    samples.Add("Copy selected: " + direction + ".\nRight stick: resize. Grip: move.");
                foreach (string text in samples)
                {
                    Call(interaction, "SetInstruction", text); camera.Render();
                    Bounds bounds = instruction.GetComponent<Renderer>().bounds;
                    Debug.Log("HUD MEASURE " + text.Replace('\n', '|') + " size=" + bounds.size.ToString("F6") + " center=" + bounds.center.ToString("F6"));
                    AssertFiniteNonzero(bounds);
                    if (bounds.size.x > 0.6001f || bounds.size.y > 0.0801f) failures.Add(text);
                    Bounds plate = instruction.transform.parent.Find("Instruction backplate").GetComponent<Renderer>().bounds;
                    Assert.Greater(bounds.min.x, plate.min.x); Assert.Less(bounds.max.x, plate.max.x);
                    Assert.Greater(bounds.min.y, plate.min.y); Assert.Less(bounds.max.y, plate.max.y);
                    Bounds button = accept.GetComponent<Renderer>().bounds;
                    AssertFiniteNonzero(button);
                    if (bounds.min.y <= button.max.y) failures.Add("Overlap: " + text);
                }
                Bounds labelBounds = accept.GetComponent<Renderer>().bounds;
                Debug.Log("ACCEPT MEASURE size=" + labelBounds.size.ToString("F6"));
                AssertFiniteNonzero(labelBounds);
                Assert.LessOrEqual(labelBounds.size.x, 0.2401f); Assert.LessOrEqual(labelBounds.size.y, 0.0451f);
                Transform outer = Read<BoxCollider>(interaction, "acceptCollider").transform;
                Assert.AreEqual(0.28f, outer.GetComponent<Renderer>().bounds.size.x, 0.0001f);
                Assert.AreEqual(0.07f, outer.GetComponent<Renderer>().bounds.size.y, 0.0001f);
                Assert.GreaterOrEqual(labelBounds.min.x, outer.GetComponent<Renderer>().bounds.min.x);
                Assert.LessOrEqual(labelBounds.max.y, outer.GetComponent<Renderer>().bounds.max.y);
                Call(interaction, "SetInstruction", InstructionSamples[0]); camera.Render();
                Vector3 fitted = instruction.transform.localScale;
                // Simulate an atlas rebuild after invalidated label scale; callback must refit both labels.
                instruction.transform.localScale = Vector3.one; accept.transform.localScale = Vector3.one;
                Call(interaction, "RefreshFontTexture", instruction.font); camera.Render();
                Assert.AreEqual(fitted.x, instruction.transform.localScale.x, 0.00001f);
                // Texture notifications can precede geometry replacement. LateUpdate must fit that replacement.
                float instructionSize = instruction.characterSize, acceptSize = accept.characterSize;
                instruction.characterSize *= 2; accept.characterSize *= 2;
                Call(interaction, "LateUpdate"); camera.Render();
                Assert.LessOrEqual(instruction.GetComponent<Renderer>().bounds.size.y, 0.0801f);
                Assert.LessOrEqual(accept.GetComponent<Renderer>().bounds.size.x, 0.2401f);
                instruction.characterSize = instructionSize; accept.characterSize = acceptSize;
                for (int i = 0; i < 3; i++) Call(interaction, "RefreshFontTexture", instruction.font);
                Assert.AreEqual(fitted.x, instruction.transform.localScale.x, 0.00001f);
                Assert.LessOrEqual(accept.GetComponent<Renderer>().bounds.size.x, 0.2401f);
                Assert.IsEmpty(failures, string.Join("; ", failures));
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        [Test] public void DefaultHudCentersSitBelowEyeAndBackplateContainsMeasuredGlyphs()
        {
            var owner = new GameObject("HUD layout test"); var eye = new GameObject("Test eye");
            try
            {
                var camera = eye.AddComponent<Camera>(); camera.enabled = false;
                var interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                camera.Render();
                Transform hud = Read<Transform>(interaction, "hud");
                Assert.AreEqual(-0.08f, hud.localPosition.y, 0.0001f);
                TextMesh instruction = Read<TextMesh>(interaction, "instruction");
                Assert.AreEqual(0.055f, instruction.transform.localPosition.y, 0.0001f);
                Assert.AreEqual(-0.08f, Read<BoxCollider>(interaction, "acceptCollider").transform.localPosition.y, 0.0001f);
                Bounds text = instruction.GetComponent<Renderer>().bounds; AssertFiniteNonzero(text);
                Bounds backplate = hud.Find("Instruction backplate").GetComponent<Renderer>().bounds;
                Assert.Greater(text.min.x, backplate.min.x); Assert.Less(text.max.x, backplate.max.x);
                Assert.Greater(text.min.y, backplate.min.y); Assert.Less(text.max.y, backplate.max.y);
                Assert.AreEqual(-1.5f, Mathf.Atan2(instruction.transform.position.y, instruction.transform.position.z) * Mathf.Rad2Deg, 0.15f);
                Transform button = Read<BoxCollider>(interaction, "acceptCollider").transform;
                Assert.AreEqual(-9.2f, Mathf.Atan2(button.position.y, button.position.z) * Mathf.Rad2Deg, 0.15f);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); }
        }
        private static void AssertFiniteNonzero(Bounds bounds)
        {
            Assert.IsFalse(float.IsNaN(bounds.size.x) || float.IsInfinity(bounds.size.x));
            Assert.IsFalse(float.IsNaN(bounds.size.y) || float.IsInfinity(bounds.size.y));
            Assert.Greater(bounds.size.x, 0f); Assert.Greater(bounds.size.y, 0f);
        }
        private static readonly string[] InstructionSamples =
        {
            "Hold right trigger: trace a loop.\nLeft stick: move this panel.",
            "Loop ready: release right trigger.\nB: undo. Hold left X: original view.",
            "Return to the start ring; release.\nB: undo. Hold left X: original view.",
            "Hold right trigger to resume this loop.\nB: discard trace. Return to start ring.",
            "Press A or Accept regions.\nB: undo. Left stick: move this panel.",
            "Right stick: resize. Grip: move.\nB: undo. Hold left X: original view.",
            "Grip a source or copy to move it.\nLeft stick: panel. Hold left X: original.",
            "Draw nearer the center of the view.\nHold left X: original view.",
            "Keep GazeShift open; wait for camera.",
            "Wear headset to resume the live view.\nLeft stick: move this panel.",
            "Allow camera access in Quest settings; reopen GazeShift.\nLeft stick: move this panel.",
            "Camera unavailable. Close and reopen GazeShift.\nLeft stick: move this panel.",
            "Use Quest 3 or 3S with Horizon OS v81 or newer.\nLeft stick: move this panel.",
            "Allow camera access in Quest settings, then reopen GazeShift.\nLeft stick: move this panel.",
            "Keep GazeShift open and wait for the camera.\nLeft stick: move this panel.",
            "Original view: release left X to return.\nLeft stick: move this panel.",
            "Connect right controller to draw or move.\nLeft stick: panel. Hold left X: original.",
            "Loop too long. Trace a shorter loop.\nPress trigger again to restart.",
            "Hold right trigger to resume this loop.\nB: discard trace; draw away from copies.",
            "Move copy into open space to enlarge.\nStick down: shrink.",
            "Keep copies inside the view and apart.\nMove elsewhere or right stick down.",
            "No room for a full-size copy.\nB: undo; trace a smaller region.",
            "Copy selected: upper right.\nRight stick: resize. Grip: move."
        };
        [Test] public void StartupDiagnosticsNeedsTwoDistinctEligibleFramesAndCompletesOnce()
        {
            var type = typeof(RegionInteraction).Assembly.GetType("GazeShift.QuestStartupDiagnostics");
            Assert.IsNotNull(type, "Startup diagnostics component is missing.");
            var owner = new GameObject("Diagnostics frame test");
            try
            {
                Component diagnostic = owner.AddComponent(type);
                Call(diagnostic, "Subscribe"); Assert.IsTrue(Read<bool>(diagnostic, "subscribed"));
                Assert.IsFalse((bool)Call(diagnostic, "TryCompleteFrame", 10, false));
                Assert.IsFalse((bool)Call(diagnostic, "TryCompleteFrame", 11, true));
                Assert.IsFalse((bool)Call(diagnostic, "TryCompleteFrame", 11, true));
                Assert.IsTrue((bool)Call(diagnostic, "TryCompleteFrame", 12, true));
                Assert.IsFalse((bool)Call(diagnostic, "TryCompleteFrame", 13, true));
                Assert.IsFalse(Read<bool>(diagnostic, "subscribed"));
            }
            finally { Object.DestroyImmediate(owner); }
        }
        [Test] public void StartupDiagnosticsTimesOutFromUpdateWithoutCameraCallbacksAndUnsubscribesOnDisable()
        {
            var type = typeof(RegionInteraction).Assembly.GetType("GazeShift.QuestStartupDiagnostics");
            Assert.IsNotNull(type, "Startup diagnostics component is missing.");
            var owner = new GameObject("Diagnostics timeout test");
            try
            {
                Component diagnostic = owner.AddComponent(type);
                Call(diagnostic, "Subscribe");
                Write(diagnostic, "startedAt", Time.realtimeSinceStartupAsDouble - 16d);
                Call(diagnostic, "Update");
                Assert.IsTrue(Read<bool>(diagnostic, "completed"));
                Assert.IsFalse(Read<bool>(diagnostic, "subscribed"));
                Call(diagnostic, "Update");
                Assert.IsFalse((bool)Call(diagnostic, "TryCompleteFrame", 20, true));
            }
            finally { Object.DestroyImmediate(owner); }
            var second = new GameObject("Diagnostics disable test");
            try
            {
                Component diagnostic = second.AddComponent(type); Call(diagnostic, "Subscribe");
                Call(diagnostic, "OnDisable"); Assert.IsFalse(Read<bool>(diagnostic, "subscribed"));
                Assert.IsFalse((bool)Call(diagnostic, "TryCompleteFrame", 20, true));
            }
            finally { Object.DestroyImmediate(second); }
        }
        private static LineRenderer FindLineRenderer(Transform parent, string name)
        {
            var matches = new List<LineRenderer>();
            foreach (LineRenderer line in parent.GetComponentsInChildren<LineRenderer>())
                if (line.gameObject.name == name) matches.Add(line);
            Assert.AreEqual(1, matches.Count, "Expected exactly one renderer named " + name);
            return matches[0];
        }
        private static void AssertPairHiddenAndUnbound(LiveRegionView view)
        {
            foreach (string field in new[] { "sourceObject", "copyObject" })
            {
                MeshRenderer renderer = Read<GameObject>(view, field).GetComponent<MeshRenderer>();
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                Assert.IsFalse(renderer.enabled); Assert.IsNull(properties.GetTexture("_MainTex"));
            }
        }
        private static void Write(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static void DestroyInteraction(GameObject owner)
        {
            if (owner == null) return;
            // This runtime-only behaviour receives no automatic lifecycle callbacks in EditMode.
            Call(owner.GetComponent<RegionInteraction>(), "OnDestroy");
            Object.DestroyImmediate(owner);
        }
        private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static object Call(object owner, string method, params object[] arguments) => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, arguments);
        private static void DrawCircle(RegionSession session, Vector2 center)
        {
            session.BeginTrigger(false);
            for (int i = 0; i <= 24; i++)
            {
                double angle = i * Math.PI * 2 / 24;
                session.AddPoint(center + new Vector2((float)Math.Cos(angle) * 0.08f, (float)Math.Sin(angle) * 0.08f));
            }
            Assert.IsTrue(session.FinishStroke());
        }
        private sealed class LiveSource : ICameraFrameSource
        {
            private readonly Texture texture;
            public bool Ready = true;
            public bool Covered = true;
            public LiveSource(Texture frame) => texture = frame;
            public bool IsReady => Ready;
            public Texture FrameTexture => Ready ? texture : null;
            public bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv) { uv = new Vector2(0.5f + worldPoint.x * 0.1f, 0.5f + worldPoint.y * 0.1f); return Covered; }
        }
        private sealed class UnavailableSource : ICameraFrameSource
        {
            public bool IsReady => false;
            public Texture FrameTexture => null;
            public bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv) { uv = default; return false; }
        }
    }
}
