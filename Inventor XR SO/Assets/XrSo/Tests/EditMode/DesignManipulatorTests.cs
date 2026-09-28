using System.Reflection;
using InventorXrSo.Core.Backend;
using InventorXrSo.Xr;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class DesignManipulatorTests
    {
        [TestCase("fillet")][TestCase("chamfer")]
        public void EdgeManipulatorTracksLengthMidpointOfSelectedOffsetGeometry(string feature)
        {
            var root=new GameObject("Offset edge handle");
            try
            {
                var workspace=root.AddComponent<DesignWorkspace>();
                var state=new DocumentState("d","r","v");
                var context=DesignContext.Parse(JObject.Parse(@"{""document_id"":""d"",""revision"":""r"",""kind"":""part"",
                    ""edges"":[{""id"":""ent_edge"",""kind"":""line"",""points_mm"":[1000,2000,3000,1002,2000,3000,1010,2000,3000]}]}"),state);
                Set(workspace,"_context",context); Set(workspace,"_feature",feature);
                Set(workspace,"_facePoint",new CadPoint(-1,-1,-1));
                var selected=(System.Collections.Generic.HashSet<string>)typeof(DesignWorkspace)
                    .GetField("_edges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);
                var target=typeof(DesignWorkspace).GetProperty("HasDimensionTarget",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.False((bool)target.GetValue(workspace)); selected.Add("ent_edge");
                Assert.True((bool)target.GetValue(workspace));
                var origin=Get(workspace,"DimensionOrigin");
                Assert.AreEqual(1005,origin.X); Assert.AreEqual(2000,origin.Y); Assert.AreEqual(3000,origin.Z);
                selected.Clear(); Assert.False((bool)target.GetValue(workspace));
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void EdgePickingRejectsOccludedCadButLeavesVirtualHandleUnrestricted()
        {
            var root=new GameObject("Edge visibility test");
            var mesh=new Mesh { vertices=new[] {new Vector3(-1,-1,1),new Vector3(0,1,1),new Vector3(1,-1,1)},triangles=new[]{0,1,2} };
            try
            {
                var blocker=new GameObject("CAD front surface"); blocker.transform.SetParent(root.transform);
                blocker.AddComponent<MeshCollider>().sharedMesh=mesh;
                var body=blocker.AddComponent<CadBody>(); Physics.SyncTransforms();
                var front=new DesignEdge("front","line",new[]{new CadPoint(-5,-100,1000),new CadPoint(-5,100,1000)});
                var back=new DesignEdge("back","line",new[]{new CadPoint(0,-100,2000),new CadPoint(0,100,2000)});
                var ray=new Ray(Vector3.zero,Vector3.forward);
                Assert.AreEqual("back",CadCoordinates.PickEdge(root.transform,new[]{front,back},ray));
                Assert.AreEqual("front",CadCoordinates.PickEdge(root.transform,new[]{front,back},ray,requireVisible:true));
                Assert.IsNull(CadCoordinates.PickEdge(root.transform,new[]{back},ray,requireVisible:true));
                Object.DestroyImmediate(body);
                Assert.AreEqual("back",CadCoordinates.PickEdge(root.transform,new[]{back},ray,requireVisible:true),"Non-CAD colliders do not hide selectable edges.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }
        [Test]
        public void ExistingSketchExtrusionUsesItsOffsetFrameAndReversesWithDirection()
        {
            var root = new GameObject("Manipulator test");
            try
            {
                var workspace = root.AddComponent<DesignWorkspace>();
                var state = DocumentState.FromJson(JObject.Parse(@"{""document_id"":""d"",""revision"":""r"",""visual_revision"":""v""}"));
                var context = DesignContext.Parse(JObject.Parse(@"{""document_id"":""d"",""revision"":""r"",""kind"":""part"",
                    ""sketches"":[{""name"":""Offset"",""origin_mm"":[10,20,30],""x_axis"":[0,1,0],""y_axis"":[0,0,1]}],
                    ""faces"":[{""id"":""ent_face"",""point_mm"":[0,0,10],""normal"":[0,0,1]}]}"), state);
                Set(workspace,"_context",context); Set(workspace,"_existingSketch","Offset"); Set(workspace,"_feature","extrude");
                var origin = Get(workspace,"DimensionOrigin"); var axis = Get(workspace,"DimensionAxis");
                Assert.AreEqual(10,origin.X); Assert.AreEqual(20,origin.Y); Assert.AreEqual(30,origin.Z);
                Assert.AreEqual(1,axis.X); Assert.AreEqual(0,axis.Y); Assert.AreEqual(0,axis.Z);
                Set(workspace,"_negative",true); Assert.AreEqual(-1,Get(workspace,"DimensionAxis").X);
                Set(workspace,"_symmetric",true); Set(workspace,"_dimension",10d);
                Assert.AreEqual(5,Get(workspace,"DimensionStart").X); Assert.AreEqual(15,Get(workspace,"DimensionEnd").X);
                Set(workspace,"_dragStart",new CadPoint(15,20,30)); Set(workspace,"_dragValue",10d);
                double resized=(double)typeof(DesignWorkspace).GetMethod("DragDimension",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(workspace,new object[]{new CadPoint(17,20,30)});
                Assert.AreEqual(14,resized,"Moving one symmetric end by 2 mm changes total distance by 4 mm.");
                Set(workspace,"_dragSide",-1d); Set(workspace,"_dragStart",new CadPoint(5,20,30));
                resized=(double)typeof(DesignWorkspace).GetMethod("DragDimension",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(workspace,new object[]{new CadPoint(3,20,30)});
                Assert.AreEqual(14,resized,"Pulling the negative end outward also increases total distance.");
                Set(workspace,"_feature","hole"); Set(workspace,"_face","ent_face");
                Set(workspace,"_facePoint",new CadPoint(4,5,10));
                Assert.AreEqual(-1,Get(workspace,"DimensionAxis").Z);
                Assert.AreEqual(4,Get(workspace,"DimensionOrigin").X);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void Set(object target,string name,object value) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static CadPoint Get(object target,string name) => (CadPoint)target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    }
}
