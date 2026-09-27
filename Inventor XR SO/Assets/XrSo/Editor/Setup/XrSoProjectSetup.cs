using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InventorXrSo.Editor
{
    /// <summary>Quest 3 player settings, URP and the shared materials. Safe to run again.</summary>
    public static class XrSoProjectSetup
    {
        public const string MaterialsFolder = "Assets/XrSo/Materials";
        private const string SettingsFolder = "Assets/XrSo/Settings";

        [MenuItem("Inventor XR SO/Configure Project")]
        public static void Configure()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "Occhipinti";
            PlayerSettings.productName = "Inventor XR SO";
            PlayerSettings.SetApplicationIdentifier(android, "com.occhipinti.inventorxrso");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            ConfigureUrp();
            CreateMaterials();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Batch entry: -executeMethod InventorXrSo.Editor.XrSoProjectSetup.ConfigureBatch</summary>
        public static void ConfigureBatch()
        {
            try
            {
                Configure();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigureUrp()
        {
            var path = SettingsFolder + "/XrSoUrp.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, SettingsFolder + "/XrSoUrpRenderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.msaaSampleCount = 4;
                AssetDatabase.CreateAsset(asset, path);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>Creates the materials whose shader is available (the face overlay shader arrives in Task C4).</summary>
        public static void CreateMaterials()
        {
            Directory.CreateDirectory(MaterialsFolder);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Create("CadBody", lit, m => m.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.77f)));
            Create("OccurrenceHighlight", lit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.25f, 0.55f, 1f));
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.05f, 0.15f, 0.35f));
            });
            Create("FaceHighlight", Shader.Find("XrSo/HighlightOverlay"), m => m.SetColor("_Color", new Color(1f, 0.6f, 0.1f, 0.6f)));
        }

        private static void Create(string name, Shader shader, Action<Material> setup)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            if (shader == null || AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var material = new Material(shader) { name = name };
            setup(material);
            AssetDatabase.CreateAsset(material, path);
        }
    }
}
