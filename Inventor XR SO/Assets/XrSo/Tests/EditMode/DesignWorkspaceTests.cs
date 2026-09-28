using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Selection;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class DesignWorkspaceTests
    {
        private GameObject _root;
        private Material _material;
        private DesignWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private LoadedScene _scene;
        private HomePanel Panel => _workspace.GetComponentInChildren<HomePanel>(true);
        private sealed class Backend : IDesignWorkspaceBackend, IDesignHistoryBackend
        {
            public DocumentState State = new DocumentState("doc_bolt","r","v");
            public GlbModel Model;
            public int Previews, Commits, HistoryCalls;
            public int StateReads;
            public string PreviewPlane;
            public Exception PreviewError;
            public TaskCompletionSource<DocumentState> PendingMutation;
            public TaskCompletionSource<DesignPreview> PendingPreview;
            public Task<DesignContext> GetDesignContextAsync(DocumentState state,CancellationToken ct) => Task.FromResult(
                DesignContext.Parse(new JObject { ["document_id"]=state.DocumentId,["revision"]=state.Revision,["kind"]="part",
                    ["planes"]=JArray.Parse(@"[{""reference"":""3"",""name"":""XY"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]}]"),
                    ["faces"]=JArray.Parse(@"[{""id"":""ent_face"",""point_mm"":[0,0,10],""normal"":[0,0,1]}]"),
                    ["edges"]=JArray.Parse(@"[{""id"":""ent_edge"",""kind"":""line"",""points_mm"":[1000,2000,3000,1010,2000,3000]}]"),
                    ["sketches"]=JArray.Parse(@"[{""name"":""Existing"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]}]"),
                    ["sketch_snapshots"]=JArray.Parse(@"[{""sketch_name"":""Existing"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},""entities"":[{""type"":""circle"",""center_mm"":[20,20],""radius_mm"":4}]}]") },state));
            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) { StateReads++; return Task.FromResult(State); }
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
            {
                Previews++;
                if (PreviewError != null) return Task.FromException<DesignPreview>(PreviewError);
                PreviewPlane=(string)operations.FirstOrDefault(o=>(string)o["command"]=="create_sketch")?["arguments"]?["plane"];
                var name=(string)operations.FirstOrDefault(o=>(string)o["command"]=="create_sketch")?["arguments"]?["name"];
                var sketch=new SketchSnapshot(name,new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(0,1,0)),true,
                    Array.Empty<SketchElement>(),constraints:new[]{"kHorizontalConstraintObject","kHorizontalConstraintObject","kVerticalConstraintObject"});
                return PendingPreview?.Task ?? Task.FromResult(new DesignPreview("p"+Previews,state.DocumentId,state.Revision,DateTimeOffset.UtcNow.AddMinutes(1),Model,
                    name==null ? Array.Empty<SketchSnapshot>() : new[]{sketch}));
            }
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct)
            { Commits++; State=new DocumentState("doc_bolt","r2","v2"); return PendingMutation?.Task ?? Task.FromResult(State); }
            public Task<DesignHistory> GetHistoryAsync(DocumentState state,CancellationToken ct) => Task.FromResult(new DesignHistory(state,true,true,"ticket"));
            public Task<DocumentState> ApplyHistoryAsync(DesignHistory history,bool redo,CancellationToken ct)
            { HistoryCalls++; State=new DocumentState("doc_bolt","r3","v3"); return PendingMutation?.Task ?? Task.FromResult(State); }
        }
        [SetUp] public void Setup()
        {
            _root=new GameObject("Design UI test");
            _material=new Material(Shader.Find("XrSo/CadSurface"));
            var eye=Child("Eye").AddComponent<Camera>();
            var hand=Child("Hand"); var ray=hand.AddComponent<ControllerRay>();
            var line=hand.AddComponent<LineRenderer>(); line.sharedMaterial=_material; ray.Configure(hand.transform,line);
            _view=Child("Model").AddComponent<CadSceneView>(); _view.BodyMaterial=_material;
            var selection=_view.gameObject.AddComponent<SelectionVisuals>(); selection.Configure(_view,null,null);
            _backend=new Backend { Model=GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)) };
            var graph=SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Bolt"",""definition_kind"":""part"",""definition_document_id"":""doc_bolt"",""children"":[]}}"));
            var scene=new LoadedScene(graph,new Dictionary<string,GlbModel>{{"doc_bolt",_backend.Model}},new Dictionary<string,string>{{"doc_bolt","a_test"}},new List<string>());
            _scene=scene;
            _view.Show(scene); _workspace=Child("Workspace").AddComponent<DesignWorkspace>();
            _workspace.Initialize(_view,selection,ray,eye.transform); _workspace.Bind(_backend);
            _workspace.SetScene(scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }
        private GameObject Child(string name) { var go=new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        private Button Button(string text) => Panel.GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text==text);
        private void Click(string text) { var b=Button(text); Assert.True(b.interactable,text); b.onClick.Invoke(); }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(_root); Object.DestroyImmediate(_material); }

        [Test] public void PreviewDoesNotCommitUntilExplicitApplyAndCancelRestoresOriginal()
        {
            Click("Crea schizzo"); Click("XY");
            Assert.AreEqual(1,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            Assert.True(Button("Applica").interactable); Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            Click("Annulla comando"); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.AreSame(_material,_view.Instances[0].Bodies[0].Renderer.sharedMaterial);
            Click("Crea schizzo"); Click("XY"); Click("Applica");
            Assert.AreEqual(1,_backend.Commits); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
        }
        [Test] public void HistoryIsAvailableFromToolsAndOfflineRemovesRemoteActions()
        {
            Click("Annulla modifica XR"); Assert.AreEqual(1,_backend.HistoryCalls);
            Click("Ripeti modifica XR"); Assert.AreEqual(2,_backend.HistoryCalls);
            _workspace.SetOnline(false);
            Assert.False(Panel.GetComponentsInChildren<Button>().Any(b=>b.GetComponentInChildren<Text>().text=="Annulla modifica XR"));
        }
        [TestCase(false)][TestCase(true)]
        public void FacePickUsesRevisionBoundTopologyAndKeepsMeshHighlightId(bool staleScene)
        {
            var body=_view.Instances[0].Bodies[0];
            var range=body.Primitive.FaceMap.FaceAtTriangle(0);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var state=new DocumentState("doc_bolt",staleScene ? "r2" : "r","v");
            var context=DesignContext.Parse(new JObject {
                ["document_id"]=state.DocumentId,["revision"]=state.Revision,["kind"]="part",
                ["faces"]=new JArray(new JObject { ["id"]="ent_different_context",["body_index"]=body.Primitive.BodyIndex,
                    ["face_ordinal"]=range.Ordinal,["point_mm"]=new JArray(0,0,10),["normal"]=new JArray(0,0,1) }) },state);
            typeof(DesignWorkspace).GetField("_context",flags).SetValue(_workspace,context);
            typeof(DesignWorkspace).GetField("_state",flags).SetValue(_workspace,state);
            typeof(DesignWorkspace).GetMethod("SelectPlanarFace",flags).Invoke(_workspace,new object[]{body,0,Vector3.zero});
            var selected=(Selection)typeof(DesignWorkspace).GetField("_faceSelection",flags).GetValue(_workspace);
            if(staleScene) { Assert.IsNull(selected); return; }
            Assert.AreEqual(range.FaceId,selected.FaceId);
            Assert.AreEqual("ent_different_context",selected.EntityId);
            Assert.True(body.GetComponentsInChildren<MeshFilter>().Length>1,"Picked face is highlighted using its mesh id.");
            Click("Crea schizzo");
            Assert.AreEqual("ent_different_context",_backend.PreviewPlane);
        }
        [TestCase("ent_face",true)][TestCase("ent_stale",false)]
        public void CreateSketchUsesOnlyCurrentPlanarSelection(string face,bool valid)
        {
            typeof(DesignWorkspace).GetField("_face",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .SetValue(_workspace,face);
            Click("Crea schizzo");
            Assert.AreEqual(valid ? 1 : 0,_backend.Previews);
            if(valid) Assert.AreEqual(face,_backend.PreviewPlane);
            else Assert.True(Button("XY").interactable);
            Assert.AreEqual(0,_backend.Commits);
        }
        [UnityTest] public IEnumerator RebindingPendingApplyKeepsGuardAfterLateSuccess() => RebindDuringMutation(false,false);
        [UnityTest] public IEnumerator RebindingPendingUndoKeepsGuardAfterLostResponse() => RebindDuringMutation(true,true);
        private IEnumerator RebindDuringMutation(bool history,bool fail)
        {
            var previous=_backend; previous.PendingMutation=new TaskCompletionSource<DocumentState>();
            if(history) Click("Annulla modifica XR");
            else { Click("Crea schizzo"); Click("XY"); Click("Applica"); }
            _workspace.Bind(null);
            var next=new Backend { Model=previous.Model,State=new DocumentState("doc_bolt","r2","v2") };
            _workspace.Bind(next); _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.Open();
            Assert.False(Panel.GetComponentsInChildren<Text>().Any(t=>t.text=="Ho controllato il CAD"));
            Assert.False(Panel.GetComponentsInChildren<Text>().Any(t=>t.text=="Crea schizzo"));
            if(fail) previous.PendingMutation.SetException(new IOException("Lost response"));
            else previous.PendingMutation.SetResult(previous.State);
            yield return null; yield return null;
            Assert.AreEqual(0,next.StateReads,"Late completion must not refresh a replacement backend.");
            Assert.AreEqual(0,next.Commits+next.HistoryCalls);
            Click("Ho controllato il CAD");
            Assert.AreEqual(1,next.StateReads); Assert.True(Button("Crea schizzo").interactable);
            Assert.AreEqual(1,previous.Commits+previous.HistoryCalls);
        }
        [Test] public void LatePreviewAfterCloseCannotResurrectGhostOrApply()
        {
            _backend.PendingPreview=new TaskCompletionSource<DesignPreview>();
            Click("Crea schizzo"); Click("XY"); _workspace.Close();
            _backend.PendingPreview.SetResult(new DesignPreview("late","doc_bolt","r",DateTimeOffset.UtcNow.AddMinutes(1),_backend.Model));
            _workspace.Open(); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing); Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void ConstraintReadoutUsesNativeSnapshotAndRejectsChangedDraft()
        {
            Click("Crea schizzo"); Click("XY"); Click("Mostra tutti i vincoli");
            var text=string.Join("\n",Panel.GetComponentsInChildren<Text>().Select(t=>t.text));
            StringAssert.Contains("Orizzontale × 2",text); StringAssert.Contains("Verticale × 1",text);
            Click("Torna allo schizzo"); Click("Rimuovi ultimo"); Click("Mostra tutti i vincoli");
            text=string.Join("\n",Panel.GetComponentsInChildren<Text>().Select(t=>t.text));
            StringAssert.Contains("Calcola prima l’anteprima",text); StringAssert.DoesNotContain("Orizzontale × 2",text);
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void InvalidDimensionKeepsGhostAndRequiresNewValidPreview()
        {
            Click("Crea schizzo"); Click("XY"); Click("Estrudi schizzo"); Click("Anteprima");
            int previews=_backend.Previews;
            var dimension=typeof(DesignWorkspace).GetField("_dimension",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            dimension.SetValue(_workspace,0d); Click("Anteprima");
            Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.False(Button("Applica").interactable);
            Assert.AreEqual(previews,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            StringAssert.Contains("positiva",string.Join("\n",Panel.GetComponentsInChildren<Text>().Select(t=>t.text)));
            dimension.SetValue(_workspace,20d); Click("Anteprima");
            Assert.AreEqual(previews+1,_backend.Previews); Assert.True(Button("Applica").interactable);
        }
        [Test] public void RemovingLastEdgeClearsItsOverlayAndDimensionHandle()
        {
            Click("Raccordo");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var selected=(HashSet<string>)typeof(DesignWorkspace).GetField("_edges",flags).GetValue(_workspace);
            selected.Add("ent_edge"); Click("Anteprima");
            var handle=(LineRenderer)typeof(DesignWorkspace).GetField("_handle",flags).GetValue(_workspace);
            Assert.AreEqual(2,handle.positionCount);
            selected.Clear(); Click("Anteprima");
            Assert.AreEqual(0,handle.positionCount);
            Assert.AreEqual(0,_view.transform.Find("Design local geometry/Selected edges").childCount);
            Assert.False(Button("Applica").interactable); Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void FailedFilletHighlightsCommandEdgesAndRecoveryClearsErrorContext()
        {
            Click("Raccordo");
            var selected=(HashSet<string>)typeof(DesignWorkspace).GetField("_edges",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(_workspace);
            selected.Add("ent_edge"); Click("Anteprima");
            _backend.PreviewError=new InvalidOperationException("Radius too large"); Click("Anteprima");
            var geometry=_view.GetComponentInChildren<DesignGeometryView>();
            Assert.True(geometry.HasErrorContext); Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.False(Button("Applica").interactable);
            StringAssert.Contains("In arancione",string.Join("\n",Panel.GetComponentsInChildren<Text>().Select(t=>t.text)));
            Click("Dettagli errore");
            StringAssert.Contains("Radius too large",string.Join("\n",Panel.GetComponentsInChildren<Text>().Select(t=>t.text)));
            Click("Indietro"); _backend.PreviewError=null; Click("Anteprima");
            Assert.False(geometry.HasErrorContext); Assert.True(Button("Applica").interactable);
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void FirstFailedExistingSketchPreviewStillHighlightsItsCurrentProfile()
        {
            _backend.PreviewError=new InvalidOperationException("No valid extrusion");
            Click("Estrusione"); Click("Existing"); Click("Anteprima");
            Assert.True(_view.GetComponentInChildren<DesignGeometryView>().HasErrorContext);
            Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.False(Button("Applica").interactable); Assert.AreEqual(0,_backend.Commits);
            Click("Annulla comando"); Assert.False(_view.GetComponentInChildren<DesignGeometryView>().HasErrorContext);
        }
        [Test] public void InvasiveSketchConstraintRequiresExplicitDraftConfirmationThenPreview()
        {
            Click("Crea schizzo"); Click("XY");
            var sketch=(SketchDraft)typeof(DesignWorkspace).GetField("_sketch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(_workspace);
            sketch.Add(new SketchElement(SketchShape.Line,default,new CadPoint(10,4)));
            sketch.Add(new SketchElement(SketchShape.Line,new CadPoint(20,0),new CadPoint(30,5)));
            int previews=_backend.Previews;
            Click("Aggiungi vincolo"); Click("Uguale"); Click("Linea 1"); Click("Linea 2");
            Assert.AreEqual(0,sketch.ConstraintCount); Assert.AreEqual(previews,_backend.Previews);
            Click("Annulla vincolo"); Assert.AreEqual(0,sketch.ConstraintCount);
            Click("Aggiungi vincolo"); Click("Uguale"); Click("Linea 1"); Click("Linea 2"); Click("Conferma nella bozza");
            Assert.AreEqual(1,sketch.ConstraintCount); Assert.False(Button("Applica").interactable);
            Assert.AreEqual(previews,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            Click("Anteprima"); Assert.AreEqual(previews+1,_backend.Previews); Assert.True(Button("Applica").interactable);
            Click("Aggiungi vincolo"); Click("Rimuovi ultimo vincolo"); Assert.AreEqual(0,sketch.ConstraintCount);
            Assert.False(Button("Applica").interactable);
        }
        [Test] public void ConstraintPickerIncludesAndHighlightsIndividualRectangleSides()
        {
            Click("Crea schizzo"); Click("XY");
            var sketch=(SketchDraft)typeof(DesignWorkspace).GetField("_sketch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(_workspace);
            sketch.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(10,20),new CadPoint(40,60)));
            sketch.Add(new SketchElement(SketchShape.Line,default,new CadPoint(10,4)));
            Click("Aggiungi vincolo"); Click("Uguale"); Click("Rettangolo 1 • lato 2");
            var highlight=_view.transform.Find("Design local geometry/Local sketch draft/Selected rectangle side").GetComponent<LineRenderer>();
            Assert.AreEqual(CadCoordinates.ToLocal(new CadPoint(40,20)),highlight.GetPosition(0));
            Assert.AreEqual(CadCoordinates.ToLocal(new CadPoint(40,60)),highlight.GetPosition(1));
            Click("Linea 2"); Click("Conferma nella bozza");
            Assert.AreEqual(1,sketch.ConstraintCount); Assert.AreEqual(0,_backend.Commits);
            Assert.AreEqual("line:2",(string)sketch.Operations().Last["arguments"]["entity_ids"][0]);
        }
        [TestCase(false)][TestCase(true)] public void ToolButtonsIncludingHistoryFitInsidePanel(bool sketch)
        {
            if(sketch) { Click("Crea schizzo"); Click("XY"); }
            var rect=(RectTransform)Panel.transform; LayoutRebuilder.ForceRebuildLayoutImmediate(rect); Canvas.ForceUpdateCanvases();
            var corners=new Vector3[4];
            foreach(var button in Panel.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    var p=rect.InverseTransformPoint(corner);
                    Assert.That(p.x,Is.InRange(rect.rect.xMin-1,rect.rect.xMax+1));
                    Assert.That(p.y,Is.InRange(rect.rect.yMin-1,rect.rect.yMax+1));
                }
            }
        }
    }
}
