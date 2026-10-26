using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEditor.XR.OpenXR.Features;

namespace InventorXrSo.Editor
{
    /// <summary>Desktop simulator profile. Android retains its independently configured Oculus loader.</summary>
    public static class XrSoSimulator
    {
        [InitializeOnLoadMethod]
        private static void DisableAutomaticXrForBatchTests()
        {
            // Lifecycle tests briefly enter Play Mode without rendering an XR session.
            // Apply in memory on every domain reload; never save this override to the asset.
            if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-runTests")) return;
            if (EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.settingsKey, out var targets))
            {
                var desktop = targets.SettingsForBuildTarget(BuildTargetGroup.Standalone);
                if (desktop != null) desktop.InitManagerOnStart = false;
            }
        }

        [MenuItem("Inventor XR SO/Simulator/Configure Desktop OpenXR")]
        public static void Configure()
        {
            if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.settingsKey, out var targets))
                throw new InvalidOperationException("Configure the XR project first.");
            if (!targets.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                targets.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var settings = targets.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            foreach (var loader in settings.Manager.activeLoaders.ToArray())
                if (loader.GetType().FullName != "UnityEngine.XR.OpenXR.OpenXRLoader")
                    XRPackageMetadataStore.RemoveLoader(settings.Manager, loader.GetType().FullName, BuildTargetGroup.Standalone);
            if (!XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone))
                throw new InvalidOperationException("Unity OpenXR package is unavailable.");
            settings.InitManagerOnStart = true;
            var packageType = Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor", true);
            packageType.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var features = openxr.GetFeatures<OpenXRFeature>();
            int controllers = 0;
            foreach (var feature in features)
            {
                var name = feature.GetType().Name;
                if (name != "OculusTouchControllerProfile" && name != "MetaQuestTouchPlusControllerProfile") continue;
                feature.enabled = true;
                EditorUtility.SetDirty(feature); controllers++;
            }
            if (controllers == 0) throw new InvalidOperationException("OpenXR Touch interaction profiles were not generated.");
            EditorUtility.SetDirty((UnityEngine.Object)openxr);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(settings.Manager); EditorUtility.SetDirty(targets);
            AssetDatabase.SaveAssets();
            XrSoProjectSetup.ValidateXr(); // Android must still have its original Oculus loader.
            Debug.Log("[XrSimulator] Desktop OpenXR configured; Android Oculus retained.");
        }

        public static void ConfigureBatch()
        {
            try { Configure(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void RunSmokeBatch()
        {
            try
            {
                Configure();
                if (!File.Exists(Environment.GetEnvironmentVariable("XR_RUNTIME_JSON")))
                    throw new InvalidOperationException("Set XR_RUNTIME_JSON to Meta XR Simulator's runtime manifest for this process.");
                string output = Path.GetFullPath("../artifacts/meta-xr-simulator");
                var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-xrSimOutput");
                if (index >= 0 && index + 1 < args.Length) output = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(output);
                string marker = Path.Combine(output, "pass.marker");
                if (File.Exists(marker)) File.Delete(marker);
                SessionState.SetString(XrSoSimulatorSmoke.OutputKey, output);
                SessionState.SetBool(XrSoSimulatorSmoke.ActiveKey, true);
                SessionState.SetString(XrSoSimulatorSmoke.ErrorsKey, "[]");
                EditorSceneManager.OpenScene(XrSoSceneBuilder.ScenePath, OpenSceneMode.Single);
                var gameView = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView, UnityEditor", true));
                gameView.Show(); gameView.Focus();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
