using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class NativeSketchGeometryTests
    {
        private GameObject _model;
        [SetUp] public void Setup() => _model = new GameObject("native model");
        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(_model);
        private static NativeSketchSnapshot Snapshot(bool constraint=false) => NativeSketchSnapshot.Parse(new JObject
        {
            ["schema_version"] = 1, ["document_id"] = "doc", ["revision"] = "r1", ["sketch_id"] = "ent_sketch",
            ["sketch_name"] = "Native", ["partial"] = false,
            ["frame"] = new JObject { ["origin_mm"] = new JArray(10,20,30), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) },
            ["entities"] = new JArray(
                new JObject { ["type"] = "control_point_spline", ["id"] = "ent_spline", ["supported"] = true, ["strokes_mm"] = new JArray(0,0,5,8,10,0),["start_mm"]=new JArray(0,0),["end_mm"]=new JArray(10,0) },
                new JObject { ["type"] = "point", ["id"] = "ent_point", ["supported"] = true, ["point_mm"] = new JArray(12,4) }),
            ["dimensions"] = new JArray(), ["constraints"] = constraint ? new JArray(new JObject {["id"]="ent_constraint",["type"]="constraint",["native_type"]="kGroundConstraintObject",
                ["supported"]=true,["binding_state"]="complete",["annotation_mm"]=new JArray(0,0),["related_entities"]=new JArray(new JObject {["entity_id"]="ent_spline",["type"]="control_point_spline",["subpart"]="Entity"})}) : new JArray()
        },new DocumentState("doc","r1","v1"),"ent_sketch");

        [TestCase(270)][TestCase(-270)]
        public void ArcGuideUsesNativeFrameSignedSweepAndPersistentRenderer(double sweep)
        {
            var view=_model.AddComponent<DesignGeometryView>();view.Initialize(null);var snapshot=Snapshot();view.ShowNative(snapshot);
            var spline=_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline");
            var cursor=_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="Sketch cursor");
            view.ArcCursor(snapshot.Frame,new CadPoint(3,4),10,0,sweep);
            Assert.AreEqual(55,cursor.positionCount);
            Assert.Less(Vector3.Distance(CadCoordinates.ToLocal(snapshot.Frame.ToModel(new CadPoint(13,4))),cursor.GetPosition(0)),.000001f);
            Assert.Less(Vector3.Distance(CadCoordinates.ToLocal(snapshot.Frame.ToModel(new CadPoint(3,sweep>0 ? -6 : 14))),cursor.GetPosition(54)),.000001f);
            view.ArcCursor(snapshot.Frame,new CadPoint(3,4),10,20,0);Assert.AreEqual(2,cursor.positionCount);
            Assert.AreSame(cursor,_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="Sketch cursor"));
            Assert.AreSame(spline,_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline"));
            view.Clear();Assert.Zero(cursor.positionCount);
        }
        [Test]
        public void SelectionWindowUsesSketchCoordinatesAndIndependentMaterialColourWithoutRebuildingGeometry()
        {
            var material=new Material(Shader.Find("XrSo/CadSurface"));
            try
            {
                var view=_model.AddComponent<DesignGeometryView>();view.Initialize(material);var snapshot=Snapshot();view.ShowNative(snapshot);
                var spline=_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline");
                var outline=_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="Native selection window");
                var original=material.GetColor("_BaseColor");
                view.SelectionWindow(snapshot.Frame,new CadPoint(-1,-2),new CadPoint(11,8),false);
                Assert.AreEqual(5,outline.positionCount);Assert.AreEqual(outline.GetPosition(0),outline.GetPosition(4));
                Assert.AreEqual(CadCoordinates.ToLocal(snapshot.Frame.ToModel(new CadPoint(11,-2))),outline.GetPosition(1));
                var properties=new MaterialPropertyBlock();outline.GetPropertyBlock(properties);
                Assert.Less(Vector4.Distance(new Vector4(.3f,.65f,1,1),properties.GetColor("_BaseColor")),.00001f);
                view.SelectionWindow(snapshot.Frame,new CadPoint(11,8),new CadPoint(-1,-2),true);outline.GetPropertyBlock(properties);
                Assert.Less(Vector4.Distance(new Vector4(.2f,1,.4f,1),properties.GetColor("_BaseColor")),.00001f);Assert.AreEqual(original,material.GetColor("_BaseColor"));
                Assert.AreSame(spline,_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline"));
                view.Clear();Assert.Zero(outline.positionCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }
        [Test]
        public void ConstraintSelectionHighlightsBoundGeometryAndKeepsSelectedAnnotationVisibleWhenOthersAreHidden()
        {
            var view=_model.AddComponent<DesignGeometryView>();view.Initialize(null);var snapshot=Snapshot(true);
            view.ShowNative(snapshot,showConstraints:false);
            Assert.False(_model.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="Fissaggio"));
            view.ShowNative(snapshot,selected:new[] {snapshot.Constraints.Single().Reference},showConstraints:false);
            Assert.AreEqual(.003f,_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline").widthMultiplier);
            Assert.AreEqual(2,_model.GetComponentsInChildren<LineRenderer>().Count(l=>l.name.StartsWith("Selected native constraint")));
            Assert.True(_model.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="Fissaggio"));
            var stale=new SketchEntityRef("doc","r2","ent_sketch","ent_constraint","constraint");
            Assert.Throws<InvalidOperationException>(()=>view.ShowNative(snapshot,selected:new[] {stale}));
        }
        [Test]
        public void EndpointSelectionMarksOnlyTheNativeEndpoint()
        {
            var view=_model.AddComponent<DesignGeometryView>(); view.Initialize(null); var snapshot=Snapshot();
            view.ShowNative(snapshot,selected:new[] {snapshot.Entities[0].Reference.WithSubpart(SketchSubpart.End)});
            var curve=_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline");
            Assert.AreEqual(.0012f,curve.widthMultiplier);
            var markers=_model.GetComponentsInChildren<LineRenderer>().Where(l=>l.name.StartsWith("Native selected End")).ToArray();
            Assert.AreEqual(2,markers.Length); Assert.True(markers.All(l=>l.widthMultiplier==.003f));
            Assert.Throws<InvalidOperationException>(()=>view.ShowNative(snapshot,selected:new[] {snapshot.Entities[0].Reference.WithSubpart(SketchSubpart.Center)}));
        }
        [Test]
        public void ImageBoundaryCannotBeDirectlySelectedButItsNativeConstraintRemainsReadable()
        {
            var baseline=Snapshot();var structural=baseline.Entities[1].Data;
            structural["selection_state"]="image_boundary";structural["structural_owner_image_id"]="ent_image";
            var constraint=new JObject { ["id"]="ent_constraint",["type"]="constraint",["native_type"]="kGroundConstraintObject",
                ["supported"]=true,["binding_state"]="complete",["related_entities"]=new JArray(new JObject
                { ["entity_id"]="ent_point",["type"]="point",["subpart"]="Point" }) };
            var snapshot=NativeSketchSnapshot.Parse(new JObject
            {
                ["schema_version"]=1,["document_id"]="doc",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Native",
                ["partial"]=true,["frame"]=new JObject { ["origin_mm"]=new JArray(10,20,30),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0) },
                ["entities"]=new JArray(baseline.Entities[0].Data,structural,new JObject
                { ["id"]="ent_image",["type"]="image",["supported"]=false,["selection_state"]="read_only_image" }),
                ["dimensions"]=new JArray(),["constraints"]=new JArray(constraint)
            },baseline.State,baseline.SketchId);
            var view=_model.AddComponent<DesignGeometryView>();view.Initialize(null);
            Assert.Throws<InvalidOperationException>(()=>view.ShowNative(snapshot,selected:new[]{snapshot.Entities[1].Reference}));
            Assert.Throws<InvalidOperationException>(()=>view.ShowNative(snapshot,selected:new[]{snapshot.Entities[1].Reference.WithSubpart(SketchSubpart.Point)}));
            view.ShowNative(snapshot,selected:new[]{snapshot.Entities[0].Reference});
            Assert.AreEqual(.003f,_model.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="control_point_spline").widthMultiplier);
            view.ShowNative(snapshot,selected:new[]{snapshot.Constraints.Single().Reference});
            Assert.True(view.ConstraintLayout.Entries.Single().AssociationsAvailable);
            Assert.AreEqual(NativeSketchConstraintAnchorSource.RelatedSubpart,view.ConstraintLayout.Entries.Single().AnchorSource);
            Assert.AreEqual("ent_point",view.ConstraintLayout.Entries.Single().RelatedEntities.Single().EntityId);
            Assert.True(snapshot.Partial);
        }
        [Test]
        public void NativeSplineUsesActualStrokesAndSelectionWidthAtModelCoordinates()
        {
            _model.transform.SetPositionAndRotation(new Vector3(3,4,5),Quaternion.Euler(30,40,50));
            _model.transform.localScale = Vector3.one * .5f;
            var view = _model.AddComponent<DesignGeometryView>(); view.Initialize(null);
            var snapshot = Snapshot(); view.ShowNative(snapshot,selected:new[] {snapshot.Entities[0].Reference});
            var spline = _model.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "control_point_spline");
            Assert.AreEqual(3,spline.positionCount); Assert.AreEqual(.003f,spline.widthMultiplier);
            Assert.Less(Vector3.Distance(CadCoordinates.ToLocal(new CadPoint(15,28,30)),spline.GetPosition(1)),1e-6f);
            Assert.AreEqual(2,_model.GetComponentsInChildren<LineRenderer>().Count(l => l.name.StartsWith("Native point")));
            var stale = new SketchEntityRef("doc","r2","ent_sketch","ent_spline","control_point_spline");
            Assert.Throws<InvalidOperationException>(() => view.ShowNative(snapshot,selected:new[] {stale}));
            Assert.AreEqual(3,spline.positionCount);
            Assert.IsEmpty(_model.GetComponentsInChildren<Collider>());
        }
    }
}
