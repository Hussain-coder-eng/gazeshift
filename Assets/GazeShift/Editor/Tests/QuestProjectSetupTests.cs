using Meta.XR;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace GazeShift.Editor.Tests
{
    public sealed class QuestProjectSetupTests
    {
        [Test]
        public void ConfigureCreatesCompleteAndroidConfigurationAndIsRepeatable()
        {
            QuestProjectSetup.Configure();
            string sceneGuid = AssetDatabase.AssetPathToGUID("Assets/GazeShift/Scenes/GazeShift.unity");
            QuestProjectSetup.Configure();

            Assert.IsFalse(PlayerSettings.SplashScreen.show);
            Assert.IsNotEmpty(sceneGuid);
            Assert.AreEqual(sceneGuid, AssetDatabase.AssetPathToGUID("Assets/GazeShift/Scenes/GazeShift.unity"));
            Assert.AreEqual(1, EditorBuildSettings.scenes.Length);
            Assert.AreEqual("Assets/GazeShift/Scenes/GazeShift.unity", EditorBuildSettings.scenes[0].path);
            Assert.IsTrue(EditorBuildSettings.scenes[0].enabled);
            Assert.AreEqual(ScriptingImplementation.IL2CPP, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            Assert.AreEqual(AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures);
            Assert.IsTrue(EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget settings));
            XRGeneralSettings android = settings.SettingsForBuildTarget(BuildTargetGroup.Android);
            Assert.IsTrue(android.InitManagerOnStart);
            Assert.AreEqual(1, android.Manager.activeLoaders.Count);
            Assert.IsInstanceOf<OpenXRLoader>(android.Manager.activeLoaders[0]);
            OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.IsNotNull(openXr);
            Assert.IsTrue(openXr.GetFeature<MetaQuestFeature>().enabled);
            Assert.IsTrue(openXr.GetFeature<MetaXRFeature>().enabled);
            Assert.IsTrue(openXr.GetFeature<OculusTouchControllerProfile>().enabled);
            Assert.IsTrue(OVRProjectConfig.CachedProjectConfig.isPassthroughCameraAccessEnabled);
            Assert.AreEqual(OVRProjectConfig.FeatureSupport.Required,
                OVRProjectConfig.CachedProjectConfig.insightPassthroughSupport);
            foreach (string name in new[] { "GazeShift/LiveRegion", "GazeShift/OverlayColor" })
            {
                Shader shader = Shader.Find(name);
                Assert.IsNotNull(shader);
                var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
                SerializedProperty includedShaders = graphics.FindProperty("m_AlwaysIncludedShaders");
                bool included = false;
                for (int i = 0; i < includedShaders.arraySize; i++)
                    included |= includedShaders.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                Assert.IsTrue(included, name + " must survive player shader stripping.");
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                    Assert.AreNotEqual(ShaderCompilerMessageSeverity.Error, message.severity, message.message);
            }
        }
    }
}
