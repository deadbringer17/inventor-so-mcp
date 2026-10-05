using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Tests
{
    public class GhostContextTests
    {
        private GameObject _root;
        private GhostContext _ghost;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("scene");
            _ghost = new GameObject("ghost").AddComponent<GhostContext>();
            _ghost.transform.SetParent(_root.transform, false);
        }

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        private static readonly float[] Pose = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.03f, 0, 0, 1 };

        [Test]
        public void ShowDrawsParentWithoutAnyEnabledCollider()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r1");
            Assert.IsTrue(_ghost.IsShowing);
            Assert.AreEqual(2, _ghost.RendererCount);
            Assert.IsFalse(_ghost.HasSelectableColliders);
            Assert.AreEqual(0, _root.GetComponentsInChildren<Collider>(true).Count(c => c.enabled));
            Assert.AreEqual(0, _root.GetComponentsInChildren<CadBody>(true).Length);   // never selectable, never counted by Fit
        }

        [Test]
        public void MaterialIsTranslucentAndShadowsAreOff()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r1");
            Assert.Less(_ghost.Material.GetColor("_Color").a, 1f);
            foreach (var r in _root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode);
                Assert.IsFalse(r.receiveShadows);
                Assert.AreEqual(2, r.gameObject.layer);   // Ignore Raycast
            }
        }

        [Test]
        public void LabelAndRevision()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "rev-7");
            Assert.AreEqual("contesto: prima delle modifiche", _ghost.Label);
            Assert.AreEqual("rev-7", _ghost.RevisionLabel);
            Assert.AreEqual("doc_asm", _ghost.ParentDocumentId);
        }

        [Test]
        public void ClearEmptiesTheGhost()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r1");
            _ghost.Clear();
            Assert.IsFalse(_ghost.IsShowing);
            Assert.AreEqual(0, _root.GetComponentsInChildren<MeshRenderer>(true).Length);
            Assert.IsFalse(_ghost.HasSelectableColliders);
            Assert.IsNull(_ghost.RevisionLabel);
        }

        [Test]
        public void ShowReplacesThePreviousGhost()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r1");
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r2");
            Assert.AreEqual(2, _ghost.RendererCount);
            Assert.AreEqual("r2", _ghost.RevisionLabel);
        }

        [Test]
        public void ParentOmittingMeshesIsTruncated()
        {
            var s = CadSceneViewTests.BoltScene();
            var truncated = new LoadedScene(s.Graph, s.Models, s.AssetIds, new List<string> { "doc_big" });
            _ghost.Show(truncated, Pose, "r1");
            Assert.IsTrue(_ghost.Truncated);
            Assert.AreEqual(2, _ghost.RendererCount);   // only the loaded definitions
            _ghost.Show(s, Pose, "r1");
            Assert.IsFalse(_ghost.Truncated);
        }

        [Test]
        public void MoreThan200DefinitionsIsTruncated()
        {
            var s = CadSceneViewTests.BoltScene();
            var bolt = s.Models["doc_bolt"];
            var models = new Dictionary<string, GlbModel>();
            var assets = new Dictionary<string, string>();
            for (int i = 0; i < 201; i++) { models["doc_x" + i] = bolt; assets["doc_x" + i] = "a" + i; }
            models["doc_bolt"] = bolt; assets["doc_bolt"] = "a_bolt";
            _ghost.Show(new LoadedScene(s.Graph, models, assets, new List<string>()), Pose, "r1");
            Assert.IsTrue(_ghost.Truncated);
        }

        [Test]
        public void TheEnteredOccurrenceIsNotDuplicated()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r1", "ent_occ_2");
            Assert.AreEqual(1, _ghost.RendererCount);
        }

        [Test]
        public void GhostRootIsTheInverseOfTheOccurrencePose()
        {
            _ghost.Show(CadSceneViewTests.BoltScene(), Pose, "r1");
            var ghostRoot = _ghost.transform.Find("GhostContext");
            // glTF +0.03 in X is -0.03 after the handedness flip; the ghost is moved the opposite way.
            Assert.AreEqual(0.03f, ghostRoot.localPosition.x, 1e-5f);
            // Bolt:2 sits at the occurrence pose, so in the scene's frame it lands on the origin.
            var bolt2 = ghostRoot.Find("Bolt:2");
            Assert.AreEqual(0f, _root.transform.InverseTransformPoint(bolt2.position).x, 1e-5f);
        }
    }
}
