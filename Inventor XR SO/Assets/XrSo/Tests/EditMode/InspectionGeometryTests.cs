using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class InspectionGeometryTests
    {
        private GameObject _model;
        [SetUp] public void SetUp() { _model = new GameObject("model"); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_model); }

        [Test]
        public void MeasurementUsesCadUnitsAfterScaleRotateAndTranslate()
        {
            var root = _model.transform;
            root.SetPositionAndRotation(new Vector3(3, 2, -5), Quaternion.Euler(30, 120, 10));
            root.localScale = Vector3.one * 0.02f;
            var a = root.TransformPoint(Vector3.zero); var b = root.TransformPoint(new Vector3(0, 0, 0.125f));
            Assert.AreEqual(125f, InspectionGeometry.DistanceMm(root, a, b), 0.03f);
            var measurement = _model.AddComponent<MeasurementView>();
            measurement.Initialize(null, null); measurement.Begin(); measurement.Pick(a); measurement.Pick(b);
            Assert.IsFalse(measurement.Measuring);
            Assert.AreEqual(125f, measurement.DistanceMm.Value, 0.03f);
            Assert.IsTrue(measurement.Pin()); Assert.AreEqual(1, measurement.PinnedCount);
            measurement.Begin(); measurement.Cancel(); Assert.AreEqual(1, measurement.PinnedCount);
            measurement.ClearAll(); Assert.AreEqual(0, measurement.PinnedCount);
        }
        [Test]
        public void ExplicitScalePreservesCenterAndRestoresOneToOne()
        {
            var bounds = new Bounds(new Vector3(3, 1, 0), new Vector3(10, 4, 2));
            _model.transform.rotation = Quaternion.Euler(0, 70, 0);
            var center = _model.transform.TransformPoint(bounds.center);
            Assert.AreEqual(1, InspectionGeometry.Scale(bounds, ModelScaleMode.OneToOne));
            InspectionGeometry.ApplyScale(_model.transform, bounds, ModelScaleMode.Table, 2);
            Assert.AreEqual(0.06f, _model.transform.localScale.x, 1e-6f);
            Assert.Less(Vector3.Distance(center, _model.transform.TransformPoint(bounds.center)), 1e-5f);
            InspectionGeometry.ApplyScale(_model.transform, bounds, ModelScaleMode.OneToOne, 2);
            Assert.AreEqual(Vector3.one, _model.transform.localScale);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => InspectionGeometry.Scale(bounds, ModelScaleMode.FitToRoom, float.NaN));
        }
        [Test]
        public void SectionNumericAndPhysicalPoseUseTheSamePlane()
        {
            _model.transform.localScale = Vector3.one * 0.5f;
            var section = new GameObject("section").AddComponent<SectionPlane>();
            section.Initialize(_model.transform, null); section.ResetPlane(new Bounds(Vector3.zero, Vector3.one));
            section.SetNumeric(100, 90); section.SetActive(true);
            Assert.IsTrue(section.IsClipped(new Vector3(0.1f, 0, 0)));
            Assert.IsFalse(section.IsClipped(Vector3.zero));
            Assert.AreEqual(0.05f, section.transform.position.x, 1e-5f);
            Assert.IsTrue(section.HitHandle(new Ray(new Vector3(1,0,0), Vector3.left), out _));
            section.SetWorldPose(new Vector3(0,0,0.2f), Quaternion.identity);
            Assert.AreEqual(400f, section.OffsetMm, 1e-4f);
            section.SetActive(false); Assert.IsFalse(section.IsClipped(Vector3.one));
        }
        [Test]
        public void ModelBoundsExcludeMeasurementLabelsAndSectionHandles()
        {
            var view = _model.AddComponent<CadSceneView>(); view.Show(CadSceneViewTests.BoltScene());
            var before = ScenePlacement.LocalBounds(_model.transform);
            _model.transform.rotation = Quaternion.Euler(35, 65, 20);
            var section = new GameObject("section").AddComponent<SectionPlane>();
            section.Initialize(_model.transform, null); section.ResetPlane(before); section.SetNumeric(100000, 0); section.SetActive(true);
            var after = ScenePlacement.LocalBounds(_model.transform);
            Assert.Less(Vector3.Distance(before.center, after.center), 1e-5f);
            Assert.Less(Vector3.Distance(before.size, after.size), 1e-5f);
        }
    }
}
