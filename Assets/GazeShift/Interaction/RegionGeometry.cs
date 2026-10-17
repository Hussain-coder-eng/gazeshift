using System;
using System.Collections.Generic;
using UnityEngine;

namespace GazeShift
{
    public static class RegionGeometry
    {
        public const float Epsilon = 0.000001f;
        public const float MinimumArea = 0.0004f;

        public static bool TryCanvasHit(Vector3 origin, Vector3 direction, Vector2 halfExtent, out Vector2 hit)
        {
            const float minimumDistance = 0.01f;
            const float maximumDistance = 4f;
            hit = default;
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z) || !IsFinite(direction.x) || !IsFinite(direction.y) || !IsFinite(direction.z) || Math.Abs(direction.z) < Epsilon) return false;
            float distance = -origin.z / direction.z;
            if (distance < minimumDistance || distance > maximumDistance) return false;
            hit = new Vector2(origin.x + direction.x * distance, origin.y + direction.y * distance);
            return IsFinite(hit) && Math.Abs(hit.x) <= halfExtent.x && Math.Abs(hit.y) <= halfExtent.y;
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(Vector2 point) => IsFinite(point.x) && IsFinite(point.y);

        public static bool TryTriangulate(IReadOnlyList<Vector2> points, out Vector2[] vertices, out int[] triangles)
        {
            vertices = Array.Empty<Vector2>();
            triangles = Array.Empty<int>();
            if (points == null) return false;
            var boundary = new List<Vector2>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                if (!IsFinite(points[i])) return false;
                if (boundary.Count == 0 || (points[i] - boundary[boundary.Count - 1]).sqrMagnitude > Epsilon * Epsilon)
                    boundary.Add(points[i]);
            }
            if (boundary.Count > 1 && (boundary[0] - boundary[boundary.Count - 1]).sqrMagnitude <= Epsilon * Epsilon)
                boundary.RemoveAt(boundary.Count - 1);
            if (boundary.Count < 3 || Math.Abs(SignedArea(boundary)) < MinimumArea) return false;
            for (int i = 0; i < boundary.Count; i++)
                for (int j = i + 1; j < boundary.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == boundary.Count - 1)) continue;
                    if (SegmentsIntersect(boundary[i], boundary[(i + 1) % boundary.Count], boundary[j], boundary[(j + 1) % boundary.Count])) return false;
                }

            var remaining = new List<int>(boundary.Count);
            for (int i = 0; i < boundary.Count; i++) remaining.Add(i);
            if (SignedArea(boundary) < 0) remaining.Reverse();
            var indices = new List<int>((boundary.Count - 2) * 3);
            while (remaining.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    float turn = Cross(boundary[a], boundary[b], boundary[c]);
                    if (Math.Abs(turn) <= Epsilon)
                    {
                        remaining.RemoveAt(i); clipped = true; break;
                    }
                    if (turn < 0) continue;
                    bool contains = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        int candidate = remaining[j];
                        if (candidate == a || candidate == b || candidate == c) continue;
                        Vector2 p = boundary[candidate];
                        if (Cross(boundary[a], boundary[b], p) >= -Epsilon && Cross(boundary[b], boundary[c], p) >= -Epsilon && Cross(boundary[c], boundary[a], p) >= -Epsilon)
                        { contains = true; break; }
                    }
                    if (contains) continue;
                    indices.Add(a); indices.Add(b); indices.Add(c);
                    remaining.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) return false;
            }
            if (remaining.Count != 3 || Cross(boundary[remaining[0]], boundary[remaining[1]], boundary[remaining[2]]) <= Epsilon) return false;
            indices.AddRange(remaining);
            vertices = boundary.ToArray();
            triangles = indices.ToArray();
            return true;
        }

        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float twiceArea = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                twiceArea += a.x * b.y - b.x * a.y;
            }
            return twiceArea * 0.5f;
        }

        public static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            if (polygon == null || polygon.Count < 3 || !IsFinite(point)) return false;
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i];
                if (OnSegment(a, b, point)) return true;
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public static bool Overlaps(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (SegmentsIntersect(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
            return ContainsPoint(a, b[0]) || ContainsPoint(b, a[0]);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p) => Math.Abs(Cross(a, b, p)) <= Epsilon && p.x >= Math.Min(a.x, b.x) - Epsilon && p.x <= Math.Max(a.x, b.x) + Epsilon && p.y >= Math.Min(a.y, b.y) - Epsilon && p.y <= Math.Max(a.y, b.y) + Epsilon;
        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            if (((abC > Epsilon && abD < -Epsilon) || (abC < -Epsilon && abD > Epsilon)) && ((cdA > Epsilon && cdB < -Epsilon) || (cdA < -Epsilon && cdB > Epsilon))) return true;
            return OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b);
        }
    }
}
