using NUnit.Framework;
using UnityEngine;

namespace GazeShift.Tests
{
    public sealed class RegionInteractionUiTests
    {
        [Test] public void InstructionAndAcceptTextRenderAfterButtonWithoutChangingSharedFont()
        {
            var owner = new GameObject("Interaction UI test");
            var eye = new GameObject("Test eye");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Material sharedFont = font.material;
            int originalQueue = sharedFont.renderQueue;
            Material ownedFont = null;
            try
            {
                owner.AddComponent<RegionInteraction>().Initialize(new UnavailableSource(), eye.transform);
                TextMesh[] labels = eye.GetComponentsInChildren<TextMesh>();
                Assert.AreEqual(2, labels.Length);
                MeshRenderer button = null;
                foreach (MeshRenderer renderer in eye.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.gameObject.name == "Accept regions") button = renderer;
                Assert.IsNotNull(button);
                foreach (TextMesh label in labels)
                {
                    Material textMaterial = label.GetComponent<MeshRenderer>().sharedMaterial;
                    Assert.AreNotSame(sharedFont, textMaterial);
                    Assert.Greater(textMaterial.renderQueue, button.sharedMaterial.renderQueue);
                    Assert.AreSame(sharedFont.mainTexture, textMaterial.mainTexture);
                    if (ownedFont == null) ownedFont = textMaterial;
                    else Assert.AreSame(ownedFont, textMaterial);
                }
                Assert.AreEqual(originalQueue, sharedFont.renderQueue);
                Object.DestroyImmediate(owner);
                Assert.IsTrue(ownedFont == null);
                Assert.IsNotNull(sharedFont);
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner);
                Object.DestroyImmediate(eye);
            }
        }
        private sealed class UnavailableSource : ICameraFrameSource
        {
            public bool IsReady => false;
            public Texture FrameTexture => null;
            public bool TryWorldToTextureUv(Vector3 worldPoint, out Vector2 uv) { uv = default; return false; }
        }
    }
}
