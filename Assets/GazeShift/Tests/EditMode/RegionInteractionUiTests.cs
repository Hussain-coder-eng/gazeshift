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
                Assert.GreaterOrEqual(Read<TextMesh>(interaction, "instruction").characterSize, 0.027f);
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
                foreach (LineRenderer line in eye.GetComponentsInChildren<LineRenderer>())
                    if (line.gameObject.name == "Current stroke inner") { Assert.IsTrue(line.enabled); Assert.AreEqual(2, line.positionCount); }
                StringAssert.Contains("resume", (string)Call(interaction, "StageInstruction"));
                Call(interaction, "UndoLastRegion"); Assert.AreEqual(1, session.Accepted.Count); Assert.AreSame(accepted, Read<RegionData>(interaction, "selected"));
                Assert.IsEmpty(session.Stroke); Call(interaction, "UndoLastRegion"); Assert.IsEmpty(session.Accepted);
            }
            finally { DestroyInteraction(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(texture); }
        }
        [Test] public void ClosureRingMatchesThresholdAndPulseCanReenterAfterLeavingZone()
        {
            var owner = new GameObject("Closure UI test"); var eye = new GameObject("Test eye");
            try
            {
                RegionInteraction interaction = owner.AddComponent<RegionInteraction>(); interaction.Initialize(new UnavailableSource(), eye.transform);
                RegionSession session = Read<RegionSession>(interaction, "session"); session.BeginTrigger(false);
                foreach (Vector2 point in new[] { Vector2.zero, new Vector2(0.1f, 0), new Vector2(0.1f, 0.1f), new Vector2(0, 0.1f), new Vector2(0, 0.04f) }) session.AddPoint(point);
                Call(interaction, "UpdateStrokeOutline"); Assert.IsTrue(Read<bool>(interaction, "closureSignaled"));
                foreach (LineRenderer line in eye.GetComponentsInChildren<LineRenderer>())
                    if (line.gameObject.name == "Stroke start ring inner") Assert.AreEqual(0.04579214f, line.GetPosition(0).x, 0.000001f);
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
