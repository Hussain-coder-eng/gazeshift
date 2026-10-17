using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeShift.Tests
{
    public sealed class RegionGeometryTests
    {
        private static readonly Vector2[] Square = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        private static readonly Vector2[] Concave = { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) };

        [Test] public void ConvexPolygonFillsItsOriginalArea()
        {
            Assert.IsTrue(RegionGeometry.TryTriangulate(Square, out Vector2[] vertices, out int[] indices));
            Assert.AreEqual(6, indices.Length);
            Assert.AreEqual(1f, TriangleArea(vertices, indices), 0.00001f);
        }

        [Test] public void ConcavePolygonPreservesNotchAndArea()
        {
            Assert.IsTrue(RegionGeometry.TryTriangulate(Concave, out Vector2[] vertices, out int[] indices));
            CollectionAssert.AreEqual(Concave, vertices);
            Assert.AreEqual(3f, TriangleArea(vertices, indices), 0.00001f);
            Assert.IsFalse(RegionGeometry.ContainsPoint(vertices, new Vector2(1.5f, 1.5f)));
        }

        [Test] public void ReversedWindingStillFillsConcavePolygon()
        {
            Vector2[] reversed = (Vector2[])Concave.Clone();
            Array.Reverse(reversed);
            Assert.IsTrue(RegionGeometry.TryTriangulate(reversed, out Vector2[] vertices, out int[] indices));
            Assert.AreEqual(3f, TriangleArea(vertices, indices), 0.00001f);
        }

        [Test] public void DuplicateClosingPointIsRemoved()
        {
            Vector2[] closed = { Square[0], Square[1], Square[2], Square[3], Square[0] };
            Assert.IsTrue(RegionGeometry.TryTriangulate(closed, out Vector2[] vertices, out _));
            Assert.AreEqual(4, vertices.Length);
        }

        [Test] public void CrossingBoundaryIsRejected()
        {
            Vector2[] crossed = { new Vector2(0, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(2, 0) };
            Assert.IsFalse(RegionGeometry.TryTriangulate(crossed, out _, out _));
        }

        [Test] public void NonfinitePointIsRejected()
        {
            Vector2[] invalid = { new Vector2(float.NaN, 0), new Vector2(1, 0), new Vector2(0, 1) };
            Assert.IsFalse(RegionGeometry.TryTriangulate(invalid, out _, out _));
        }

        [Test] public void ZeroAreaIsRejected()
        {
            Vector2[] line = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0) };
            Assert.IsFalse(RegionGeometry.TryTriangulate(line, out _, out _));
        }

        [Test] public void PolygonEdgeCountsAsInside()
        {
            Assert.IsTrue(RegionGeometry.ContainsPoint(Square, new Vector2(1, 0.5f)));
            Assert.IsFalse(RegionGeometry.ContainsPoint(Square, new Vector2(1.1f, 0.5f)));
        }

        [Test] public void CrossingPolygonsOverlapWithoutContainedVertices()
        {
            Vector2[] horizontal = { new Vector2(-2, -0.2f), new Vector2(2, -0.2f), new Vector2(2, 0.2f), new Vector2(-2, 0.2f) };
            Vector2[] vertical = { new Vector2(-0.2f, -2), new Vector2(0.2f, -2), new Vector2(0.2f, 2), new Vector2(-0.2f, 2) };
            Assert.IsTrue(RegionGeometry.Overlaps(horizontal, vertical));
        }

        [Test] public void RayRejectsPlaneBehindControllerAndParallelDirection()
        {
            Assert.IsFalse(RegionGeometry.TryCanvasHit(new Vector3(0, 0, 1), Vector3.forward, Vector2.one, out _));
            Assert.IsFalse(RegionGeometry.TryCanvasHit(new Vector3(0, 0, -1), Vector3.right, Vector2.one, out _));
            Assert.IsTrue(RegionGeometry.TryCanvasHit(new Vector3(0.2f, 0.1f, -1), Vector3.forward, Vector2.one, out Vector2 hit));
            Assert.AreEqual(new Vector2(0.2f, 0.1f), hit);
        }

        [Test] public void CollinearStrokeSamplesPreserveBoundaryAndArea()
        {
            var points = new Vector2[40];
            for (int i = 0; i < 10; i++)
            {
                float t = i / 10f;
                points[i] = new Vector2(t, 0); points[10 + i] = new Vector2(1, t);
                points[20 + i] = new Vector2(1 - t, 1); points[30 + i] = new Vector2(0, 1 - t);
            }
            Assert.IsTrue(RegionGeometry.TryTriangulate(points, out Vector2[] vertices, out int[] indices));
            Assert.AreEqual(40, vertices.Length);
            Assert.AreEqual(1f, TriangleArea(vertices, indices), 0.00001f);
        }
        [Test] public void AccidentalTinyPolygonIsRejected()
        {
            Vector2[] tiny = { Vector2.zero, new Vector2(0.001f, 0), new Vector2(0, 0.001f) };
            Assert.IsFalse(RegionGeometry.TryTriangulate(tiny, out _, out _));
        }

        [Test] public void ContainedDenseBoundariesAvoidQuadraticCollisionScan()
        {
            var outer = new CountingPolygon(DenseRectangle(0.64f, 0.39f));
            var inner = new CountingPolygon(DenseRectangle(0.16f, 0.1f));
            Assert.IsTrue(RegionGeometry.Overlaps(outer, inner));
            Assert.LessOrEqual(outer.Reads + inner.Reads, 8192);
        }
        [Test] public void ExhaustedCollisionBudgetConservativelyBlocksCandidate()
        {
            int remaining = 4;
            Assert.IsTrue(RegionGeometry.Overlaps(DenseRectangle(0.64f, 0.39f), DenseRectangle(0.16f, 0.1f), ref remaining));
            Assert.AreEqual(0, remaining);
        }
        private static Vector2[] DenseRectangle(float halfWidth, float halfHeight)
        {
            var points = new Vector2[480];
            for (int i = 0; i < 120; i++)
            {
                float t = i / 120f;
                points[i] = new Vector2(-halfWidth + 2 * halfWidth * t, -halfHeight);
                points[120 + i] = new Vector2(halfWidth, -halfHeight + 2 * halfHeight * t);
                points[240 + i] = new Vector2(halfWidth - 2 * halfWidth * t, halfHeight);
                points[360 + i] = new Vector2(-halfWidth, halfHeight - 2 * halfHeight * t);
            }
            return points;
        }
        private sealed class CountingPolygon : IReadOnlyList<Vector2>
        {
            private readonly Vector2[] points;
            public int Reads { get; private set; }
            public CountingPolygon(Vector2[] boundary) => points = boundary;
            public int Count => points.Length;
            public Vector2 this[int index] { get { Reads++; return points[index]; } }
            public IEnumerator<Vector2> GetEnumerator() => ((IEnumerable<Vector2>)points).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static float TriangleArea(Vector2[] vertices, int[] indices)
        {
            float sum = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector2 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                sum += Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            return sum;
        }
    }
}
