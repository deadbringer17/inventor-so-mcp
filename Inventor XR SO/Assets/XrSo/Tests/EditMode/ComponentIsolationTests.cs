using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    /// <summary>Isolamento visivo di un componente: avanza a meta strada, il resto al 20%, il rilascio ripristina.</summary>
    public class ComponentIsolationTests
    {
        private GameObject _root;
        private Material _material;
        private CadSceneView _view;
        private ComponentIsolation _isolation;
        private WorkbenchFrame _frame;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("isolation");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _view = new GameObject("model").AddComponent<CadSceneView>();
            _view.transform.SetParent(_root.transform);
            _view.BodyMaterial = _material;
            var model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture));
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Test"",""definition_kind"":""assembly"",""children"":[
                {""name"":""A"",""occurrence_id"":""ent_a"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]},
                {""name"":""B"",""occurrence_id"":""ent_b"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",
                 ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.03,0,0,1],""children"":[]},
                {""name"":""C"",""occurrence_id"":""ent_c"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",
                 ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.06,0,0,1],""children"":[]}]}}"));
            _view.Show(new LoadedScene(graph, new Dictionary<string, GlbModel> { ["doc_bolt"] = model },
                new Dictionary<string, string> { ["doc_bolt"] = "a_test" }, new List<string>()));
            _view.transform.position = new Vector3(0, 0, 2);
            _isolation = _view.gameObject.AddComponent<ComponentIsolation>();
            _isolation.Initialize(_view);
            _frame = WorkbenchFrame.FromHead(new CadPoint(0, 1.6, 0), 0, null);
        }

        [TearDown] public void TearDown() { Object.DestroyImmediate(_root); Object.DestroyImmediate(_material); }

        private Vector3 CenterOf(string id)
        {
            var instance = _view.Find(id);
            return instance.transform.TransformPoint(ScenePlacement.LocalBounds(instance.transform).center);
        }

        private int Ghosts() => _view.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == "IsolationGhost");

        [Test]
        public void IsolateMovesTheComponentHalfwayToTheUserOverAbout250Ms()
        {
            var before = CenterOf("ent_b"); var home = _view.Find("ent_b").transform.localPosition;
            Assert.IsTrue(_isolation.Isolate("ent_b", _frame));
            Assert.IsTrue(_isolation.Active); Assert.AreEqual("ent_b", _isolation.OccurrenceId); Assert.IsTrue(_isolation.Tweening);
            Assert.AreEqual(before, CenterOf("ent_b"));   // niente salto istantaneo
            _isolation.Snap();
            var origin = new Vector3(0, 1.6f, 0);
            Assert.That(Vector3.Distance(CenterOf("ent_b"), (before + origin) * 0.5f), Is.LessThan(1e-4f));
            Assert.AreEqual(home, _isolation.HomeLocalPosition);
            Assert.IsFalse(_isolation.Tweening);
        }

        [Test]
        public void TheRestFadesToTwentyPercentWithoutSwappingTheirMaterials()
        {
            var originals = _view.Instances.SelectMany(i => i.Bodies).Select(b => (b, b.Renderer.sharedMaterial)).ToArray();
            _isolation.Isolate("ent_b", _frame); _isolation.Snap();
            Assert.AreEqual(2, _isolation.FadedBodies); Assert.AreEqual(2, Ghosts());
            Assert.IsTrue(_view.Find("ent_b").Bodies.All(b => b.Renderer.enabled));
            foreach (var id in new[] { "ent_a", "ent_c" })
            {
                Assert.IsTrue(_view.Find(id).Bodies.All(b => !b.Renderer.enabled), id);
                Assert.IsTrue(_isolation.IsFaded(id));
            }
            var ghost = _view.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "IsolationGhost");
            Assert.AreEqual(0.2f, ghost.sharedMaterial.GetColor("_Color").a, 1e-5f);
            Assert.AreEqual(0.2f, ComponentIsolation.FadeAlpha);
            Assert.IsNull(ghost.GetComponent<Collider>());
            foreach (var (body, material) in originals) Assert.AreSame(material, body.Renderer.sharedMaterial, "no shared material was replaced");
            Assert.IsNotNull(_view.Find("ent_a").Bodies[0].GetComponent<Collider>(), "the ray still hits the faded components");
        }

        [Test]
        public void ReleaseRestoresPositionAndOpacity()
        {
            var home = _view.Find("ent_b").transform.localPosition;
            _isolation.Isolate("ent_b", _frame); _isolation.Snap();
            _isolation.Release();
            Assert.IsFalse(_isolation.Active);
            Assert.AreEqual(0, Ghosts(), "opacity is back at once");
            Assert.IsTrue(_view.Instances.SelectMany(i => i.Bodies).All(b => b.Renderer.enabled));
            Assert.IsTrue(_isolation.Tweening);
            _isolation.Snap();
            Assert.AreEqual(home, _view.Find("ent_b").transform.localPosition);
            Assert.IsFalse(_isolation.Tweening);
        }

        [Test]
        public void ImmediateReleaseAndIsolatingAnotherReleaseThePreviousOne()
        {
            var homeA = _view.Find("ent_a").transform.localPosition; var homeB = _view.Find("ent_b").transform.localPosition;
            _isolation.Isolate("ent_a", _frame); _isolation.Snap();
            Assert.IsTrue(_isolation.Isolate("ent_b", _frame));
            Assert.AreEqual("ent_b", _isolation.OccurrenceId);
            Assert.AreEqual(homeA, _view.Find("ent_a").transform.localPosition, "the previous component went back at once");
            _isolation.Snap();
            _isolation.Release(true);
            Assert.IsFalse(_isolation.Tweening); Assert.AreEqual(homeB, _view.Find("ent_b").transform.localPosition);
            Assert.AreEqual(0, Ghosts());
        }

        [Test]
        public void UnknownOccurrenceAndMissingFrameAreRefusedAndIsolatingTwiceIsANoOp()
        {
            Assert.IsFalse(_isolation.Isolate("ent_zzz", _frame)); Assert.IsFalse(_isolation.Active);
            Assert.IsFalse(_isolation.Isolate("ent_a", null)); Assert.IsFalse(_isolation.Isolate("", _frame));
            int changes = 0; _isolation.Changed += () => changes++;
            Assert.IsTrue(_isolation.Isolate("ent_a", _frame)); Assert.IsTrue(_isolation.Isolate("ent_a", _frame));
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void ARebuiltSceneClearsTheIsolationWithoutTouchingTheNewInstances()
        {
            _isolation.Isolate("ent_a", _frame); _isolation.Snap();
            bool changed = false; _isolation.Changed += () => changed = true;
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v2"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Test"",""definition_kind"":""assembly"",""children"":[
                {""name"":""A"",""occurrence_id"":""ent_a"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]}]}}"));
            var model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture));
            _view.Show(new LoadedScene(graph, new Dictionary<string, GlbModel> { ["doc_bolt"] = model },
                new Dictionary<string, string> { ["doc_bolt"] = "a_test" }, new List<string>()));
            Assert.IsTrue(changed); Assert.IsFalse(_isolation.Active); Assert.IsFalse(_isolation.Tweening); Assert.AreEqual(0, _isolation.FadedBodies);
            Assert.IsTrue(_view.Find("ent_a").Bodies.All(b => b.Renderer.enabled));
            Assert.AreEqual(0, Ghosts());
        }
    }
}
