using InventorXrSo.Core.Selection;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class SelectionVisualsTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private SelectionVisuals _visuals;
        private Material _body, _occurrence, _face;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            _body = new Material(shader); _occurrence = new Material(shader); _face = new Material(shader);
            _root = new GameObject("scene");
            _view = _root.AddComponent<CadSceneView>();
            _view.BodyMaterial = _body;
            _view.Show(CadSceneViewTests.BoltScene());
            _visuals = _root.AddComponent<SelectionVisuals>();
            _visuals.Configure(_view, _occurrence, _face);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_body);
            Object.DestroyImmediate(_occurrence);
            Object.DestroyImmediate(_face);
        }

        [Test]
        public void RebuildingReleasesTheGeneratedOverlayMesh()
        {
            _visuals.Show(new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy"));
            var overlayMesh = _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>()[1].sharedMesh;
            _view.Show(CadSceneViewTests.BoltScene());
            Assert.IsTrue(overlayMesh == null);
            Assert.AreEqual(1, _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>().Length);
        }

        [Test]
        public void AnOccurrenceIsTintedAndRestored()
        {
            _visuals.Show(new Selection(SelectionKind.Occurrence, "ent_occ_2", null, "ent_occ_2"));
            Assert.AreSame(_occurrence, _view.Find("ent_occ_2").Bodies[0].Renderer.sharedMaterial);
            Assert.AreSame(_body, _view.Find("ent_occ_1").Bodies[0].Renderer.sharedMaterial);
            _visuals.Show(Selection.None);
            Assert.AreSame(_body, _view.Find("ent_occ_2").Bodies[0].Renderer.sharedMaterial);
        }

        [Test]
        public void AFaceGetsAnOverlayOfItsTrianglesOnly()
        {
            _visuals.Show(new Selection(SelectionKind.Face, "ent_occ_1", "ent_doc_bolt_f4", "proxy"));
            var overlay = _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>()[1];
            Assert.AreEqual("FaceHighlight", overlay.gameObject.name);
            Assert.AreEqual(6, overlay.sharedMesh.GetIndices(0).Length);
            Assert.AreSame(_face, overlay.GetComponent<MeshRenderer>().sharedMaterial);
            _visuals.Clear();
            Assert.AreEqual(1, _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>().Length);
        }
    }
}
