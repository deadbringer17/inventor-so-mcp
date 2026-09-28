using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public sealed class AssemblyFaceVisualTests
    {
        private GameObject _root;
        private Material _material;
        private CadSceneView _view;
        private AssemblyVisuals _visuals;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Assembly face visuals test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _view = _root.AddComponent<CadSceneView>();
            _view.BodyMaterial = _material;
            _view.Show(CadSceneViewTests.BoltScene());
            _visuals = _root.AddComponent<AssemblyVisuals>();
            _visuals.Initialize(_material);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        private AssemblyReference Reference(string occurrence, int body, int ordinal, string kind = "face")
        {
            var json = new JObject { ["id"] = "ent_dynamic", ["occurrence_id"] = occurrence, ["name"] = "Face", ["kind"] = kind,
                ["geometry"] = "kPlaneSurface", ["point_mm"] = new JArray(0, 0, 0), ["body_index"] = body, ["face_ordinal"] = ordinal };
            return AssemblyContext.Parse(new JObject { ["document_id"] = "doc_asm", ["revision"] = "r", ["kind"] = "assembly", ["truncated"] = false,
                ["occurrences"] = JArray.FromObject(new[] { new JObject { ["occurrence_id"] = occurrence, ["editable"] = true, ["grounded"] = false,
                    ["suppressed"] = false, ["adaptive"] = false, ["dof_translation"] = 0, ["dof_rotation"] = 0, ["dof_complete"] = true,
                    ["translation_axes"] = new JArray(), ["rotation_axes"] = new JArray() } }), ["references"] = new JArray(json) }, new DocumentState("doc_asm", "r", "v")).References[0];
        }

        [Test]
        public void FaceReferencesHighlightMappedInstanceWithColorAndNoCollider()
        {
            var body = _view.Find("ent_occ_1").Bodies[0];
            var ordinal = body.Primitive.Faces[0].Ordinal;
            _visuals.Show(null, Reference("ent_occ_1", body.Primitive.BodyIndex, ordinal),
                Reference("ent_occ_2", body.Primitive.BodyIndex, ordinal), false, false, -1);

            var a = _view.Find("ent_occ_1").GetComponentsInChildren<MeshRenderer>()[1];
            var b = _view.Find("ent_occ_2").GetComponentsInChildren<MeshRenderer>()[1];
            Assert.AreEqual("AssemblyFaceHighlight", a.name);
            Assert.AreEqual(body.Primitive.Faces[0].IndexCount, a.GetComponent<MeshFilter>().sharedMesh.GetIndices(0).Length);
            Assert.AreEqual(0, a.GetComponents<Collider>().Length);
            Assert.AreEqual("XrSo/HighlightOverlay", a.sharedMaterial.shader.name);
            Assert.AreNotSame(_material, a.sharedMaterial);
            var props = new MaterialPropertyBlock(); a.GetPropertyBlock(props);
            Assert.That(props.GetColor(Shader.PropertyToID("_Color")).r, Is.EqualTo(0.1f).Within(0.001f));
            b.GetPropertyBlock(props);
            Assert.That(props.GetColor(Shader.PropertyToID("_Color")).r, Is.EqualTo(1f).Within(0.001f));
            Assert.AreSame(a.sharedMaterial, b.sharedMaterial);
            Assert.AreSame(_material, _view.Find("ent_occ_1").Bodies[0].Renderer.sharedMaterial);
        }

        [Test]
        public void MissingOccurrenceBodyOrFaceMappingCreatesNoOverlay()
        {
            var valid = _view.Find("ent_occ_1").Bodies[0];
            _visuals.Show(null, Reference("ent_missing", valid.Primitive.BodyIndex, valid.Primitive.Faces[0].Ordinal),
                Reference("ent_occ_1", 999, valid.Primitive.Faces[0].Ordinal), false, false, -1);
            Assert.AreEqual(2, _view.GetComponentsInChildren<MeshRenderer>().Length);
            _visuals.Show(null, Reference("ent_occ_1", valid.Primitive.BodyIndex, 999), null, false, false, -1);
            Assert.AreEqual(2, _view.GetComponentsInChildren<MeshRenderer>().Length);
        }

        [Test]
        public void ClearAndSceneRebuildReleaseOverlayMesh()
        {
            var body = _view.Find("ent_occ_1").Bodies[0];
            _visuals.Show(null, Reference("ent_occ_1", body.Primitive.BodyIndex, body.Primitive.Faces[0].Ordinal), null, false, false, -1);
            var overlay = _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>()[1].sharedMesh;
            _visuals.Clear();
            Assert.IsTrue(overlay == null);
            Assert.AreEqual(1, _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>().Length);

            _visuals.Show(null, Reference("ent_occ_1", body.Primitive.BodyIndex, body.Primitive.Faces[0].Ordinal), null, false, false, -1);
            overlay = _view.Find("ent_occ_1").GetComponentsInChildren<MeshFilter>()[1].sharedMesh;
            _view.Show(CadSceneViewTests.BoltScene());
            Assert.IsTrue(overlay == null);
        }
    }
}
