using UnityEngine;

namespace GazeShift
{
    internal sealed class LiveRegionView
    {
        private const float CopyDepth = -0.002f;
        private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        private static readonly int Blur = Shader.PropertyToID("_Blur");
        private readonly RegionData region;
        private readonly Transform plane;
        private readonly GameObject sourceObject;
        private readonly GameObject copyObject;
        private readonly Mesh sourceMesh;
        private readonly Mesh copyMesh;
        private readonly MeshRenderer sourceRenderer;
        private readonly MeshRenderer copyRenderer;
        private readonly Vector3[] worldSource;
        private readonly Vector3[] copyVertices;
        private readonly Vector2[] uvs;
        private readonly MaterialPropertyBlock sourceProperties = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock copyProperties = new MaterialPropertyBlock();

        public LiveRegionView(RegionData data, Transform parent, Material material)
        {
            region = data; plane = parent;
            worldSource = new Vector3[data.Source.Length];
            copyVertices = new Vector3[data.Source.Length];
            uvs = new Vector2[data.Source.Length];
            var localSource = new Vector3[data.Source.Length];
            for (int i = 0; i < localSource.Length; i++) localSource[i] = new Vector3(data.Source[i].x, data.Source[i].y, 0);
            sourceMesh = CreateMesh(localSource, data.Triangles);
            copyMesh = CreateMesh(localSource, data.Triangles);
            sourceObject = CreateSurface("Source blur", sourceMesh, material, out sourceRenderer);
            copyObject = CreateSurface("Live sharp copy", copyMesh, material, out copyRenderer);
            sourceProperties.SetFloat(Blur, 1); copyProperties.SetFloat(Blur, 0);
            SetVisible(false);
        }

        public bool IsVisible => sourceRenderer.enabled && copyRenderer.enabled;

        public bool UpdateFrame(ICameraFrameSource source)
        {
            for (int i = 0; i < worldSource.Length; i++) worldSource[i] = plane.TransformPoint(new Vector3(region.Source[i].x, region.Source[i].y, 0));
            bool covered = source != null && source.IsReady && source.FrameTexture != null && RegionProjection.TryUpdateUvs(source, worldSource, uvs);
            SetVisible(covered);
            if (!covered) return false;
            for (int i = 0; i < copyVertices.Length; i++)
            {
                Vector2 point = region.CopyPoint(i);
                copyVertices[i] = new Vector3(point.x, point.y, CopyDepth);
            }
            copyMesh.vertices = copyVertices;
            copyMesh.RecalculateBounds();
            sourceMesh.uv = uvs; copyMesh.uv = uvs;
            sourceProperties.SetTexture(MainTexture, source.FrameTexture);
            copyProperties.SetTexture(MainTexture, source.FrameTexture);
            sourceRenderer.SetPropertyBlock(sourceProperties); copyRenderer.SetPropertyBlock(copyProperties);
            return true;
        }

        public void SetVisible(bool visible)
        {
            sourceRenderer.enabled = visible; copyRenderer.enabled = visible;
            if (visible) return;
            sourceProperties.Clear(); copyProperties.Clear();
            sourceProperties.SetFloat(Blur, 1); copyProperties.SetFloat(Blur, 0);
            sourceRenderer.SetPropertyBlock(sourceProperties); copyRenderer.SetPropertyBlock(copyProperties);
        }
        public void Dispose()
        {
            DestroyOwned(sourceObject); DestroyOwned(copyObject);
            DestroyOwned(sourceMesh); DestroyOwned(copyMesh);
        }
        private static void DestroyOwned(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Object.Destroy(resource); else Object.DestroyImmediate(resource);
        }
        private static Mesh CreateMesh(Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = "GazeShift polygon" };
            mesh.MarkDynamic(); mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateBounds();
            return mesh;
        }
        private GameObject CreateSurface(string name, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            var surface = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            surface.transform.SetParent(plane, false);
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer = surface.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return surface;
        }
    }
}
