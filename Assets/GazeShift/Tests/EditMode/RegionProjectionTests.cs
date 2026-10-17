using NUnit.Framework;
using UnityEngine;

namespace GazeShift.Tests
{
    public sealed class RegionProjectionTests
    {
        [Test] public void AnyUnprojectableSourceVertexClearsWholeRegionUntilCoveredAgain()
        {
            var source = new ProjectionSource();
            Vector3[] points = { new Vector3(0.1f, 0.2f, 1), new Vector3(0.3f, 0.4f, 1), new Vector3(0.5f, 0.6f, 1) };
            var uvs = new Vector2[points.Length];
            Assert.IsTrue(RegionProjection.TryUpdateUvs(source, points, uvs));
            Assert.AreEqual(new Vector2(0.5f, 0.6f), uvs[2]);
            source.RejectX = 0.5f;
            Assert.IsFalse(RegionProjection.TryUpdateUvs(source, points, uvs));
            CollectionAssert.AreEqual(new Vector2[3], uvs);
            source.RejectX = 2;
            source.Offset = new Vector2(0.1f, 0.1f);
            Assert.IsTrue(RegionProjection.TryUpdateUvs(source, points, uvs));
            Assert.AreEqual(0.6f, uvs[2].x, 0.00001f);
            Assert.AreEqual(0.7f, uvs[2].y, 0.00001f);
        }
        [Test] public void LostReadinessRemovesPreviousUvBuffer()
        {
            var source = new ProjectionSource();
            Vector3[] points = { new Vector3(0.1f, 0.2f, 1) }; var uvs = new Vector2[1];
            Assert.IsTrue(RegionProjection.TryUpdateUvs(source, points, uvs));
            source.Ready = false;
            Assert.IsFalse(RegionProjection.TryUpdateUvs(source, points, uvs));
            Assert.AreEqual(Vector2.zero, uvs[0]);
        }
        private sealed class ProjectionSource : ICameraFrameSource
        {
            public bool Ready = true; public float RejectX = 2; public Vector2 Offset;
            public bool IsReady => Ready;
            public Texture FrameTexture => null;
            public bool TryWorldToTextureUv(Vector3 point, out Vector2 uv)
            {
                uv = new Vector2(point.x, point.y) + Offset;
                return point.x < RejectX;
            }
        }
    }
}
