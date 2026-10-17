using System;
using UnityEngine;

namespace GazeShift
{
    public static class RegionProjection
    {
        public static bool TryUpdateUvs(ICameraFrameSource source, Vector3[] worldPoints, Vector2[] uvs)
        {
            if (uvs == null) return false;
            if (source == null || !source.IsReady || worldPoints == null || worldPoints.Length != uvs.Length)
            { Array.Clear(uvs, 0, uvs.Length); return false; }
            for (int i = 0; i < worldPoints.Length; i++)
                if (!source.TryWorldToTextureUv(worldPoints[i], out uvs[i]) || !RegionGeometry.IsFinite(uvs[i]) || uvs[i].x < 0 || uvs[i].x > 1 || uvs[i].y < 0 || uvs[i].y > 1)
                { Array.Clear(uvs, 0, uvs.Length); return false; }
            return true;
        }
    }
}
