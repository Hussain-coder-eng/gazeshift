using UnityEngine;

namespace GazeShift
{
    public interface ICameraFrameSource
    {
        bool IsReady { get; }
        Texture FrameTexture { get; }
        bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv);
    }
}
