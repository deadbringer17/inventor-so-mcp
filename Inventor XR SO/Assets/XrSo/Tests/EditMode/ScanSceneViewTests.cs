using System.Linq;
using InventorXrSo.Core.Capture;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr.Capture;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public sealed class ScanSceneViewTests
    {
        private GameObject _root;
        private ScanSceneView _view;
        private ScanReview _review;
        [SetUp] public void Setup()
        {_root=new GameObject("RC3 synthetic fixture");_view=_root.AddComponent<ScanSceneView>();_review=ScanReviewFixture.Create();_view.Load(_review,new Vector3(1,2,3),Quaternion.Euler(0,30,0));}
        [TearDown] public void Cleanup() {Object.DestroyImmediate(_root);}
        [Test] public void MeshAndPointCoordinatesAgreeAfterMetreHandednessConversionAndDisplayScaling()
        {
            var mesh=_root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Estimated sparse surface").sharedMesh;
            for(int i=0;i<3;i++) Assert.That(Vector3.Distance(mesh.vertices[i],_view.DisplayPoint(_review.Points[i].Position)),Is.LessThan(1e-6));
            Assert.AreEqual(new[]{0,1,2},mesh.triangles);Assert.AreEqual(1000,ScanGeometry.Distance(_review.Points[0].Position,_review.Points[1].Position));
            Assert.AreEqual(0,_root.GetComponentsInChildren<CadBody>().Length);Assert.AreEqual(0,_root.GetComponentsInChildren<Collider>().Length);
        }
        [Test] public void RayPickUsesOriginalPointIdsAndRegionSelectionUsesMillimetres()
        {
            Vector3 target=_root.transform.TransformPoint(_view.DisplayPoint(_review.Points[0].Position));
            Assert.IsTrue(_view.Pick(new Ray(target-Vector3.forward,Vector3.forward)));Assert.AreEqual(new[]{"p0"},_view.Selected);
            _view.SelectRegion(100);Assert.AreEqual(new[]{"p0"},_view.Selected);
            _view.SelectRegion(1001);Assert.That(_view.Selected,Is.EquivalentTo(new[]{"p0","p1"}));
            _view.ClearSelection();Assert.IsEmpty(_view.Selected);
        }
        [Test] public void WeakPointSelectionDoesNotClaimSurfaceCoverageAndPrimitiveSupportRemainsRecognizable()
        {
            _view.SelectWeak();Assert.That(_view.Selected,Is.EquivalentTo(new[]{"p0","p2"}));
            var primitive=ScanGeometry.Fit("plane","plane",_review.Points);var annotations=(JObject)_review.Annotations.DeepClone();annotations["primitives"]=JArray.FromObject(new[]{primitive});
            _review.ApplyAnnotations(annotations);_view.Redraw();_view.SelectIds(primitive.SupportIds);Assert.AreEqual(3,_view.Selected.Count);
            Assert.Null(primitive.Uncertainty);Assert.AreEqual("estimated",primitive.Status);
        }
        [Test] public void ReloadAndClearDropSelectionAndDestroyOwnedMeshes()
        {
            _view.SelectAll();var meshes=_root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
            _view.Load(_review,Vector3.zero,Quaternion.identity);Assert.IsEmpty(_view.Selected);Assert.IsTrue(meshes.All(m=>m==null));
            _view.Clear();Assert.IsFalse(_root.activeSelf);Assert.AreEqual(0,_root.GetComponentsInChildren<MeshFilter>(true).Length);
        }
    }
}
