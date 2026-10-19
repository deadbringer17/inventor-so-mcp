using System.Collections;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InventorXrSo.Tests
{
    public sealed class NativeSketchTextLayerTests
    {
        private GameObject _playRoot;
        private static NativeSketchSnapshot Snapshot()
        {
            return NativeSketchSnapshot.Parse(new JObject{["schema_version"]=1,["document_id"]="doc",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Glyphs",["partial"]=false,
                ["frame"]=new JObject{["origin_mm"]=new JArray(200,300,400),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,0,1)},
                ["entities"]=new JArray(new JObject{["id"]="ent_text",["type"]="text",["supported"]=true,["text"]="Native",["text_mm"]=new JArray(10,20),
                    ["text_glyphs"]=new JObject{["schema"]=1,["coordinate_space"]="model_mm",["vertices_mm"]=new JArray(30,40,50,40,40,50,30,50,50),["indices"]=new JArray(0,1,2),["color_rgb"]=new JArray(255,0,0)}}),
                ["dimensions"]=new JArray(),["constraints"]=new JArray()},new DocumentState("doc","r1","v"),"ent_sketch");
        }
        [Test]
        public void NativeVerticesUseModelCoordinatesOnceAndRepeatedSnapshotRetainsOwnedMesh()
        {
            var root=new GameObject("text mesh test");var layer=NativeSketchTextLayer.Create(root.transform);var snapshot=Snapshot();
            try
            {
                layer.Synchronize(snapshot);var filter=layer.GetComponentInChildren<MeshFilter>();var mesh=filter.sharedMesh;
                Assert.That(Vector3.Distance(new Vector3(-.03f,.04f,.05f),mesh.vertices[0]),Is.LessThan(1e-8f));Assert.AreEqual(1,layer.MeshCount);
                CollectionAssert.AreEqual(new[]{0,1,2},mesh.triangles);Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
                layer.Synchronize(snapshot);Assert.AreSame(mesh,layer.GetComponentInChildren<MeshFilter>().sharedMesh);
                var renderer=filter.GetComponent<MeshRenderer>();var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
                Assert.AreEqual(Color.red,properties.GetColor("_BaseColor"));
                var material=renderer.sharedMaterial;layer.Clear();Assert.Zero(layer.MeshCount);Assert.True(mesh==null);Assert.True(material==null);
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void NativeViewRendersGlyphMeshWithoutReplacingItWithTmpText()
        {
            var root=new GameObject("native text view integration");var view=root.AddComponent<DesignGeometryView>();view.Initialize(null);
            try
            {
                var snapshot=Snapshot();view.ShowNative(snapshot);
                Assert.AreEqual(1,root.GetComponentsInChildren<MeshFilter>().Length);
                Assert.IsEmpty(root.GetComponentsInChildren<TMPro.TMP_Text>());
                var mesh=root.GetComponentInChildren<MeshFilter>().sharedMesh;
                view.ShowNative(snapshot);Assert.AreSame(mesh,root.GetComponentInChildren<MeshFilter>().sharedMesh);
                view.Clear();Assert.True(mesh==null);
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void NativeTextShaderProducesOpaqueNativeColorOnGpu()
        {
            var material=new Material(Shader.Find("InventorXrSo/NativeSketchText"));material.SetColor("_BaseColor",Color.red);
            var target=new RenderTexture(8,8,0,RenderTextureFormat.ARGB32);var pixels=new Texture2D(8,8,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;
            try
            {
                target.Create();Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,8,8),0,0);pixels.Apply();var color=pixels.GetPixel(4,4);
                Assert.That(color.r,Is.EqualTo(1).Within(.01));Assert.That(color.g,Is.LessThan(.01));Assert.That(color.b,Is.LessThan(.01));Assert.That(color.a,Is.EqualTo(1).Within(.01));
            }
            finally{RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(material);}
        }
        [UnityTest]
        public IEnumerator RuntimeDisableAndDestroyReleaseNativeTextResources()
        {
            yield return new EnterPlayMode();
            _playRoot=new GameObject("runtime text lifecycle");var layer=NativeSketchTextLayer.Create(_playRoot.transform);
            layer.Synchronize(Snapshot());var filter=layer.GetComponentInChildren<MeshFilter>();var mesh=filter.sharedMesh;var material=filter.GetComponent<MeshRenderer>().sharedMaterial;
            layer.gameObject.SetActive(false);Assert.Zero(layer.MeshCount);yield return null;
            Assert.True(mesh==null);Assert.True(material==null);
            layer.gameObject.SetActive(true);layer.Synchronize(Snapshot());filter=layer.GetComponentInChildren<MeshFilter>();mesh=filter.sharedMesh;material=filter.GetComponent<MeshRenderer>().sharedMaterial;
            Object.Destroy(layer.gameObject);yield return null;Assert.True(mesh==null);Assert.True(material==null);Assert.Zero(_playRoot.transform.childCount);
            Object.Destroy(_playRoot);yield return null;_playRoot=null;yield return new ExitPlayMode();
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(_playRoot!=null){if(Application.isPlaying){Object.Destroy(_playRoot);yield return null;}else Object.DestroyImmediate(_playRoot);_playRoot=null;}
            if(EditorApplication.isPlaying)yield return new ExitPlayMode();
        }
    }
}
