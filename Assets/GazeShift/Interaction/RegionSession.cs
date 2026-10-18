using System;
using System.Collections.Generic;
using UnityEngine;

namespace GazeShift
{
    public sealed class RegionData
    {
        public readonly Vector2[] Source;
        public readonly int[] Triangles;
        public readonly Vector2 SourceCenter;
        public readonly Vector2 SourceHalfExtent;
        internal readonly Vector2[] PlacementBuffer;
        internal readonly Vector2[] CopyBoundary;
        internal bool HasPlacement;
        public Vector2 Destination { get; internal set; }
        public float Scale { get; internal set; } = 1;

        public RegionData(Vector2[] source, int[] triangles)
        {
            Source = source; Triangles = triangles;
            PlacementBuffer = new Vector2[source.Length]; CopyBoundary = new Vector2[source.Length];
            Vector2 min = source[0], max = source[0];
            for (int i = 1; i < source.Length; i++) { min = Vector2.Min(min, source[i]); max = Vector2.Max(max, source[i]); }
            SourceCenter = (min + max) * 0.5f; SourceHalfExtent = (max - min) * 0.5f;
        }
        public Vector2 CopyPoint(int index) => (Source[index] - SourceCenter) * Scale + Destination;
        public bool ContainsCopy(Vector2 point) => RegionGeometry.ContainsPoint(Source, (point - Destination) / Scale + SourceCenter);
        public Vector2[] DestinationPolygon()
        {
            var polygon = new Vector2[Source.Length];
            for (int i = 0; i < polygon.Length; i++) polygon[i] = CopyPoint(i);
            return polygon;
        }
    }

    public sealed class RegionSession
    {
        // Three distinct vertices are the geometric minimum; frame rate must not decide closure.
        public const int MinimumSamples = 3;
        public const int MaximumSamples = 512;
        public const float ClosureDiagonalFraction = 0.03f;
        public const float MinimumScale = 0.25f;
        public const float MaximumScale = 2;
        public const float SampleSpacing = 0.001f;
        private const float PlacementGap = 0.005f;
        private const int MaximumPlacementCandidates = 13;
        private const int MaximumAcceptanceChecks = 32768;
        // Physical feedback showed scale1 touching placements blocked the first enlargement.
        private const float InitialHeadroomScale = 1.25f;
        // Five interior rows include quarter-span positions missed by the legacy edge/center grid.
        private const int HeadroomGridSize = 5;
        private const int AdjacentCandidateCount = 4;
        private Vector2[] closureVertices;
        private int[] closureTriangles;
        private int remainingAcceptanceChecks;
        public static readonly Vector2 CanvasHalfExtent = new Vector2(0.65f, 0.4f);
        public bool IsDrawing { get; private set; }
        public static float ClosureRadius => (CanvasHalfExtent * 2).magnitude * ClosureDiagonalFraction;
        public bool IsClosureReady => IsDrawing && closureVertices != null && !CoversAcceptedCopy(closureVertices);
        public bool StrokeOverflowed { get; private set; }
        internal int LastPlacementAttempts { get; private set; }
        public readonly List<Vector2> Stroke = new List<Vector2>();
        public readonly List<RegionData> Pending = new List<RegionData>();
        public readonly List<RegionData> Accepted = new List<RegionData>();

