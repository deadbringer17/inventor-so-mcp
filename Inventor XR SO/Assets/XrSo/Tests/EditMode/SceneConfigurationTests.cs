using System.IO;
using InventorXrSo.Xr;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class SceneConfigurationTests
    {
        [Test]
        public void AndroidBuildHasAnEnabledXrLoader()
        {
            Assert.DoesNotThrow(InventorXrSo.Editor.XrSoProjectSetup.ValidateXr);
        }

        [Test]
        public void MainSceneHasAllAppReferencesAndNoMissingScripts()
        {
            const string path = "Assets/XrSo/Scenes/Main.unity";
            Assert.IsTrue(File.Exists(path), "Generate the main scene before running acceptance tests.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                AppController app = null;
                ControllerUiInputModule input = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        Assert.Zero(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), transform.name);
                    var candidate = root.GetComponentInChildren<AppController>(true);
                    if (candidate != null) { Assert.IsNull(app); app = candidate; }
                    var ui = root.GetComponentInChildren<ControllerUiInputModule>(true);
                    if (ui != null) input = ui;
                }
                Assert.IsNotNull(app);
                Assert.IsNotNull(input, "UI must use the explicit right-controller input module.");
                Assert.AreEqual(OVRInput.Controller.RTouch, ControllerUiInputModule.PointerController);
                Assert.IsNotNull(input.rayTransform);
                Assert.IsFalse(input.GetComponent<UnityEngine.EventSystems.EventSystem>().sendNavigationEvents);
                var serialized = new SerializedObject(app);
                foreach (var field in new[] { "sceneView", "selectionVisuals", "ray", "environment", "head", "qrScanner" })
                    Assert.IsNotNull(serialized.FindProperty(field).objectReferenceValue, field);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
