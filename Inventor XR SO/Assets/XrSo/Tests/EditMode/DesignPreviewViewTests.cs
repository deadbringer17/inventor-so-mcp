using System;
using System.Collections.Generic;
using System.IO;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class DesignPreviewViewTests
    {
        private GameObject _root;
        private Material _material;
        [TearDown] public void Cleanup() { if (_root != null) Object.DestroyImmediate(_root); if (_material != null) Object.DestroyImmediate(_material); }

        private (CadSceneView view, DesignPreviewView preview, GlbModel model, LoadedScene scene) Setup()
        {
            _root = new GameObject("Design test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            var view = _root.AddComponent<CadSceneView>(); view.BodyMaterial = _material;
            var model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture));
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Bolt"",""definition_kind"":""part"",""definition_document_id"":""doc_bolt"",""children"":[]}}"));
            var scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { ["doc_bolt"] = model },
                new Dictionary<string, string> { ["doc_bolt"] = "a_test" }, new List<string>());
            view.Show(scene);
            var preview = _root.AddComponent<DesignPreviewView>(); preview.Initialize(view);
            return (view, preview, model, scene);
        }
        private static DesignPreview Plan(GlbModel model) => new DesignPreview("p", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), model);

        [Test]
        public void NativeDimensionTextUsesSketchFrameAndRemainsReadableAtTableScale()
        {
            var (view,preview,model,_) = Setup();
            view.transform.localScale=Vector3.one*0.1f;
            var frame=new SketchFrame(new CadPoint(10,20,30),new CadPoint(0,1,0),new CadPoint(0,0,1));
            var snapshot=new SketchSnapshot("XR",frame,true,Array.Empty<SketchElement>(),dimensions:new[] {
                new SketchDimension("d0","12 mm",new CadPoint(5,7),false) });
            preview.Show(new DesignPreview("dimension","doc_bolt","r",DateTimeOffset.UtcNow.AddMinutes(1),model,new[] { snapshot }));
            var labels=view.GetComponentsInChildren<SketchDimensionLabel>(); Assert.AreEqual(1,labels.Length);
            Assert.That(Vector3.Distance(labels[0].transform.localPosition,CadCoordinates.ToLocal(new CadPoint(10,25,37))),Is.LessThan(0.000001));
            Assert.That(labels[0].transform.lossyScale.x,Is.EqualTo(0.001f).Within(0.000001));
            var text=labels[0].GetComponentInChildren<TMPro.TextMeshProUGUI>();
            Assert.AreEqual("d0 = 12 mm",text.text); Assert.False(text.raycastTarget);
            Assert.GreaterOrEqual(InventorXrSo.Unity.Ui.UiTypography.CapHeight(text) * text.transform.lossyScale.x * 1000, 14f);
            view.transform.localScale=Vector3.one; labels[0].Refresh();
            Assert.That(labels[0].transform.lossyScale.x,Is.EqualTo(0.001f).Within(0.000001));
            preview.Clear(); Assert.AreEqual(0,view.GetComponentsInChildren<SketchDimensionLabel>().Length);
        }

        [Test]
        public void PreviewSharesModelTransformAndHasNoSelectableTransientGeometry()
        {
            var (view, preview, model, _) = Setup();
            view.transform.SetPositionAndRotation(new Vector3(1,2,3), Quaternion.Euler(0,45,0));
            view.transform.localScale = Vector3.one * 0.2f;
            preview.Show(Plan(model));
            var result = view.transform.Find("Design Preview");
            Assert.AreEqual(Vector3.zero, result.localPosition); Assert.AreEqual(Vector3.one, result.localScale);
            Assert.AreEqual(0, result.GetComponentsInChildren<Collider>().Length);
            Assert.AreEqual(0, result.GetComponentsInChildren<CadBody>().Length);
            Assert.AreEqual(model.Primitives.Count, result.GetComponentsInChildren<MeshFilter>().Length);
            Assert.AreNotSame(_material, view.Instances[0].Bodies[0].Renderer.sharedMaterial);
            Assert.IsTrue(preview.IsShowing); Assert.AreEqual("p", preview.PlanId);
        }

        [Test]
        public void CancelRestoresOriginalMaterialsAndDestroysOwnedMeshes()
        {
            var (view, preview, model, _) = Setup(); preview.Show(Plan(model));
            var mesh = view.transform.Find("Design Preview").GetComponentInChildren<MeshFilter>().sharedMesh;
            preview.Clear();
            Assert.IsFalse(preview.IsShowing); Assert.IsTrue(mesh == null);
            Assert.AreSame(_material, view.Instances[0].Bodies[0].Renderer.sharedMaterial);
            Assert.IsNull(view.transform.Find("Design Preview"));
        }

        [Test]
        public void DocumentRefreshClearsGhostAndDoesNotReuseAbortedTopology()
        {
            var (view, preview, model, scene) = Setup(); preview.Show(Plan(model));
            view.Show(scene);
            Assert.IsFalse(preview.IsShowing); Assert.IsNull(preview.PlanId);
            Assert.AreSame(_material, view.Instances[0].Bodies[0].Renderer.sharedMaterial);
        }

        [Test]
        public void CannotOverlayDefinitionPreviewOntoAssemblyOccurrences()
        {
            var (view, preview, model, _) = Setup(); view.Show(CadSceneViewTests.BoltScene());
            Assert.Throws<InvalidOperationException>(() => preview.Show(Plan(model)));
            Assert.IsFalse(preview.IsShowing);
        }
    }
}
