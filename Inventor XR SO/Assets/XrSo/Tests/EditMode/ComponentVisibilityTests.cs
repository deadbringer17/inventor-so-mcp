using System.Linq;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class ComponentVisibilityTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private ComponentVisibility _visibility;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Model");
            _view = _root.AddComponent<CadSceneView>();
            _view.Show(CadSceneViewTests.BoltScene());
            _visibility = _root.AddComponent<ComponentVisibility>();
            _visibility.Initialize(_view);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        private CadBody Body(string id) => _view.Find(id).Bodies[0];
        private static bool HasGhost(CadBody body) => body.GetComponentsInChildren<MeshRenderer>().Any(r => r != body.Renderer && r.enabled);

        [Test]
        public void XRayDrawsAGhostAndKeepsTheColliderSoTheRayStillHits()
        {
            _visibility.XRay(new[] { "ent_occ_1" });
            var body = Body("ent_occ_1");
            Assert.AreEqual(OccurrenceVisibility.Ghost, _visibility.Get("ent_occ_1"));
            Assert.False(body.Renderer.enabled); Assert.True(HasGhost(body));
            Assert.True(body.GetComponent<Collider>().enabled);
            Assert.AreEqual(OccurrenceVisibility.Normal, _visibility.Get("ent_occ_2"));
        }

        [Test]
        public void HideTurnsOffRendererAndColliderSoTheRayPassesThrough()
        {
            _visibility.Hide(new[] { "ent_occ_1" });
            var body = Body("ent_occ_1");
            Assert.False(body.Renderer.enabled); Assert.False(HasGhost(body));
            Assert.False(body.GetComponent<Collider>().enabled);
            Physics.SyncTransforms();
            Assert.False(Physics.Raycast(new Vector3(-0.005f, 1f, 0.005f), Vector3.down, out var hit, 5f) && hit.collider.GetComponent<CadBody>() == body);
        }

        [Test]
        public void IsolateGhostsEverythingElseAndShowAllRestores()
        {
            _visibility.Isolate(new[] { "ent_occ_2" });
            Assert.AreEqual(OccurrenceVisibility.Ghost, _visibility.Get("ent_occ_1"));
            Assert.AreEqual(OccurrenceVisibility.Normal, _visibility.Get("ent_occ_2"));
            _visibility.ShowAll();
            Assert.False(_visibility.AnyChanged);
            foreach (var instance in _view.Instances)
            {
                Assert.True(instance.Bodies[0].Renderer.enabled); Assert.True(instance.Bodies[0].GetComponent<Collider>().enabled);
                Assert.False(HasGhost(instance.Bodies[0]));
            }
        }

        [Test]
        public void SnapshotAndRestoreReturnToThePreviousState()
        {
            _visibility.Hide(new[] { "ent_occ_1" });
            var snapshot = _visibility.Snapshot();
            _visibility.Isolate(new[] { "ent_occ_1" });
            _visibility.Restore(snapshot);
            Assert.AreEqual(OccurrenceVisibility.Hidden, _visibility.Get("ent_occ_1"));
            Assert.AreEqual(OccurrenceVisibility.Normal, _visibility.Get("ent_occ_2"));
        }

        [Test]
        public void ASceneRebuildForgetsEveryState()
        {
            int changed = 0; _visibility.Changed += () => changed++;
            _visibility.Hide(new[] { "ent_occ_1" });
            _view.Show(CadSceneViewTests.BoltScene());
            Assert.False(_visibility.AnyChanged);
            Assert.AreEqual(2, changed);
            Assert.True(_view.Find("ent_occ_1").Bodies[0].Renderer.enabled);
        }

        [Test]
        public void UnknownOccurrencesAreIgnored()
        {
            _visibility.Hide(new[] { "missing" });
            Assert.False(_visibility.AnyChanged);
        }
    }
}
