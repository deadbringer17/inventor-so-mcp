using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    /// <summary>M5-03 (software part): the spatial handle is scale independent and only the knob edits the height.</summary>
    public class FlangeManipulatorTests
    {
        private GameObject _root;
        private Material _material;
        private FlangeManipulator _manip;
        private Transform _model;

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Manipulator test");
            _model = new GameObject("Model").transform; _model.SetParent(_root.transform);
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _manip = new GameObject("Manipulator").AddComponent<FlangeManipulator>();
            _manip.transform.SetParent(_model, false);
            _manip.Initialize(_model, _material);
        }

        [TearDown] public void Cleanup() { Object.DestroyImmediate(_root); Object.DestroyImmediate(_material); }

        private static Ray RayAt(Vector3 target) => new Ray(target + new Vector3(0, 0, -1), Vector3.forward);

        [TestCase(1f)][TestCase(0.1f)][TestCase(4f)]
        public void KnobKeepsConstantWorldSizeWhateverTheViewScaleOrCadSize(float scale)
        {
            _model.localScale = Vector3.one * scale;
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            Assert.That(_manip.KnobWorldSize, Is.EqualTo(FlangeManipulator.KnobMetres).Within(1e-5));
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 5000, true);
            Assert.That(_manip.KnobWorldSize, Is.EqualTo(FlangeManipulator.KnobMetres).Within(1e-5), "Height in mm never changes the target size.");
            // The knob sits at the flange end, in model scale: 20 mm at scale s is 0.02 * s metres from the anchor.
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            Assert.That(_manip.KnobWorldPosition.y - _manip.OriginWorldPosition.y, Is.EqualTo(0.02f * scale).Within(1e-5));
        }

        [TestCase(1f)][TestCase(0.1f)]
        public void DragHeightFollowsTheHandInModelMillimetresAtAnyScale(float scale)
        {
            _model.localScale = Vector3.one * scale;
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            var knob = _manip.KnobWorldPosition;
            Assert.True(_manip.TryBeginDrag(RayAt(knob), CadCoordinates.FromWorld(_model, knob)));
            Assert.True(_manip.Dragging);
            // 15 mm of model space upwards, expressed in world metres at the current scale.
            var hand = knob + Vector3.up * (0.015f * scale);
            Assert.That(_manip.Drag(CadCoordinates.FromWorld(_model, hand)), Is.EqualTo(35).Within(0.1));
            Assert.That(_manip.Drag(CadCoordinates.FromWorld(_model, knob - Vector3.up * (0.1f * scale))), Is.EqualTo(FlangeManipulator.MinHeightMm), "Clamped to the tool minimum.");
            _manip.EndDrag(); Assert.False(_manip.Dragging);
            Assert.AreEqual(FlangeManipulator.MinHeightMm, _manip.HeightMm);
        }

        [Test] public void OnlyARayOnTheKnobStartsADrag()
        {
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            var knob = _manip.KnobWorldPosition;
            Assert.False(_manip.TryBeginDrag(RayAt(knob + Vector3.right * (FlangeManipulator.PickRadiusMetres * 2)), default), "Missing the knob is a plain grip: view only.");
            Assert.False(_manip.Dragging);
            Assert.False(_manip.TryBeginDrag(new Ray(knob + Vector3.forward, Vector3.forward), default), "Behind the controller.");
            _manip.Hide();
            Assert.False(_manip.TryBeginDrag(RayAt(knob), default), "A hidden handle cannot be grabbed.");
        }

        [Test] public void NumericHeightMovesTheKnobButNeverDuringADrag()
        {
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            _manip.SetHeight(40); Assert.AreEqual(40, _manip.HeightMm);
            var knob = _manip.KnobWorldPosition;
            Assert.True(_manip.TryBeginDrag(RayAt(knob), CadCoordinates.FromWorld(_model, knob)));
            _manip.SetHeight(5); Assert.AreEqual(40, _manip.HeightMm, "The running drag owns the height.");
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 7, true); Assert.AreEqual(40, _manip.HeightMm);
        }

        [Test] public void IsOverKnobChangesNoStateAndPrecisionSlowsTheDragWithoutJumping()
        {
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            var knob = _manip.KnobWorldPosition;
            Assert.True(_manip.IsOverKnob(RayAt(knob))); Assert.False(_manip.Dragging);
            Assert.False(_manip.IsOverKnob(RayAt(knob + Vector3.right * 1f)));
            Assert.True(_manip.TryBeginDrag(RayAt(knob), CadCoordinates.FromWorld(_model, knob), FlangeManipulator.PrecisionFactor));
            Assert.That(_manip.Drag(CadCoordinates.FromWorld(_model, knob + Vector3.up * 0.010f), FlangeManipulator.PrecisionFactor), Is.EqualTo(21).Within(0.05),
                "10 mm of hand = 1 mm of height at the precision factor");
            // switching to the normal factor mid-drag re-anchors: the height does not jump
            var hand = knob + Vector3.up * 0.010f;
            Assert.That(_manip.Drag(CadCoordinates.FromWorld(_model, hand), 1), Is.EqualTo(21).Within(0.05));
            Assert.That(_manip.Drag(CadCoordinates.FromWorld(_model, hand + Vector3.up * 0.010f), 1), Is.EqualTo(31).Within(0.05));
        }

        [Test] public void AGarbagePoseNeverReachesTheDragSoTheLastValidHeightStays()
        {
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            var knob = _manip.KnobWorldPosition;
            Assert.True(_manip.TryBeginDrag(RayAt(knob), CadCoordinates.FromWorld(_model, knob)));
            Assert.That(_manip.Drag(CadCoordinates.FromWorld(_model, knob + Vector3.up * 0.005f)), Is.EqualTo(25).Within(0.05));
            // CadPoint itself refuses non-finite coordinates, so a garbage pose cannot even reach the drag
            Assert.Throws<System.ArgumentException>(() => new CadPoint(double.NaN, 0, 0));
            Assert.That(_manip.HeightMm, Is.EqualTo(25).Within(0.05));
            Assert.True(_manip.Dragging);
        }

        [Test] public void HandleFrameUsesTheSheetFacePairAndNotTheThinSideFace()
        {
            var top = new DesignFace("ent_top", new CadPoint(0, 0, 2), new CadPoint(0, 0, 1));
            var bottom = new DesignFace("ent_bottom", new CadPoint(0, 0, 0), new CadPoint(0, 0, -1));
            var side = new DesignFace("ent_side", new CadPoint(0, 0, 1), new CadPoint(0, -1, 0));
            var edge = new DesignEdge("ent_edge", "line", new[] { new CadPoint(0, 0, 2), new CadPoint(100, 0, 2) });
            Assert.True(FlangeManipulator.TryFrame(edge, new[] { side, top, bottom }, 2.0, out var origin, out var axis, out var fromFaces));
            Assert.True(fromFaces);
            Assert.AreEqual(50, origin.X, 1e-9); Assert.AreEqual(2, origin.Z, 1e-9);
            Assert.AreEqual(1, axis.Z, 1e-9); Assert.AreEqual(0, axis.Y, 1e-9);
        }

        [Test] public void HandleFrameFallsBackPerpendicularToTheEdgeAndSaysSo()
        {
            var edge = new DesignEdge("ent_edge", "line", new[] { new CadPoint(0, 0, 2), new CadPoint(100, 0, 2) });
            Assert.True(FlangeManipulator.TryFrame(edge, System.Array.Empty<DesignFace>(), 2.0, out var origin, out var axis, out var fromFaces));
            Assert.False(fromFaces);
            Assert.AreEqual(1, axis.Y, 1e-9); Assert.AreEqual(0, axis.X, 1e-9, "Perpendicular to the edge.");
            Assert.False(FlangeManipulator.TryFrame(new DesignEdge("x", "line", new[] { new CadPoint(1, 1, 1) }), null, null, out _, out _, out _));
        }

        private static GlbModel PreviewWithTip(double tipZMm)
        {
            float[] mm = { 0, -100, 0, 100, 0, 2, 50, 0, 2, 50, 0, (float)(2 + tipZMm) };
            var positions = new float[mm.Length];
            for (int i = 0; i < mm.Length; i++) positions[i] = mm[i] / 1000f;
            return new GlbModel("doc", new[] { new GlbPrimitive(1, "b", true, positions, new float[0], new uint[0], System.Array.Empty<FaceRange>()) });
        }

        [Test] public void CalibrationFlipsAWrongSignAndKeepsTheRightOne()
        {
            var edge = new DesignEdge("ent_edge", "line", new[] { new CadPoint(0, 0, 2), new CadPoint(100, 0, 2) });
            var up = new CadPoint(0, 0, 1);
            Assert.True(FlangeManipulator.TryCalibrate(PreviewWithTip(-20), edge, up, true, 20, 2.0, out var axis));
            Assert.AreEqual(-1, axis.Z, 1e-6, "Inventor grew the flange downwards.");
            Assert.True(FlangeManipulator.TryCalibrate(PreviewWithTip(20), edge, up, true, 20, 2.0, out axis));
            Assert.AreEqual(1, axis.Z, 1e-6);
        }

        [Test] public void CalibrationWithoutEvidenceLeavesTheHeuristicAlone()
        {
            var edge = new DesignEdge("ent_edge", "line", new[] { new CadPoint(0, 0, 2), new CadPoint(100, 0, 2) });
            var up = new CadPoint(0, 0, 1);
            Assert.False(FlangeManipulator.TryCalibrate(PreviewWithTip(0.5), edge, up, true, 20, 2.0, out var axis));
            Assert.AreEqual(1, axis.Z, 1e-9);
            Assert.False(FlangeManipulator.TryCalibrate(null, edge, up, true, 20, 2.0, out _));
            Assert.False(FlangeManipulator.TryCalibrate(PreviewWithTip(20), null, up, true, 20, 2.0, out _));
        }

        [Test] public void ACalibratedHandleIsFlaggedAndFollowsTheNewAxis()
        {
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, -1, 0), 20, false, true);
            Assert.True(_manip.AxisCalibrated); Assert.False(_manip.AxisFromFaces);
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, false);
            Assert.False(_manip.AxisCalibrated);
        }

        [Test] public void MidpointIsTheLengthMidpointOfAPolyline()
        {
            var mid = FlangeManipulator.Midpoint(new[] { new CadPoint(0, 0, 0), new CadPoint(2, 0, 0), new CadPoint(10, 0, 0) });
            Assert.AreEqual(5, mid.X, 1e-9);
        }

        [Test] public void HandleHasNoColliderSoItCanNeverBeAPickedCadBody()
        {
            _manip.Show(new CadPoint(0, 0, 0), new CadPoint(0, 1, 0), 20, true);
            Assert.AreEqual(0, _model.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(0, _model.GetComponentsInChildren<CadBody>(true).Length);
        }
    }
}
