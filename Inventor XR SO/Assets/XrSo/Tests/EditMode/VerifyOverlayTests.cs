using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class VerifyOverlayTests
    {
        private GameObject _root;
        private CadSceneView _view;
        private VerifyOverlay _overlay;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Model");
            _view = _root.AddComponent<CadSceneView>();
            _view.Show(CadSceneViewTests.BoltScene());
            var head = new GameObject("Head"); head.transform.SetParent(_root.transform);
            _overlay = new GameObject("Overlay").AddComponent<VerifyOverlay>();
            _overlay.transform.SetParent(_root.transform, false);
            _overlay.Initialize(new Material(Shader.Find("Sprites/Default")), head.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void InventorMillimetresMapToTheSceneLikeTheMeshes()
        {
            // Bolt:2 is placed at glTF x = +0.03 m and shows at local x = -0.03 (CadSceneViewTests): 30 mm on X maps the same way.
            var p = VerifyOverlay.ToLocal(new[] { 30.0, 10, 5 });
            Assert.AreEqual(_view.Find("ent_occ_2").transform.localPosition.x, p.x, 1e-6f);
            Assert.AreEqual(0.010f, p.y, 1e-6f); Assert.AreEqual(0.005f, p.z, 1e-6f);
        }

        [Test]
        public void BoxesAndTintAreDrawnAndCleared()
        {
            _overlay.ShowBoxes(new[] { new VerifyBox(new[] { 0.0, 0, 0 }, new[] { 5.0, 20, 20 }) });
            _overlay.Tint(new[] { _view.Find("ent_occ_1") });
            Assert.AreEqual(1, _overlay.BoxCount);
            Assert.AreEqual(1, _overlay.TintedBodies);
            var line = _overlay.GetComponentsInChildren<LineRenderer>().Single();
            Assert.AreEqual(16, line.positionCount, "one polyline traces the 12 edges");
            _overlay.Clear();
            Assert.AreEqual(0, _overlay.BoxCount); Assert.AreEqual(0, _overlay.TintedBodies);
            Assert.True(_view.Find("ent_occ_1").Bodies[0].Renderer.enabled, "the tint is an overlay, the body stays visible");
        }

        [Test]
        public void DistanceLineCarriesItsLabel()
        {
            _overlay.ShowDistance(Vector3.zero, new Vector3(-0.03f, 0, 0), "30 mm");
            Assert.True(_overlay.HasDistance); Assert.AreEqual("30 mm", _overlay.DistanceLabel);
            _overlay.ClearDistance();
            Assert.False(_overlay.HasDistance);
        }

        [Test]
        public void ClosestVerticesFindTheGapBetweenTwoInstances()
        {
            Assert.True(VerifyOverlay.ClosestVertices(new[] { _view.Find("ent_occ_1") }, new[] { _view.Find("ent_occ_2") }, _view.transform, out var a, out var b));
            Assert.Less(Vector3.Distance(a, b), 0.03f, "closer than the 30 mm between the two origins");
            Assert.Greater(a.x, b.x, "Bolt:2 lies towards -X in Unity");
        }

        [Test]
        public void InstancesBoundsEncloseTheChosenInstancesOnly()
        {
            var one = InspectionGeometry.InstancesBounds(_view.transform, new[] { _view.Find("ent_occ_1") }).Value;
            var both = InspectionGeometry.InstancesBounds(_view.transform, _view.Instances).Value;
            Assert.Less(one.size.x, both.size.x);
            Assert.IsNull(InspectionGeometry.InstancesBounds(_view.transform, Enumerable.Empty<CadInstance>()));
        }
    }
}
