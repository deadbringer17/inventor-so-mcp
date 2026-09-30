using System.Collections.Generic;
using InventorXrSo.Core.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class WorkbenchTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();
        private Workbench _bench;
        private Transform _head, _scene;

        [SetUp]
        public void SetUp()
        {
            var host = new GameObject("bench");
            _head = new GameObject("head").transform;
            _scene = new GameObject("scene").transform;
            _roots.Add(host); _roots.Add(_head.gameObject); _roots.Add(_scene.gameObject);
            _bench = host.AddComponent<Workbench>();
            _head.position = new Vector3(0, 1.2f, 0);
        }

        [TearDown]
        public void TearDown() { foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r); _roots.Clear(); }

        [Test]
        public void ApplyPartReachesTheLayoutPoseAfterSnap()
        {
            _bench.Recenter(_head);
            _bench.ApplyPart(_scene, 0.8);
            var before = _scene.position;
            Assert.IsTrue(_bench.Tweening);
            Assert.AreEqual(before, _scene.position);   // niente salto istantaneo
            _bench.Snap();
            var expected = WorkbenchLayout.Part(_bench.Frame, 0.8);
            Assert.AreEqual((float)expected.Scale, _scene.localScale.x, 1e-5f);
            Assert.AreEqual((float)expected.Position.Z, _scene.position.z, 1e-4f);
            Assert.AreEqual(1.2f - (float)WorkbenchFrame.DefaultDeskDrop, _scene.position.y, 1e-4f);
            Assert.AreEqual(0.5f, _scene.localScale.x, 1e-5f);
            Assert.IsFalse(_bench.Clamped);
        }

        [Test]
        public void RecenterFollowsTheHeadYawOnly()
        {
            var first = _bench.Recenter(_head);
            Assert.AreEqual(0, first.YawDegrees, 1e-6);
            _head.position = new Vector3(1, 1.3f, 2);
            _head.rotation = Quaternion.Euler(30, 90, 10);
            var second = _bench.Recenter(_head);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(90, second.YawDegrees, 1e-3);
            Assert.AreEqual(1.0, second.Origin.X, 1e-6);
            _bench.ApplyPart(_scene, 0.4); _bench.Snap();
            Assert.AreEqual(1.4f, _scene.position.x, 1e-3f);   // 0,40 m davanti, yaw 90 = +X
            Assert.AreEqual(2f, _scene.position.z, 1e-3f);
            Assert.AreEqual(90f, _scene.eulerAngles.y, 1e-2f);
        }

        [Test]
        public void RecenterMovesAnAlreadyPlacedPartWithATween()
        {
            _bench.Recenter(_head);
            _bench.ApplyPart(_scene, 0.4); _bench.Snap();
            _head.rotation = Quaternion.Euler(0, 180, 0);
            var pos = _scene.position;
            _bench.Recenter(_head);
            Assert.IsTrue(_bench.Tweening);
            Assert.AreEqual(pos, _scene.position);
            _bench.Snap();
            Assert.AreEqual(-0.4f, _scene.position.z, 1e-3f);
        }

        [Test]
        public void ClampedIsExposedForTinyAndHugeParts()
        {
            _bench.Recenter(_head);
            _bench.ApplyPart(_scene, 1000);   // ideale 0,0004 < 0,001
            Assert.IsTrue(_bench.Clamped);
            _bench.Snap();
            Assert.AreEqual(0.001f, _scene.localScale.x, 1e-6f);
            _bench.ApplyPart(_scene, 0.01);   // ideale 40 > 10
            Assert.IsTrue(_bench.Clamped);
            _bench.Snap();
            Assert.AreEqual(10f, _scene.localScale.x, 1e-4f);
            _bench.ApplyPart(_scene, 0.4);
            Assert.IsFalse(_bench.Clamped);
        }

        [Test]
        public void CalibratedDeskHeightSetsThePlaneOnNextRecenter()
        {
            _bench.SetDeskHeight(0.72);
            _bench.Recenter(_head);
            _bench.ApplyPart(_scene, 0.4); _bench.Snap();
            Assert.AreEqual(0.72f, _scene.position.y, 1e-4f);
        }
    }
}
