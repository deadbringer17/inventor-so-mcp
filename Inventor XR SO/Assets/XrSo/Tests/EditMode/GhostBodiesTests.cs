using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    /// <summary>GhostBodies restores each renderer to the state it wants; visibility and isolation no longer fight over it.</summary>
    public class GhostBodiesTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private Material _ghostMaterial;
        private GhostBodies _ghosts;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ghost-bodies");
            _view = _root.AddComponent<CadSceneView>();
            _view.BodyMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            _view.Show(CadSceneViewTests.BoltScene());
            _ghostMaterial = GhostBodies.CreateMaterial("test ghost", Color.white);
            _ghosts = new GhostBodies(_ghostMaterial);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_ghostMaterial);
        }

        private CadBody Body(string id) => _view.Find(id).Bodies[0];

        [Test]
        public void ARendererThatWasOffStaysOffAfterAddAndRemove()
        {
            var body = Body("ent_occ_1");
            body.RendererWanted = false; body.RefreshRenderer();
            _ghosts.Add(body);
            Assert.IsTrue(_ghosts.Contains(body));
            _ghosts.Remove(body);
            Assert.IsFalse(_ghosts.Contains(body));
            Assert.IsFalse(body.Renderer.enabled);
        }

        [Test]
        public void ARendererThatWasOffStaysOffAfterClear()
        {
            var body = Body("ent_occ_1");
            body.RendererWanted = false; body.RefreshRenderer();
            _ghosts.Add(body);
            _ghosts.Clear();
            Assert.IsFalse(body.Renderer.enabled);
            Assert.AreEqual(0, _ghosts.Count);
        }

        [Test]
        public void ABodyGhostedWhileTheFlatPatternHidesItComesBackWhenBothEnd()
        {
            var body = Body("ent_occ_1");
            body.FoldedHidden = true; body.RefreshRenderer();
            _ghosts.Add(body);
            _ghosts.Remove(body);
            Assert.IsFalse(body.Renderer.enabled);
            body.FoldedHidden = false; body.RefreshRenderer();
            Assert.IsTrue(body.Renderer.enabled);
        }

        [Test]
        public void AnEnabledBodyIsDrawnAsGhostThenComesBack()
        {
            var body = Body("ent_occ_1");
            _ghosts.Add(body);
            Assert.IsFalse(body.Renderer.enabled);
            Assert.AreEqual(1, _ghosts.Count);
            _ghosts.Add(body);   // adding twice is a no-op
            Assert.AreEqual(1, _ghosts.Count);
            _ghosts.Remove(body);
            Assert.IsTrue(body.Renderer.enabled);
            Assert.AreEqual(0, _ghosts.Count);
        }

        private (ComponentVisibility visibility, ComponentIsolation isolation, WorkbenchFrame frame) Tools()
        {
            var visibility = _root.AddComponent<ComponentVisibility>();
            visibility.Initialize(_view);
            var isolation = _root.AddComponent<ComponentIsolation>();
            isolation.Initialize(_view);
            return (visibility, isolation, WorkbenchFrame.FromHead(new CadPoint(0, 1.6, 0), 0, null));
        }

        [Test]
        public void HideThenIsolateThenReleaseKeepsTheHiddenComponentHidden()
        {
            var (visibility, isolation, frame) = Tools();
            var hidden = Body("ent_occ_1");

            visibility.Hide(new[] { "ent_occ_1" });
            Assert.IsTrue(isolation.Isolate("ent_occ_2", frame, 0f));
            Assert.IsFalse(hidden.Renderer.enabled);
            isolation.Release(true);

            Assert.IsFalse(hidden.Renderer.enabled);                              // still hidden
            Assert.IsFalse(hidden.GetComponent<Collider>().enabled);
            Assert.IsTrue(Body("ent_occ_2").Renderer.enabled);
            visibility.ShowAll();
            Assert.IsTrue(hidden.Renderer.enabled);                               // and ShowAll still brings it back
        }

        [Test]
        public void ShowAllWhileIsolatedDoesNotBringFadedBodiesBackUntilTheIsolationIsReleased()
        {
            var (visibility, isolation, frame) = Tools();
            var other = Body("ent_occ_1");

            visibility.Hide(new[] { "ent_occ_1" });
            isolation.Isolate("ent_occ_2", frame, 0f);
            visibility.ShowAll();
            Assert.IsFalse(other.Renderer.enabled);   // the isolation ghost still stands in for it: no solid + ghost double draw
            Assert.AreEqual(1, other.GhostOverlays);
            isolation.Release(true);
            Assert.IsTrue(other.Renderer.enabled);    // ShowAll made it Normal meanwhile
            Assert.AreEqual(0, other.GhostOverlays);
        }

        [Test]
        public void IsolateThenReleaseRestoresEveryBody()
        {
            var (_, isolation, frame) = Tools();
            isolation.Isolate("ent_occ_2", frame, 0f);
            Assert.IsTrue(isolation.IsFaded("ent_occ_1"));
            isolation.Release(true);
            Assert.IsTrue(_view.Instances.SelectMany(i => i.Bodies).All(b => b.Renderer.enabled));
            Assert.IsFalse(isolation.IsFaded("ent_occ_1"));
        }
    }
}
