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
        [Test] public void OpenOrUndersampledStrokeIsRejected()
        {
            var session = new RegionSession(); session.BeginTrigger(false);
            session.AddPoint(new Vector2(0, 0)); session.AddPoint(new Vector2(0.1f, 0)); session.AddPoint(new Vector2(0, 0.1f));
            Assert.IsFalse(session.FinishStroke());
            DrawCircle(session, Vector2.zero); session.AddPoint(new Vector2(0.3f, 0.3f));
            Assert.IsFalse(session.FinishStroke());
        }
        [Test] public void UndoRemovesOnlyLatestPendingRegion()
        {
            var session = new RegionSession();
            DrawCircle(session, new Vector2(-0.2f, 0)); session.FinishStroke(); session.AcceptPending();
            DrawCircle(session, new Vector2(0.2f, 0)); session.FinishStroke(); session.UndoPending();
            Assert.AreEqual(0, session.Pending.Count); Assert.AreEqual(1, session.Accepted.Count);
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
            Assert.LessOrEqual(session.LastPlacementAttempts, 52);
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
            Assert.IsTrue(circle.FinishStroke()); Assert.IsTrue(circle.AcceptPending());
            Assert.AreEqual(480, circle.Accepted[0].Source.Length);
            Assert.IsFalse(RegionGeometry.Overlaps(circle.Accepted[0].Source, circle.Accepted[0].DestinationPolygon()));
            var concave = new RegionSession(); concave.BeginTrigger(false);
            Vector2[] corners = { new Vector2(-0.12f, -0.12f), new Vector2(0.12f, -0.12f), new Vector2(0.12f, 0), Vector2.zero, new Vector2(0, 0.12f), new Vector2(-0.12f, 0.12f) };
            for (int edge = 0; edge < corners.Length; edge++)
                for (int i = 0; i < 80; i++) concave.AddPoint(corners[edge] + (corners[(edge + 1) % corners.Length] - corners[edge]) * (i / 80f));
            concave.AddPoint(corners[0]);
            Assert.IsTrue(concave.FinishStroke()); Assert.IsTrue(concave.AcceptPending());
            Assert.AreEqual(480, concave.Accepted[0].Source.Length);
            Assert.IsFalse(RegionGeometry.Overlaps(concave.Accepted[0].Source, concave.Accepted[0].DestinationPolygon()));
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