        public void BeginTrigger(bool hitAccept)
        {
            if (hitAccept) CancelStroke();
            else IsDrawing = true;
            StrokeOverflowed = false;
        }
        public void AddPoint(Vector2 point)
        {
            if (!IsDrawing || !RegionGeometry.IsFinite(point) || !InsideCanvas(point)) return;
            // A sub-millimeter exit still ends readiness even when no new sample is stored.
            if (Stroke.Count > 0 && (Stroke[0] - point).sqrMagnitude > ClosureRadius * ClosureRadius) ClearClosureCandidate();
            if (Stroke.Count > 0 && (point - Stroke[Stroke.Count - 1]).sqrMagnitude < SampleSpacing * SampleSpacing) return;
            if (Stroke.Count == MaximumSamples) { CancelStroke(); StrokeOverflowed = true; return; }
            Stroke.Add(point);
            if ((Stroke[0] - point).sqrMagnitude > ClosureRadius * ClosureRadius)
            { ClearClosureCandidate(); return; }
            if (Stroke.Count >= MinimumSamples && RegionGeometry.TryTriangulate(Stroke, out Vector2[] vertices, out int[] indices) && !CoversAcceptedCopy(vertices))
            { closureVertices = vertices; closureTriangles = indices; }
            // Keep the last simple prefix for small in-ring release overshoots only.
            // Exiting the ring above invalidates it before any later re-entry can close.
        }
        private void ClearClosureCandidate() { closureVertices = null; closureTriangles = null; }
        private bool CoversAcceptedCopy(Vector2[] vertices)
        {
            for (int i = 0; i < Accepted.Count; i++)
                if (RegionGeometry.Overlaps(vertices, Accepted[i].CopyBoundary)) return true;
            return false;
        }
        public void CancelStroke() { IsDrawing = false; Stroke.Clear(); ClearClosureCandidate(); }
        public bool FinishStroke()
        {
            if (!IsClosureReady) { IsDrawing = false; return false; }
            Pending.Add(new RegionData(closureVertices, closureTriangles));
            CancelStroke();
            return true;
        }
        public RegionData UndoLast()
        {
            if (IsDrawing || Stroke.Count > 0) { CancelStroke(); return null; }
            List<RegionData> regions = Pending.Count > 0 ? Pending : Accepted;
            if (regions.Count == 0) return null;
            RegionData removed = regions[regions.Count - 1];
            regions.RemoveAt(regions.Count - 1);
            return removed;
        }
        public bool AcceptPending()
        {
            CancelStroke(); LastPlacementAttempts = 0; remainingAcceptanceChecks = MaximumAcceptanceChecks;
            var originalDestinations = new Vector2[Pending.Count];
            var originalScales = new float[Pending.Count];
            var originalPlacements = new bool[Pending.Count];
            for (int i = 0; i < Pending.Count; i++)
            {
                originalDestinations[i] = Pending[i].Destination; originalScales[i] = Pending[i].Scale;
                originalPlacements[i] = Pending[i].HasPlacement;
            }
            for (int i = 0; i < Pending.Count; i++) Pending[i].HasPlacement = false;
            for (int i = 0; i < Pending.Count; i++) if (!FindInitialDestination(Pending[i]) && !FindHeadroomDestination(Pending[i]))
            {
                for (int j = 0; j < Pending.Count; j++)
                {
                    RegionData region = Pending[j];
                    region.Destination = originalDestinations[j]; region.Scale = originalScales[j]; region.HasPlacement = originalPlacements[j];
                    for (int k = 0; k < region.CopyBoundary.Length; k++) region.CopyBoundary[k] = region.HasPlacement ? region.CopyPoint(k) : Vector2.zero;
                }
                return false;
            }
            // Establish every original scale1 fallback before spending remaining work on preferences.
            // A failed headroom probe leaves its established pose and boundary unchanged.
            var headroomPlacements = new HashSet<RegionData>();
            for (int i = 0; i < Pending.Count; i++)
                if (FindHeadroomDestination(Pending[i], headroomPlacements)) headroomPlacements.Add(Pending[i]);
            Accepted.AddRange(Pending); Pending.Clear();
            return true;
        }
        private bool FindInitialDestination(RegionData region)
        {
            // Candidates touch source or canvas bounds; exact polygon checks decide validity.
            // shortcut: boundary candidates and a finite work budget can reject tight concave packing; upgrade to incremental free-space search if needed.
            Vector2 sourceOffset = region.SourceHalfExtent * 2 + Vector2.one * PlacementGap;
            var candidates = new Vector2[MaximumPlacementCandidates];
            for (int side = 0; side < 4; side++)
            {
                Vector2 offset = side < 2 ? new Vector2((side == 0 ? -1 : 1) * sourceOffset.x, 0) : new Vector2(0, (side == 2 ? -1 : 1) * sourceOffset.y);
                candidates[side] = region.SourceCenter + offset;
            }
            Vector2 range = CanvasHalfExtent - region.SourceHalfExtent - Vector2.one * PlacementGap;
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++) candidates[4 + y * 3 + x] = new Vector2((x - 1) * Math.Max(0, range.x), (y - 1) * Math.Max(0, range.y));
            // Center-first is a convenience default, not a prescription for any visual field.
            Array.Sort(candidates, (left, right) => left.sqrMagnitude.CompareTo(right.sqrMagnitude));
            for (int i = 0; i < candidates.Length; i++)
            {
                LastPlacementAttempts++;
                if (TryPlace(region, candidates[i], 1, ref remainingAcceptanceChecks)) return true;
                if (remainingAcceptanceChecks == 0) return false;
            }
            return false;
        }
        private bool FindHeadroomDestination(RegionData region, ISet<RegionData> headroomPlacements = null)
        {
            Vector2 range = CanvasHalfExtent - region.SourceHalfExtent * InitialHeadroomScale - Vector2.one * PlacementGap;
            if (range.x < 0 || range.y < 0) return false;
            var candidates = new Vector2[AdjacentCandidateCount + HeadroomGridSize * HeadroomGridSize];
            Vector2 offset = region.SourceHalfExtent * (1 + InitialHeadroomScale) + Vector2.one * PlacementGap;
            candidates[0] = region.SourceCenter + Vector2.left * offset.x;
            candidates[1] = region.SourceCenter + Vector2.right * offset.x;
            candidates[2] = region.SourceCenter + Vector2.down * offset.y;
            candidates[3] = region.SourceCenter + Vector2.up * offset.y;
            for (int y = 0; y < HeadroomGridSize; y++)
                for (int x = 0; x < HeadroomGridSize; x++)
                    candidates[AdjacentCandidateCount + y * HeadroomGridSize + x] = new Vector2(
                        (2f * x / (HeadroomGridSize - 1) - 1) * range.x,
                        (2f * y / (HeadroomGridSize - 1) - 1) * range.y);
            Array.Sort(candidates, (left, right) => left.sqrMagnitude.CompareTo(right.sqrMagnitude));
            for (int i = 0; i < candidates.Length && remainingAcceptanceChecks > 0; i++)
            {
                LastPlacementAttempts++;
                if (HasHeadroom(region, candidates[i], headroomPlacements) && TryPlace(region, candidates[i], 1, ref remainingAcceptanceChecks)) return true;
            }
            return false;
        }
        private bool HasHeadroom(RegionData region, Vector2 center, ISet<RegionData> headroomPlacements)
        {
            Vector2 half = region.SourceHalfExtent * InitialHeadroomScale;
            if (Math.Abs(center.x) + half.x > CanvasHalfExtent.x || Math.Abs(center.y) + half.y > CanvasHalfExtent.y) return false;
            // The expanded AABB contains every copy over the full 1..1.25 path, including concave copies.
            // Endpoint-only polygon validity cannot guarantee clearance during that enlargement.
            for (int i = 0; i < Accepted.Count; i++)
            {
                RegionData other = Accepted[i];
                if (!BoundsClear(center, half, other.SourceCenter, other.SourceHalfExtent)) return false;
                if (other != region && !BoundsClear(center, half, other.Destination, other.SourceHalfExtent * other.Scale)) return false;
            }
            for (int i = 0; i < Pending.Count; i++)
            {
                RegionData other = Pending[i];
                if (!BoundsClear(center, half, other.SourceCenter, other.SourceHalfExtent)) return false;
                if (other == region || !other.HasPlacement) continue;
                if (!BoundsClear(center, half, other.Destination, other.SourceHalfExtent * other.Scale)) return false;
                // Mixed-size copies can clear another's current boundary but intrude its reserved growth envelope.
                // Preserve earlier preference clearance using this candidate's current scale1 footprint.
                if (headroomPlacements != null && headroomPlacements.Contains(other) &&
                    !BoundsClear(center, region.SourceHalfExtent, other.Destination, other.SourceHalfExtent * InitialHeadroomScale)) return false;
            }
            return true;
        }
        private bool BoundsClear(Vector2 center, Vector2 half, Vector2 otherCenter, Vector2 otherHalf)
        {
            if (remainingAcceptanceChecks <= 0) return false;
            remainingAcceptanceChecks--;
            Vector2 separation = center - otherCenter;
            return Math.Abs(separation.x) > half.x + otherHalf.x + RegionGeometry.Epsilon ||
                Math.Abs(separation.y) > half.y + otherHalf.y + RegionGeometry.Epsilon;
        }
        public bool TryPlace(RegionData region, Vector2 center, float scale)
        {
            int remainingChecks = MaximumAcceptanceChecks;
            return TryPlace(region, center, scale, ref remainingChecks);
        }
        private bool TryPlace(RegionData region, Vector2 center, float scale, ref int remainingChecks)
        {
            if (region == null || !RegionGeometry.IsFinite(center) || !RegionGeometry.IsFinite(scale) || scale < MinimumScale || scale > MaximumScale) return false;
            Vector2 half = region.SourceHalfExtent * scale;
            if (Math.Abs(center.x) + half.x > CanvasHalfExtent.x || Math.Abs(center.y) + half.y > CanvasHalfExtent.y) return false;
            for (int i = 0; i < region.Source.Length; i++) region.PlacementBuffer[i] = (region.Source[i] - region.SourceCenter) * scale + center;
            for (int i = 0; i < Accepted.Count; i++) if (RegionGeometry.Overlaps(region.PlacementBuffer, Accepted[i].Source, ref remainingChecks)) return false;
            for (int i = 0; i < Pending.Count; i++) if (RegionGeometry.Overlaps(region.PlacementBuffer, Pending[i].Source, ref remainingChecks)) return false;
            for (int i = 0; i < Accepted.Count; i++)
                if (Accepted[i] != region && RegionGeometry.Overlaps(region.PlacementBuffer, Accepted[i].CopyBoundary, ref remainingChecks)) return false;
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i] != region && Pending[i].HasPlacement && RegionGeometry.Overlaps(region.PlacementBuffer, Pending[i].CopyBoundary, ref remainingChecks)) return false;
            region.Destination = center; region.Scale = scale; region.HasPlacement = true;
            Array.Copy(region.PlacementBuffer, region.CopyBoundary, region.CopyBoundary.Length);
            return true;
        }
        public static float ClampScale(float current, float delta)
        {
            if (!RegionGeometry.IsFinite(current)) current = 1;
            current = Math.Max(MinimumScale, Math.Min(MaximumScale, current));
            if (!RegionGeometry.IsFinite(delta)) return current;
            return Math.Max(MinimumScale, Math.Min(MaximumScale, current + delta));
        }
        private static bool InsideCanvas(Vector2 point) => Math.Abs(point.x) <= CanvasHalfExtent.x && Math.Abs(point.y) <= CanvasHalfExtent.y;
    }
}
