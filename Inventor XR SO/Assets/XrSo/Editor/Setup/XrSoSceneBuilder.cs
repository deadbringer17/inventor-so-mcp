using System;
using System.IO;
using System.Linq;
using System.Xml;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Editor
{
    /// <summary>Builds Assets/XrSo/Scenes/Main.unity from code, so the scene is reproducible.</summary>
    public static class XrSoSceneBuilder
    {
        public const string ScenePath = "Assets/XrSo/Scenes/Main.unity";

        [MenuItem("Inventor XR SO/Build Main Scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            XrSoProjectSetup.CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigPath = AssetDatabase.FindAssets("OVRCameraRig t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/OVRCameraRig.prefab"));
            if (rigPath == null) throw new InvalidOperationException("OVRCameraRig.prefab not found: is com.meta.xr.sdk.core installed?");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(rigPath));
            if (rig.GetComponent<OVRManager>() == null) rig.AddComponent<OVRManager>();
            rig.GetComponent<OVRManager>().isInsightPassthroughEnabled = true;
            var config = OVRProjectConfig.CachedProjectConfig;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.isPassthroughCameraAccessEnabled = true;
            OVRProjectConfig.CommitProjectConfig(config);
            var passthrough = rig.AddComponent<OVRPassthroughLayer>();
            // Meta XR 207 creates its internal passthrough overlay as an Underlay.
            var centerEye = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var right = rig.transform.Find("TrackingSpace/RightHandAnchor/RightControllerAnchor") ?? rig.transform.Find("TrackingSpace/RightHandAnchor");

            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cad = new GameObject("CadModel");
            var view = cad.AddComponent<CadSceneView>();
            view.BodyMaterial = Material("CadBody");
            var visuals = cad.AddComponent<SelectionVisuals>();
            visuals.Configure(view, Material("OccurrenceHighlight"), Material("FaceHighlight"));

            var rayObject = new GameObject("ControllerRay");
            rayObject.transform.SetParent(right, false);
            var line = rayObject.AddComponent<LineRenderer>();
            line.widthMultiplier = 0.003f;
            line.useWorldSpace = true;
            line.sharedMaterial = Material("Ray");
            var ray = rayObject.AddComponent<ControllerRay>();
            ray.Configure(right, line);

            var events = new GameObject("EventSystem", typeof(EventSystem));
            events.GetComponent<EventSystem>().sendNavigationEvents = false;
            var input = events.AddComponent<ControllerUiInputModule>();
            input.rayTransform = right;
            input.joyPadClickButton = OVRInput.Button.PrimaryIndexTrigger;

            var environment = rig.AddComponent<EnvironmentModeController>();
            environment.Configure(centerEye.GetComponent<Camera>(), passthrough);
            centerEye.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            centerEye.GetComponent<Camera>().backgroundColor = Color.clear;
            var scanner = new GameObject("QrScanner").AddComponent<QrScanner>();
            new GameObject("App").AddComponent<AppController>().Configure(view, visuals, ray, environment, centerEye, scanner);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
            const string manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
            const string androidNs = "http://schemas.android.com/apk/res/android";
            var manifest = new XmlDocument();
            manifest.Load(manifestPath);
            var namespaces = new XmlNamespaceManager(manifest.NameTable);
            namespaces.AddNamespace("android", androidNs);
            if (manifest.SelectSingleNode("/manifest/uses-permission[@android:name='android.permission.CAMERA']", namespaces) == null)
            {
                var permission = manifest.CreateElement("uses-permission");
                permission.SetAttribute("name", androidNs, "android.permission.CAMERA");
                manifest.DocumentElement.AppendChild(permission);
            }
            manifest.Save(manifestPath);
            AssetDatabase.Refresh();
        }

        public static void BuildBatch()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(XrSoProjectSetup.MaterialsFolder + "/" + name + ".mat")
            ?? throw new InvalidOperationException("Missing material " + name + ": run Inventor XR SO/Configure Project.");
    }
}
