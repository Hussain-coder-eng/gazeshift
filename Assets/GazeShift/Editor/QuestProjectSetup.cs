using System;
using System.IO;
using Meta.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace GazeShift.Editor
{
    public static class QuestProjectSetup
    {
        public const string ScenePath = "Assets/GazeShift/Scenes/GazeShift.unity";
        private const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string DefaultApkPath = "Builds/GazeShift.apk";
        private const int InputSystemOnly = 1;

        [MenuItem("GazeShift/Configure Quest project")]
        public static void Configure()
        {
            PlayerSettings.companyName = "GazeShift";
            PlayerSettings.productName = "GazeShift";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.gazeshift.quest");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            playerSettings.FindProperty("activeInputHandler").intValue = InputSystemOnly;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();

            ConfigureXr();
            var config = OVRProjectConfig.CachedProjectConfig;
            config.targetDeviceTypes.Clear();
            config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3);
            config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3S);
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersOnly;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            config.isPassthroughCameraAccessEnabled = true;
            config.minHorizonOsSdkVersion = 81;
            OVRProjectConfig.CommitProjectConfig(config);
            IncludeShader("GazeShift/LiveRegion");
            IncludeShader("GazeShift/OverlayColor");

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Quest configuration canceled to preserve the current scene.");
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("GazeShift").AddComponent<GazeShiftBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("GazeShift Quest configuration and scene saved.");
        }

        private static void ConfigureXr()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(XrSettingsPath));
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, XrSettingsPath);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = settings.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager,
                "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not enable the Android OpenXR loader.");
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            openXr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EnableFeature<MetaQuestFeature>(openXr);
            EnableFeature<MetaXRFeature>(openXr);
            EnableFeature<OculusTouchControllerProfile>(openXr);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            EditorUtility.SetDirty(openXr);
        }

        private static void EnableFeature<T>(OpenXRSettings settings)
            where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            T feature = settings.GetFeature<T>();
            if (feature == null) throw new InvalidOperationException("OpenXR feature missing: " + typeof(T).Name);
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static void IncludeShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) throw new InvalidOperationException("Required shader missing: " + name);
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            SerializedProperty shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < shaders.arraySize; i++)
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            int index = shaders.arraySize;
            shaders.InsertArrayElementAtIndex(index);
            shaders.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("GazeShift/Build Quest APK")]
        public static void BuildAndroid()
        {
            Configure();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support, SDK, NDK, and OpenJDK for Unity 6000.3.19f1.");
            string output = Environment.GetEnvironmentVariable("GAZESHIFT_APK_PATH") ?? DefaultApkPath;
            if (string.IsNullOrWhiteSpace(output) || Path.GetExtension(output) != ".apk")
                throw new BuildFailedException("GAZESHIFT_APK_PATH must name an .apk file.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.Android, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Quest build failed: " + report.summary.result);
            Debug.Log("Quest APK built: " + Path.GetFullPath(output));
        }
    }
}
