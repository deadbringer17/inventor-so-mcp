using System;
using System.IO;
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

        private static void Build(bool acceptance, bool profile = false, string outputPath = ApkPath)
        {
            XrSoProjectSetup.ValidateXr();
            if (Resources.LoadAll<Sprite>("InventorIcons").Length != 34)
                throw new BuildFailedException("M10 icon pack incomplete: run scripts/prepare-m10-icons.ps1 and import all 34 Sprites.");
            var theme = InventorXrSo.Unity.Ui.UiThemeAssets.Current;
            if (theme == null || theme.Medium == null || theme.Bold == null || theme.RoundedPanel == null || theme.FontLicense == null || theme.FallbackFontLicense == null)
                throw new BuildFailedException("M8 theme assets/license missing: run XrSoThemeAssets.GenerateBatch before building.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { XrSoSceneBuilder.ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = profile ? BuildOptions.Development : BuildOptions.None,
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

        public static void BuildSimulatorApksBatch()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-simulatorApkOutput");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Pass a new -simulatorApkOutput directory.");
                string output = Path.GetFullPath(args[index + 1]);
                if (Directory.Exists(output)) throw new IOException("APK evidence directory already exists.");
                Directory.CreateDirectory(output);
                Build(true, true, Path.Combine(output, "InventorXrSo-simulator-acceptance.apk"));
                Build(false, false, Path.Combine(output, "InventorXrSo-simulator-ordinary.apk"));
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void BuildM8ApksBatch()
        {
            try
            {
                var output = Path.GetFullPath("../artifacts/m8-verification");
                Directory.CreateDirectory(output);
                Build(true);
                File.Copy(ApkPath, Path.Combine(output, "InventorXrSo-m8-acceptance.apk"), true);
                Build(false);
                File.Copy(ApkPath, Path.Combine(output, "InventorXrSo-m8-ordinary.apk"), true);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void BuildM10ApksBatch()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-m10Output");
                var output = Path.GetFullPath(index >= 0 && index + 1 < args.Length ? args[index + 1] : "../artifacts/m10-verification");
                Directory.CreateDirectory(output);
                Build(true, true);
                File.Copy(ApkPath, Path.Combine(output, "InventorXrSo-m10-acceptance.apk"), true);
                Build(false);
                File.Copy(ApkPath, Path.Combine(output, "InventorXrSo-m10-ordinary.apk"), true);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void BuildS1ApksBatch()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-s1Output");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Explicit -s1Output directory required.");
                var output = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(output);
                var qa = Path.Combine(output, "InventorXrSo-s1-acceptance.apk");
                var ordinary = Path.Combine(output, "InventorXrSo-s1-ordinary.apk");
                if (File.Exists(qa) || File.Exists(ordinary)) throw new IOException("S1 output APK already exists; use a new evidence directory.");
                Build(true);
                File.Copy(ApkPath, qa);
                Build(false);
                File.Copy(ApkPath, ordinary);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        // The isolated QA wrapper streams its one-shot bootstrap through run-as;
        // Android therefore requires a Development QA APK. Each result is built
        // directly into a new explicit evidence directory.
        public static void BuildS1OwnedApksBatch()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-s1Output");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Explicit -s1Output directory required.");
                var output = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(output);
                var qa = Path.Combine(output, "InventorXrSo-s1-acceptance.apk");
                var ordinary = Path.Combine(output, "InventorXrSo-s1-ordinary.apk");
                if (File.Exists(qa) || File.Exists(ordinary)) throw new IOException("S1 output APK already exists; use a new evidence directory.");
                Build(true, true, qa);
                Build(false, false, ordinary);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void BuildS1OwnedAcceptanceApkBatch()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-s1Output");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Explicit -s1Output directory required.");
                var output = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(output);
                var qa = Path.Combine(output, "InventorXrSo-s1-acceptance.apk");
                if (File.Exists(qa)) throw new IOException("S1 output APK already exists; use a new evidence directory.");
                Build(true, true, qa);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
