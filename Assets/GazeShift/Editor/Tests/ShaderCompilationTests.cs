using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace GazeShift.Editor.Tests
{
    public sealed class ShaderCompilationTests
    {
        [TestCase("GazeShift/LiveRegion")]
        [TestCase("GazeShift/OverlayColor")]
        public void AppShaderPassCompiles(string name)
        {
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader);
            var material = new Material(shader);
            try
            {
                ShaderUtil.CompilePass(material, 0, true);
                Assert.IsTrue(ShaderUtil.IsPassCompiled(material, 0), name);
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                    Assert.AreNotEqual(ShaderCompilerMessageSeverity.Error, message.severity, message.message);
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
