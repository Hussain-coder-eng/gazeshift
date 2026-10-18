using System;
using NUnit.Framework;
using UnityEngine;

namespace GazeShift.Tests
{
    public sealed class RegionSessionTests
    {
        [Test] public void AcceptHitDoesNotStartStroke()
        {
            var session = new RegionSession();
            session.BeginTrigger(false);
            Assert.IsTrue(session.IsDrawing);
            session.AddPoint(Vector2.zero);
            session.BeginTrigger(true);
            Assert.IsFalse(session.IsDrawing);
            Assert.AreEqual(0, session.Stroke.Count);
        }
        [Test] public void TrackingInterruptionDiscardsStroke()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.2f, 0));
            Assert.IsTrue(session.IsDrawing);
            session.CancelStroke();
            Assert.IsFalse(session.FinishStroke());
            Assert.AreEqual(0, session.Pending.Count);
        }
        [Test] public void TwoAcceptedSourcesKeepSeparateCopies()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.2f, 0)); Assert.IsTrue(session.FinishStroke());
            DrawCircle(session, new Vector2(0.2f, 0)); Assert.IsTrue(session.FinishStroke());
            Assert.IsTrue(session.AcceptPending());
            Assert.AreEqual(2, session.Accepted.Count);
            Assert.AreEqual(0, session.Pending.Count);
            Assert.IsFalse(RegionGeometry.Overlaps(session.Accepted[0].DestinationPolygon(), session.Accepted[1].DestinationPolygon()));
            Assert.IsFalse(RegionGeometry.Overlaps(session.Accepted[0].Source, session.Accepted[0].DestinationPolygon()));
            Assert.IsFalse(RegionGeometry.Overlaps(session.Accepted[1].Source, session.Accepted[1].DestinationPolygon()));
        }
        [Test] public void InvalidDestinationKeepsLastValidPosition()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.2f, 0)); session.FinishStroke(); session.AcceptPending();
            RegionData region = session.Accepted[0]; Vector2 original = region.Destination;
            Assert.IsFalse(session.TryPlace(region, region.SourceCenter, 1));
            Assert.AreEqual(original, region.Destination);
            Assert.IsFalse(session.TryPlace(region, new Vector2(9, 0), 1));
            Assert.AreEqual(original, region.Destination);
        }
        [Test] public void ResizeIsFiniteAndBounded()
        {
            Assert.AreEqual(RegionSession.MaximumScale, RegionSession.ClampScale(1, 100));
            Assert.AreEqual(RegionSession.MinimumScale, RegionSession.ClampScale(1, -100));
            Assert.AreEqual(1, RegionSession.ClampScale(1, float.NaN));
            Assert.AreEqual(RegionSession.MinimumScale, RegionSession.ClampScale(-2, float.NaN));
        }
        [Test] public void OpenStrokeIsRejectedAndCanBeExplicitlyDiscarded()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            session.AddPoint(new Vector2(0, 0)); session.AddPoint(new Vector2(0.1f, 0)); session.AddPoint(new Vector2(0, 0.1f));
            Assert.IsFalse(session.FinishStroke());
            session.CancelStroke();
            DrawCircle(session, Vector2.zero); session.AddPoint(new Vector2(0.3f, 0.3f));
            Assert.IsFalse(session.FinishStroke());
        }
        [Test] public void UndoRemovesLatestPendingThenAcceptedInReverseOrder()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.25f, 0)); session.FinishStroke();
            DrawCircle(session, new Vector2(0.25f, 0)); session.FinishStroke();
            Assert.IsTrue(session.AcceptPending());
            RegionData first = session.Accepted[0], second = session.Accepted[1];
            DrawCircle(session, new Vector2(0, -0.25f)); Assert.IsTrue(session.FinishStroke());
            RegionData pending = session.Pending[0];
            Assert.AreSame(pending, session.UndoLast());
            Assert.AreEqual(2, session.Accepted.Count);
            Assert.AreSame(second, session.UndoLast());
            Assert.AreSame(first, session.UndoLast());
            Assert.IsNull(session.UndoLast());
            Assert.AreEqual(0, session.Pending.Count); Assert.AreEqual(0, session.Accepted.Count);
        }
        [Test] public void AcceptanceUsesOriginalScaleAndClosestCandidateToCenter()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.35f, 0)); Assert.IsTrue(session.FinishStroke());
            Assert.IsTrue(session.AcceptPending());
            Assert.AreEqual(1, session.Accepted[0].Scale);
            Assert.AreEqual(Vector2.zero, session.Accepted[0].Destination);
        }
        [Test] public void CrowdedCanvasDoesNotShrinkAndLeavesAllPendingUnchanged()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.5f, 0.25f)); Assert.IsTrue(session.FinishStroke());
            session.BeginTrigger(false);
            Vector2[] corners = { new Vector2(-0.34f, -0.3f), new Vector2(0.34f, -0.3f), new Vector2(0.34f, 0.3f), new Vector2(-0.34f, 0.3f) };
            for (int edge = 0; edge < 4; edge++)
                for (int i = 0; i < 10; i++) session.AddPoint(corners[edge] + (corners[(edge + 1) % 4] - corners[edge]) * (i / 10f));
            session.AddPoint(corners[0]); Assert.IsTrue(session.FinishStroke());
            Assert.IsFalse(session.AcceptPending());
            Assert.AreEqual(2, session.Pending.Count); Assert.AreEqual(0, session.Accepted.Count);
            foreach (RegionData region in session.Pending)
            {
                Assert.AreEqual(1, region.Scale); Assert.AreEqual(Vector2.zero, region.Destination);
                Assert.IsFalse(region.HasPlacement);
            }
            Assert.LessOrEqual(session.LastPlacementAttempts, 200, "Finite candidate search must remain bounded.");
        }
        [Test] public void FourCornerClosureUsesDistinctGeometryAndBowtieNeverBecomesReady()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            Vector2[] square = { Vector2.zero, new Vector2(0.1f, 0), new Vector2(0.1f, 0.1f), new Vector2(0, 0.1f), Vector2.zero };
            foreach (Vector2 point in square) session.AddPoint(point);
            Assert.IsTrue(session.IsClosureReady); Assert.IsTrue(session.FinishStroke());
            CollectionAssert.AreEqual(new[] { square[0], square[1], square[2], square[3] }, session.Pending[0].Source);
            session.BeginTrigger(false);
            foreach (Vector2 point in new[] { square[0], square[2], square[3], square[1], square[0] }) session.AddPoint(point);
            Assert.IsFalse(session.IsClosureReady); Assert.IsFalse(session.FinishStroke());
            Assert.AreEqual(1, session.Pending.Count);
        }
        [Test] public void ThreeDistinctVerticesCloseButDegenerateAndNonfiniteInputsCannotCreateRegion()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            foreach (Vector2 point in new[] { Vector2.zero, new Vector2(0.1f, 0), new Vector2(0, 0.1f), Vector2.zero }) session.AddPoint(point);
            Assert.IsTrue(session.IsClosureReady); Assert.IsTrue(session.FinishStroke());
            Assert.AreEqual(3, session.Pending[0].Source.Length);
            session.BeginTrigger(false);
            foreach (Vector2 point in new[] { Vector2.zero, new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 0), new Vector2(9, 0), new Vector2(0.1f, 0), Vector2.zero }) session.AddPoint(point);
            Assert.AreEqual(3, session.Stroke.Count);
            Assert.IsFalse(session.IsClosureReady); Assert.IsFalse(session.FinishStroke());
        }
        [Test] public void InsideZoneOvershootKeepsExactValidSquarePrefixRatherThanRepairingBoundary()
        {
            var session = new RegionSession(); DrawSampledSquare(session);
            Assert.IsTrue(session.IsClosureReady);
            session.AddPoint(Vector2.zero);
            Vector2[] validPrefix = session.Stroke.GetRange(0, 40).ToArray();
            session.AddPoint(new Vector2(0.005f, -0.005f));
            Assert.IsTrue(session.IsClosureReady); Assert.IsTrue(session.FinishStroke());
            CollectionAssert.AreEqual(validPrefix, session.Pending[0].Source);
            Assert.IsTrue(RegionGeometry.TryTriangulate(session.Pending[0].Source, out _, out _));
            Assert.AreEqual(0.01f, RegionGeometry.SignedArea(session.Pending[0].Source), 0.000001f);
        }
        [Test] public void LeavingClosureZoneInvalidatesPrefixAndCrossingReturnCannotResurrectIt()
        {
            var session = new RegionSession(); DrawSampledSquare(session); session.AddPoint(Vector2.zero);
            Assert.IsTrue(session.IsClosureReady);
            session.AddPoint(new Vector2(0.2f, 0.2f)); Assert.IsFalse(session.IsClosureReady);
            session.AddPoint(new Vector2(0.005f, -0.005f));
            Assert.IsFalse(session.IsClosureReady); Assert.IsFalse(session.FinishStroke()); Assert.IsEmpty(session.Pending);
        }
        [Test] public void SubSpacingExitStillInvalidatesClosureCandidate()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            foreach (Vector2 point in new[] { Vector2.zero, new Vector2(0.1f, 0), new Vector2(0.1f, 0.1f), new Vector2(0, 0.1f), new Vector2(0, 0.0455f) }) session.AddPoint(point);
            Assert.IsTrue(session.IsClosureReady); int samples = session.Stroke.Count;
            session.AddPoint(new Vector2(0, 0.046f));
            Assert.AreEqual(samples, session.Stroke.Count, "The movement is less than the sampling distance.");
            Assert.IsFalse(session.IsClosureReady, "Sampling must not hide an exit from the closure ring.");
            Assert.IsFalse(session.FinishStroke());
        }
        [Test] public void InvalidReleaseRetainsTraceAndTriggerResumesBeforeStrokeOnlyUndo()
        {
            var session = new RegionSession(); DrawCircle(session, new Vector2(-0.3f, 0)); Assert.IsTrue(session.FinishStroke());
            session.BeginTrigger(false); session.AddPoint(Vector2.zero); session.AddPoint(new Vector2(0.1f, 0));
            Assert.IsFalse(session.FinishStroke()); Assert.IsFalse(session.IsDrawing); Assert.AreEqual(2, session.Stroke.Count);
            session.BeginTrigger(false); Assert.IsTrue(session.IsDrawing); Assert.AreEqual(2, session.Stroke.Count);
            Assert.IsNull(session.UndoLast()); Assert.AreEqual(1, session.Pending.Count); Assert.IsEmpty(session.Stroke);
            session.BeginTrigger(false); session.AddPoint(Vector2.zero); session.FinishStroke();
            Assert.IsNull(session.UndoLast()); Assert.AreEqual(1, session.Pending.Count);
            Assert.IsNotNull(session.UndoLast()); Assert.IsEmpty(session.Pending);
        }
        [TestCase(0f, 0f)] [TestCase(-0.25f, 0.12f)]
        public void InitialCopyCanEnlargeAndShrinkWithoutMovingOrChangingSource(float x, float y)
        {
            var session = new RegionSession(); DrawCircle(session, new Vector2(x, y)); Assert.IsTrue(session.FinishStroke());
            Vector2[] source = (Vector2[])session.Pending[0].Source.Clone();
            Assert.IsTrue(session.AcceptPending()); RegionData region = session.Accepted[0]; Vector2 center = region.Destination;
            Assert.AreEqual(1, region.Scale);
            Assert.IsTrue(session.TryPlace(region, center, 1.25f), "Initial copy must have room to grow without a drag.");
            Assert.AreEqual(center, region.Destination); CollectionAssert.AreEqual(source, region.Source);
            Assert.IsTrue(session.TryPlace(region, center, 0.5f)); Assert.AreEqual(center, region.Destination);
            CollectionAssert.AreEqual(source, region.Source);
        }
        [Test] public void MixedSizePendingCopiesEachKeepIndependentEnlargementHeadroom()
        {
            var session = new RegionSession();
            Vector2[][] boundaries = {
                new[] { new Vector2(0.20f, -0.10f), new Vector2(0.40f, -0.10f), new Vector2(0.40f, 0.10f), new Vector2(0.20f, 0.10f) },
                new[] { new Vector2(-0.295f, -0.04f), new Vector2(-0.215f, -0.04f), new Vector2(-0.215f, 0.04f), new Vector2(-0.295f, 0.04f) }
            };
            foreach (Vector2[] boundary in boundaries)
            {
                session.BeginTrigger(false);
                foreach (Vector2 point in boundary) session.AddPoint(point);
                session.AddPoint(boundary[0]); Assert.IsTrue(session.FinishStroke());
            }
            Assert.IsTrue(session.AcceptPending()); Assert.AreEqual(2, session.Accepted.Count);
            for (int i = 0; i < session.Accepted.Count; i++)
            {
                RegionData region = session.Accepted[i]; Vector2 center = region.Destination;
                Vector2[] source = (Vector2[])region.Source.Clone(); Vector2[] copy = (Vector2[])region.CopyBoundary.Clone();
                foreach (RegionData other in session.Accepted) Assert.AreEqual(1, other.Scale);
                Assert.IsTrue(session.TryPlace(region, center, 1.25f), "Final copy " + i + " lost its independent enlargement clearance.");
                Assert.AreEqual(center, region.Destination); CollectionAssert.AreEqual(source, region.Source);
                Assert.IsTrue(session.TryPlace(region, center, 1)); CollectionAssert.AreEqual(copy, region.CopyBoundary);
            }
        }
        [Test] public void ConcaveInitialCopyHasClearanceAcrossEnlargementPathAndPreservesNotch()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            Vector2[] source = { new Vector2(-0.12f, -0.12f), new Vector2(0.12f, -0.12f), new Vector2(0.12f, 0), Vector2.zero, new Vector2(0, 0.12f), new Vector2(-0.12f, 0.12f) };
            foreach (Vector2 point in source) session.AddPoint(point); session.AddPoint(source[0]);
            Assert.IsTrue(session.FinishStroke()); Assert.IsTrue(session.AcceptPending());
            RegionData region = session.Accepted[0]; Vector2 center = region.Destination;
            for (int i = 0; i <= 5; i++)
            {
                Assert.IsTrue(session.TryPlace(region, center, 1 + i * 0.05f), "Concave copy must stay clear while growing.");
                Assert.AreEqual(center, region.Destination); CollectionAssert.AreEqual(source, region.Source);
                Assert.IsFalse(region.ContainsCopy(center + new Vector2(0.06f, 0.06f) * region.Scale));
            }
        }
        [Test] public void RejectedEnlargementPreservesExactPoseBoundaryAndSource()
        {
            var session = new RegionSession(); DrawCircle(session, Vector2.zero); Assert.IsTrue(session.FinishStroke()); Assert.IsTrue(session.AcceptPending());
            RegionData region = session.Accepted[0]; Assert.IsTrue(session.TryPlace(region, new Vector2(0.56f, 0), 1));
            Vector2 center = region.Destination; Vector2[] boundary = (Vector2[])region.CopyBoundary.Clone(); Vector2[] source = (Vector2[])region.Source.Clone();
            Assert.IsFalse(session.TryPlace(region, center, 1.25f));
            Assert.AreEqual(center, region.Destination); Assert.AreEqual(1, region.Scale); CollectionAssert.AreEqual(boundary, region.CopyBoundary); CollectionAssert.AreEqual(source, region.Source);
            Assert.IsTrue(session.TryPlace(region, new Vector2(0.17f, 0), 1));
            center = region.Destination; boundary = (Vector2[])region.CopyBoundary.Clone();
            Assert.IsFalse(session.TryPlace(region, center, 1.25f));
            Assert.AreEqual(center, region.Destination); Assert.AreEqual(1, region.Scale); CollectionAssert.AreEqual(boundary, region.CopyBoundary); CollectionAssert.AreEqual(source, region.Source);
        }
        private static void DrawSampledSquare(RegionSession session)
        {
            session.BeginTrigger(false);
            Vector2[] corners = { Vector2.zero, new Vector2(0.1f, 0), new Vector2(0.1f, 0.1f), new Vector2(0, 0.1f) };
            for (int edge = 0; edge < 4; edge++)
                for (int i = 0; i < 10; i++) session.AddPoint(Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], i / 10f));
        }
        [Test] public void SampleOverflowCancelsAndNextTriggerCanRestart()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            for (int i = 0; i < 512; i++) session.AddPoint(new Vector2((i % 2) * 0.01f, 0));
            Assert.IsTrue(session.IsDrawing); Assert.IsFalse(session.StrokeOverflowed);
            session.AddPoint(new Vector2(0, 0.01f));
            Assert.IsFalse(session.IsDrawing); Assert.AreEqual(0, session.Stroke.Count);
            Assert.IsTrue(session.StrokeOverflowed); Assert.IsFalse(session.FinishStroke());
            session.BeginTrigger(false); session.AddPoint(Vector2.zero);
            Assert.IsFalse(session.StrokeOverflowed); Assert.AreEqual(1, session.Stroke.Count);
        }
        [Test] public void BlockedMovementFeedbackHasAnIntervalAndStopsImmediately()
        {
            var feedback = new InteractionFeedback();
            Assert.IsTrue(feedback.TryReject(10));
            Assert.IsFalse(feedback.TryReject(10.1f));
            Assert.IsFalse(feedback.TryReject(10.5f));
            Assert.IsTrue(feedback.TryReject(10.8f));
            feedback.BeginPulse(11, 0.1f);
            Assert.IsTrue(feedback.IsPulsing(11.05f));
            Assert.IsFalse(feedback.IsPulsing(11.2f));
            feedback.BeginPulse(12, 0.1f); feedback.Stop();
            Assert.IsFalse(feedback.IsPulsing(12.01f));
            Assert.IsFalse(feedback.TryReject(float.NaN));
        }
        [Test] public void ResizePreservesPolygonCenterAndShape()
        {
            var session = new RegionSession(); DrawCircle(session, Vector2.zero); session.FinishStroke(); session.AcceptPending();
            RegionData region = session.Accepted[0]; Vector2 original = region.Source[0];
            Assert.IsTrue(session.TryPlace(region, new Vector2(0.4f, 0.2f), 0.5f));
            Vector2 copy = region.DestinationPolygon()[0];
            Assert.AreEqual(0.44f, copy.x, 0.00001f); Assert.AreEqual(0.2f, copy.y, 0.00001f);
            Assert.AreEqual(original, region.Source[0]);
        }
        [Test] public void NewSourceCannotCoverExistingSharpCopy()
        {
            var session = new RegionSession(); DrawCircle(session, Vector2.zero); session.FinishStroke(); session.AcceptPending();
            DrawCircle(session, session.Accepted[0].Destination);
            Assert.IsFalse(session.IsClosureReady, "An existing copy also blocks the readiness pulse.");
            Assert.IsFalse(session.FinishStroke());
            Assert.AreEqual(0, session.Pending.Count);
        }
        [Test] public void NearlyFullCanvasRejectsWithinBoundedPlacementAttempts()
        {
            var session = new RegionSession();
            session.BeginTrigger(false);
            for (int i = 0; i < 120; i++) session.AddPoint(new Vector2(-0.64f + 1.28f * i / 120, -0.39f));
            for (int i = 0; i < 120; i++) session.AddPoint(new Vector2(0.64f, -0.39f + 0.78f * i / 120));
            for (int i = 0; i < 120; i++) session.AddPoint(new Vector2(0.64f - 1.28f * i / 120, 0.39f));
            for (int i = 0; i < 120; i++) session.AddPoint(new Vector2(-0.64f, 0.39f - 0.78f * i / 120));
            session.AddPoint(new Vector2(-0.64f, -0.39f));
            Assert.IsTrue(session.FinishStroke());
            Assert.IsFalse(session.AcceptPending());
            Assert.LessOrEqual(session.LastPlacementAttempts, 100, "Finite candidate search must remain bounded.");
            Assert.AreEqual(1, session.Pending.Count); Assert.AreEqual(0, session.Accepted.Count);
        }
        [Test] public void DenseConvexAndConcaveStrokesStillAcceptExactCopies()
        {
            var circle = new RegionSession(); circle.BeginTrigger(false);
            for (int i = 0; i <= 480; i++)
            {
                double angle = i * Math.PI * 2 / 480;
                circle.AddPoint(new Vector2((float)Math.Cos(angle) * 0.16f, (float)Math.Sin(angle) * 0.16f));
            }
            Assert.IsTrue(circle.FinishStroke()); Assert.IsTrue(circle.AcceptPending(), "Dense circle must still fit at scale1");
            Assert.AreEqual(480, circle.Accepted[0].Source.Length);
            Assert.IsFalse(RegionGeometry.Overlaps(circle.Accepted[0].Source, circle.Accepted[0].DestinationPolygon()));
            var concave = new RegionSession(); concave.BeginTrigger(false);
            Vector2[] corners = { new Vector2(-0.12f, -0.12f), new Vector2(0.12f, -0.12f), new Vector2(0.12f, 0), Vector2.zero, new Vector2(0, 0.12f), new Vector2(-0.12f, 0.12f) };
            for (int edge = 0; edge < corners.Length; edge++)
                for (int i = 0; i < 80; i++) concave.AddPoint(corners[edge] + (corners[(edge + 1) % corners.Length] - corners[edge]) * (i / 80f));
            concave.AddPoint(corners[0]);
            Assert.IsTrue(concave.FinishStroke()); Assert.IsTrue(concave.AcceptPending(), "Dense concave region must still fit at scale1");
            Assert.AreEqual(480, concave.Accepted[0].Source.Length);
            Assert.IsFalse(RegionGeometry.Overlaps(concave.Accepted[0].Source, concave.Accepted[0].DestinationPolygon()));
        }
        [Test] public void DenseLargeCircleFindsValidScaleOneCornerWithinBudget()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            for (int i = 0; i <= 480; i++)
            {
                double angle = i * Math.PI * 2 / 480;
                session.AddPoint(new Vector2((float)Math.Cos(angle) * 0.22f, (float)Math.Sin(angle) * 0.22f));
            }
            Assert.IsTrue(session.FinishStroke()); Assert.IsTrue(session.AcceptPending());
            RegionData region = session.Accepted[0];
            Assert.AreEqual(480, region.Source.Length); Assert.AreEqual(1, region.Scale);
            Assert.LessOrEqual(session.LastPlacementAttempts, 100, "Finite candidate search must remain bounded.");
            Assert.IsFalse(RegionGeometry.Overlaps(region.Source, region.DestinationPolygon()));
            Assert.AreEqual(0.425f, Math.Abs(region.Destination.x), 0.00001f);
            Assert.AreEqual(0.175f, Math.Abs(region.Destination.y), 0.00001f);
        }
        private static void DrawCircle(RegionSession session, Vector2 center)
        {
            session.BeginTrigger(false);
            for (int i = 0; i <= 24; i++)
            {
                double angle = i * Math.PI * 2 / 24;
                session.AddPoint(center + new Vector2((float)Math.Cos(angle) * 0.08f, (float)Math.Sin(angle) * 0.08f));
            }
        }
    }
}
