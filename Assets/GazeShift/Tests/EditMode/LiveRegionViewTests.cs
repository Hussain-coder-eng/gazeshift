using NUnit.Framework;
using UnityEngine;

namespace GazeShift.Tests
{
    public sealed class LiveRegionViewTests
    {
        [Test] public void FailedCoverageHidesBothMeshesAndClearsTheirTextureBindings()
        {
            var plane = new GameObject("Test plane");
            var texture = new Texture2D(2, 2);
            var material = new Material(Shader.Find("GazeShift/LiveRegion"));
            var source = new LiveSource(texture);
            var region = new RegionData(new[] { new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0, 0.1f) }, new[] { 0, 1, 2 });
            var view = new LiveRegionView(region, plane.transform, material);
            try
            {
                Assert.IsTrue(view.UpdateFrame(source));
                MeshRenderer[] renderers = plane.GetComponentsInChildren<MeshRenderer>();
                Assert.AreEqual(2, renderers.Length);
                foreach (MeshRenderer renderer in renderers) Assert.IsTrue(renderer.enabled);
                source.RejectPositiveX = true;
                Assert.IsFalse(view.UpdateFrame(source));
                var properties = new MaterialPropertyBlock();
                foreach (MeshRenderer renderer in renderers)
                {
                    Assert.IsFalse(renderer.enabled);
                    renderer.GetPropertyBlock(properties);
                    Assert.IsNull(properties.GetTexture("_MainTex"));
                }
                source.RejectPositiveX = false;
                Assert.IsTrue(view.UpdateFrame(source));
                foreach (MeshRenderer renderer in renderers) Assert.IsTrue(renderer.enabled);
                source.Ready = false;
                Assert.IsFalse(view.UpdateFrame(source));
                foreach (MeshRenderer renderer in renderers) Assert.IsFalse(renderer.enabled);
            }
            finally
            {
                foreach (MeshFilter surface in plane.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(surface.sharedMesh);
                Object.DestroyImmediate(plane); Object.DestroyImmediate(texture); Object.DestroyImmediate(material);
            }
        }

        [Test] public void MovingCopyRetainsOriginalUvsWhileHeadMotionReprojectsThem()
        {
            var plane = new GameObject("Test plane");
            var texture = new Texture2D(2, 2);
            var material = new Material(Shader.Find("GazeShift/LiveRegion"));
            var source = new LiveSource(texture);
            var region = new RegionData(new[] { new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0, 0.1f) }, new[] { 0, 1, 2 });
            var view = new LiveRegionView(region, plane.transform, material);
            try
            {
                Assert.IsTrue(new RegionSession().TryPlace(region, new Vector2(0.4f, 0), 0.5f));
                Assert.IsTrue(view.UpdateFrame(source));
                MeshFilter[] surfaces = plane.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter surface in surfaces) Assert.AreEqual(0.5f, surface.sharedMesh.uv[0].x, 0.00001f);
                plane.transform.position = new Vector3(0.5f, 0, 1);
                Assert.IsTrue(view.UpdateFrame(source));
                foreach (MeshFilter surface in surfaces) Assert.AreEqual(0.55f, surface.sharedMesh.uv[0].x, 0.00001f);
                Assert.AreEqual(0.375f, surfaces[1].sharedMesh.vertices[0].x, 0.00001f);
                Assert.AreEqual(Vector2.zero, region.Source[0]);
            }
            finally
            {
                foreach (MeshFilter surface in plane.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(surface.sharedMesh);
                Object.DestroyImmediate(plane); Object.DestroyImmediate(texture); Object.DestroyImmediate(material);
            }
        }

        private sealed class LiveSource : ICameraFrameSource
        {
            private readonly Texture texture;
            public bool Ready = true;
            public bool RejectPositiveX;
            public LiveSource(Texture frame) => texture = frame;
            public bool IsReady => Ready;
            public Texture FrameTexture => Ready ? texture : null;
            public bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv)
            {
                uv = new Vector2(0.5f + worldPoint.x * 0.1f, 0.5f + worldPoint.y * 0.1f);
                return !RejectPositiveX || worldPoint.x <= 0;
            }
        }
    }
}
