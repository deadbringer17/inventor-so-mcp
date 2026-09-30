using System.Collections.Generic;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class SketchSheetViewTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();
        private SketchSheetView _view;
        private Transform _sceneRoot;
        private SketchFrame _frame;
        private WorkbenchFrame _bench;

        [SetUp]
        public void SetUp()
        {
            _sceneRoot = new GameObject("root").transform;
            var host = new GameObject("sheet");
            _roots.Add(_sceneRoot.gameObject); _roots.Add(host);
            _view = host.AddComponent<SketchSheetView>();
            _view.Bind(_sceneRoot);
            _frame = new SketchFrame(new CadPoint(0, 0, 0), new CadPoint(1, 0, 0), new CadPoint(0, 1, 0));
            _bench = WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 0, null);
        }

        [TearDown]
        public void TearDown() { foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r); _roots.Clear(); }

        private Vector3 SketchToWorld(double x, double y) =>
            _sceneRoot.TransformPoint(CadCoordinates.ToLocal(_frame.ToModel(new CadPoint(x, y))));

        [Test]
        public void EnterSnapsToTheLayoutPose()
        {
            _view.Enter(_frame, 100, 50, _bench);
            Assert.AreEqual(SketchSheetState.Sheet, _view.State);
            Assert.IsTrue(_view.Tweening);
            _view.Snap();
            Assert.IsFalse(_view.Tweening);
            var expected = SketchSheetLayout.Compute(_frame, 100, 50, _bench);
            Assert.AreEqual((float)expected.Scale, _sceneRoot.localScale.x, 1e-4f);
            Assert.AreEqual((float)expected.Position.Y, _sceneRoot.position.y, 1e-4f);
            // il piano di schizzo e orizzontale alla quota del tavolo
            Assert.AreEqual((float)_bench.DeskY, SketchToWorld(10, 20).y, 1e-3f);
            Assert.AreEqual((float)_bench.DeskY, SketchToWorld(-30, 5).y, 1e-3f);
            // il centro del foglio sta a PartDistance davanti alla testa
            var centre = SketchToWorld(50, 25);
            Assert.AreEqual((float)WorkbenchLayout.PartDistance, centre.z, 1e-3f);
            Assert.AreEqual(0f, centre.x, 1e-3f);
        }

        [Test]
        public void TweenDoesNotJumpBeforeSnap()
        {
            var before = _sceneRoot.position;
            _view.Enter(_frame, 100, 50, _bench);
            Assert.AreEqual(before, _sceneRoot.position);
        }

        [Test]
        public void ProjectPenWithinTwoCentimetersUsesTheTip()
        {
            _view.Enter(_frame, 100, 50, _bench); _view.Snap();
            var tip = SketchToWorld(10, 20) + Vector3.up * 0.01f;
            // raggio volutamente sbagliato: deve contare la punta
            Assert.IsTrue(_view.ProjectPen(tip, new Ray(tip + Vector3.right, Vector3.up), _frame, out var p));
            Assert.AreEqual(10f, p.x, 0.05f);
            Assert.AreEqual(20f, p.y, 0.05f);
        }

        [Test]
        public void ProjectPenBeyondTwoCentimetersUsesTheRay()
        {
            _view.Enter(_frame, 100, 50, _bench); _view.Snap();
            var target = SketchToWorld(-15, 8);
            var tip = target + Vector3.up * 0.10f + Vector3.right * 0.05f;   // lontano dal piano, con altre x/y
            var ray = new Ray(target + Vector3.up * 0.2f, Vector3.down);
            Assert.IsTrue(_view.ProjectPen(tip, ray, _frame, out var p));
            Assert.AreEqual(-15f, p.x, 0.05f);
            Assert.AreEqual(8f, p.y, 0.05f);
            // raggio parallelo al piano: nessun punto
            Assert.IsFalse(_view.ProjectPen(tip, new Ray(tip, Vector3.right), _frame, out _));
        }

        [Test]
        public void ModelAndSheetViewsNeverChangeCadCoordinates()
        {
            _view.Enter(_frame, 100, 50, _bench); _view.Snap();
            var tipSheet = SketchToWorld(10, 20);
            Assert.IsTrue(_view.ProjectPen(tipSheet, new Ray(tipSheet + Vector3.up, Vector3.down), _frame, out var a));

            _view.ShowModel(WorkbenchLayout.Part(_bench, 0.1)); _view.Snap();
            Assert.AreEqual(SketchSheetState.Model, _view.State);
            var tipModel = SketchToWorld(10, 20);
            Assert.AreNotEqual(tipSheet, tipModel);
            Assert.IsTrue(_view.ProjectPen(tipModel, new Ray(tipModel + Vector3.up, Vector3.down), _frame, out var b));
            Assert.AreEqual(a.x, b.x, 0.05f);
            Assert.AreEqual(a.y, b.y, 0.05f);

            _view.ShowSheet(_frame, 100, 50, _bench); _view.Snap();
            Assert.AreEqual(SketchSheetState.Sheet, _view.State);
            Assert.AreEqual(tipSheet.x, SketchToWorld(10, 20).x, 1e-3f);
            // la cornice CAD non e mai stata toccata
            Assert.AreEqual(0.0, _frame.OriginMm.X);
            Assert.AreEqual(1.0, _frame.XAxis.X);
            Assert.AreEqual(1.0, _frame.YAxis.Y);
        }

        [Test]
        public void ExitLeavesNoStateAndNoTween()
        {
            _view.Enter(_frame, 100, 50, _bench);
            _view.Exit();
            Assert.AreEqual(SketchSheetState.None, _view.State);
            Assert.IsFalse(_view.Tweening);
        }
    }
}
