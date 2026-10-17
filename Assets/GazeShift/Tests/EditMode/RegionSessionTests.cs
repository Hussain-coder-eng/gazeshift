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
