using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InventorXrSo.Editor
{
    public static class XrSoBuild
    {
        public const string ApkPath = "Builds/InventorXrSo.apk";

        [MenuItem("Inventor XR SO/Build Quest APK")]
        public static void BuildApk()
            => Build(false);

        private static void Build(bool acceptance)
        {
            XrSoProjectSetup.ValidateXr();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { XrSoSceneBuilder.ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
                extraScriptingDefines = acceptance ? new[] { "XR_SO_ACCEPTANCE" } : Array.Empty<string>(),
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("APK build " + report.summary.result + " with " + report.summary.totalErrors + " error(s).");
        }

        // Opt-in fixture-only device harness; excluded from the ordinary APK.
        public static void BuildAcceptanceApkBatch()
        {
            try { Build(true); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void BuildApkBatch()
        {
            try
            {
                BuildApk();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
