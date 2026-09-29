using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;
using Unity.XR.Oculus;
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
            ConfigureXr();
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

        public static void ConfigureXr()
        {
            if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.settingsKey, out var targets))
            {
                const string path = "Assets/Xr/XRGeneralSettingsPerBuildTarget.asset";
                targets = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
                if (targets == null)
                {
                    Directory.CreateDirectory("Assets/Xr");
                    targets = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(targets, path);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, targets, true);
            }
            if (!targets.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                targets.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var settings = targets.SettingsForBuildTarget(BuildTargetGroup.Android);
            settings.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(settings.Manager, typeof(OculusLoader).FullName, BuildTargetGroup.Android))
                throw new BuildFailedException("Cannot assign the Android Oculus XR loader.");
            var oculus = AssetDatabase.LoadAssetAtPath<OculusSettings>("Assets/Xr/Settings/OculusSettings.asset");
            if (oculus == null) throw new BuildFailedException("Oculus settings asset is missing.");
            oculus.TargetQuest3 = true;
            EditorBuildSettings.AddConfigObject("Unity.XR.Oculus.Settings", oculus, true);
            EditorUtility.SetDirty(oculus);
            EditorUtility.SetDirty(settings.Manager);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(targets);
        }

        public static void ValidateXr()
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings != null && settings.InitManagerOnStart && settings.Manager != null)
                foreach (var loader in settings.Manager.activeLoaders)
                    if (loader is OculusLoader) return;
            throw new BuildFailedException("Android XR loader is missing or disabled. Run Inventor XR SO/Configure Project before building.");
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
            Create("Ray", Shader.Find("Universal Render Pipeline/Unlit"), m => m.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f)));
            UpgradeInspectionMaterials();
        }

        [MenuItem("Inventor XR SO/Upgrade M2 Materials")]
        public static void UpgradeInspectionMaterials()
        {
            var shader = Shader.Find("XrSo/CadSurface");
            if (shader == null) throw new InvalidOperationException("M2 CAD shader is missing.");
            foreach (var name in new[] { "CadBody", "OccurrenceHighlight" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/" + name + ".mat");
                if (material != null && name == "CadBody" && material.HasProperty("_UseVertexColor"))
                {
                    material.SetFloat("_UseVertexColor", 1f);   // body colour comes from the GLB COLOR_0 (default grey when absent)
                    EditorUtility.SetDirty(material);
                }
                if (material == null || material.shader == shader) continue;
                var color = material.GetColor("_BaseColor");
                material.shader = shader;
                material.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }

        public static void UpgradeInspectionMaterialsBatch()
        {
            try { UpgradeInspectionMaterials(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
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
