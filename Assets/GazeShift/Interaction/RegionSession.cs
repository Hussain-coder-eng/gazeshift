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
            SourceCenter = (min + max) * 0.5f;
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
        // Retained from the phone prototype; headset comfort still requires device validation.
        public const int MinimumSamples = 20;
        public const int MaximumSamples = 512;
        public const float ClosureDiagonalFraction = 0.03f;
        public const float MinimumScale = 0.25f;
        public const float MaximumScale = 2;
        public const float SampleSpacing = 0.001f;
        private const float PlacementStep = 0.05f;
        public static readonly Vector2 CanvasHalfExtent = new Vector2(0.65f, 0.4f);
        public bool IsDrawing { get; private set; }
        public readonly List<Vector2> Stroke = new List<Vector2>();
        public readonly List<RegionData> Pending = new List<RegionData>();
        public readonly List<RegionData> Accepted = new List<RegionData>();

        public void BeginTrigger(bool hitAccept)
        {
            CancelStroke();
            if (!hitAccept) IsDrawing = true;
        }
        public void AddPoint(Vector2 point)
        {
            if (!IsDrawing || !RegionGeometry.IsFinite(point) || !InsideCanvas(point)) return;
            if (Stroke.Count == MaximumSamples) { CancelStroke(); return; }
            if (Stroke.Count == 0 || (point - Stroke[Stroke.Count - 1]).sqrMagnitude >= SampleSpacing * SampleSpacing) Stroke.Add(point);
        }
        public void CancelStroke() { IsDrawing = false; Stroke.Clear(); }
        public bool FinishStroke()
        {
            bool valid = false;
            if (IsDrawing && Stroke.Count >= MinimumSamples && (Stroke[0] - Stroke[Stroke.Count - 1]).magnitude <= (CanvasHalfExtent * 2).magnitude * ClosureDiagonalFraction &&
                RegionGeometry.TryTriangulate(Stroke, out Vector2[] vertices, out int[] indices) && vertices.Length >= MinimumSamples)
            {
                bool coversCopy = false;
                for (int i = 0; i < Accepted.Count; i++)
                    if (RegionGeometry.Overlaps(vertices, Accepted[i].CopyBoundary)) { coversCopy = true; break; }
                if (!coversCopy) { Pending.Add(new RegionData(vertices, indices)); valid = true; }
            }
            CancelStroke();
            return valid;
        }
        public void UndoPending()
        {
            CancelStroke();
            if (Pending.Count > 0) Pending.RemoveAt(Pending.Count - 1);
        }
        public bool AcceptPending()
        {
            CancelStroke();
            for (int i = 0; i < Pending.Count; i++) Pending[i].HasPlacement = false;
            for (int i = 0; i < Pending.Count; i++) if (!FindInitialDestination(Pending[i]))
            {
                for (int j = 0; j < Pending.Count; j++) Pending[j].HasPlacement = false;
                return false;
            }
            Accepted.AddRange(Pending); Pending.Clear();
            return true;
        }
        private bool FindInitialDestination(RegionData region)
        {
            for (float scale = 1; scale >= MinimumScale; scale -= MinimumScale)
                for (float y = CanvasHalfExtent.y - PlacementStep; y > -CanvasHalfExtent.y; y -= PlacementStep)
                    for (float x = -CanvasHalfExtent.x + PlacementStep; x < CanvasHalfExtent.x; x += PlacementStep)
                        if (TryPlace(region, new Vector2(x, y), scale)) return true;
            return false;
        }
        public bool TryPlace(RegionData region, Vector2 center, float scale)
        {
            if (region == null || !RegionGeometry.IsFinite(center) || !RegionGeometry.IsFinite(scale) || scale < MinimumScale || scale > MaximumScale) return false;
            for (int i = 0; i < region.Source.Length; i++)
            {
                Vector2 point = (region.Source[i] - region.SourceCenter) * scale + center;
                if (!InsideCanvas(point)) return false;
                region.PlacementBuffer[i] = point;
            }
            for (int i = 0; i < Accepted.Count; i++) if (RegionGeometry.Overlaps(region.PlacementBuffer, Accepted[i].Source)) return false;
            for (int i = 0; i < Pending.Count; i++) if (RegionGeometry.Overlaps(region.PlacementBuffer, Pending[i].Source)) return false;
            for (int i = 0; i < Accepted.Count; i++)
                if (Accepted[i] != region && RegionGeometry.Overlaps(region.PlacementBuffer, Accepted[i].CopyBoundary)) return false;
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i] != region && Pending[i].HasPlacement && RegionGeometry.Overlaps(region.PlacementBuffer, Pending[i].CopyBoundary)) return false;
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
