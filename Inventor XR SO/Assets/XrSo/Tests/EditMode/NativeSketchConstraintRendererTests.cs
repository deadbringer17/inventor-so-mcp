using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class NativeSketchConstraintRendererTests
    {
        private GameObject _root;
        private Material _material;
        private DesignGeometryView _view;
        [SetUp] public void Setup()
        {
            _root=new GameObject("constraint integration");
            _material=new Material(Shader.Find("Sprites/Default"));
            _view=_root.AddComponent<DesignGeometryView>();_view.Initialize(_material);
        }
        [TearDown] public void Cleanup()
        { Object.DestroyImmediate(_root);Object.DestroyImmediate(_material); }
        private static NativeSketchSnapshot Snapshot(bool foreignBinding=false,bool annotation=true)
        {
            var baseline=NativeSketchTestData.Snapshot(new DocumentState("part","r1","v1"),"ent_sketch",constraints:true);
            var constraint=baseline.Constraints[0].Data;
            if(foreignBinding) constraint["related_entities"][0]["revision"]="foreign";
            if(!annotation) constraint.Remove("annotation_mm");
            return NativeSketchSnapshot.Parse(new JObject
            {
                ["schema_version"]=1,["document_id"]="part",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Native",
                ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
                ["entities"]=new JArray(baseline.Entities.Select(e=>e.Data)),["dimensions"]=new JArray(),["constraints"]=new JArray(constraint),["partial"]=false
            },baseline.State,baseline.SketchId);
        }
        [TestCase(false)][TestCase(true)]
        public void HighlightUsesOnlyValidatedAssociationsAndNativeAnnotationRemainsIndependent(bool foreignBinding)
        {
            var snapshot=Snapshot(foreignBinding);_view.ShowNative(snapshot,selected:new[] {snapshot.Constraints[0].Reference});
            Assert.AreEqual(!foreignBinding,_view.ConstraintLayout.Entries.Single().AssociationsAvailable);
            var line=_view.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="line");
            Assert.AreEqual(foreignBinding ? .0012f : .003f,line.widthMultiplier);
            Assert.AreEqual(1,_view.GetComponentInChildren<NativeSketchConstraintLayer>().VisibleCount);
        }
        [Test]
        public void MissingNativeAnnotationUsesValidatedAnchorAndLabelsSurviveOrdinaryRenders()
        {
            var snapshot=Snapshot(annotation:false);_view.ShowNative(snapshot);
            var layer=_view.GetComponentInChildren<NativeSketchConstraintLayer>();
            Assert.AreEqual(NativeSketchConstraintAnchorSource.RelatedEntityAnchor,_view.ConstraintLayout.Entries.Single().AnchorSource);
            Assert.True(layer.TryGetLabel("ent_constraint",out var label));
            _view.ShowNative(snapshot,selected:new[] {snapshot.Constraints[0].Reference});
            Assert.True(layer.TryGetLabel("ent_constraint",out var reused));Assert.AreSame(label,reused);
            Assert.AreEqual(1,layer.CreatedLabelCount);
            _view.Clear();Assert.IsNull(_view.ConstraintLayout);
            Assert.IsEmpty(_view.PickConstraintLabels(snapshot,new Ray(Vector3.forward,Vector3.back)));
        }
    }
}
