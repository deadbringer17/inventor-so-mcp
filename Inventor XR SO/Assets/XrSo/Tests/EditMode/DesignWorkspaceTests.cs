using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

namespace InventorXrSo.Tests
{
    public class DesignWorkspaceTests
    {
        private const string Create = "design.sketch.create", Extrude = "design.extrude", Hole = "design.hole",
            Fillet = "design.fillet", Chamfer = "design.chamfer", Undo = "design.history.undo", Redo = "design.history.redo",
            Dimension = "design.dimension", AddConstraint = "design.constraint.add", AllConstraints = "design.constraint.all",
            RemoveLastConstraint = "design.constraint.removelast", RemoveLast = "design.sketch.removelast",
            Parameters = "design.parameters";

        private GameObject _root;
        private Material _material;
        private DesignWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private LoadedScene _scene;
        private readonly List<string> _hud = new List<string>();
        private sealed class Backend : IDesignWorkspaceBackend, IDesignHistoryBackend
        {
            public DocumentState State = new DocumentState("doc_bolt","r","v");
            public GlbModel Model;
            public int Previews, Commits, HistoryCalls;
            public int StateReads, ParameterCount;
            public string PreviewPlane;
            public Exception PreviewError, CommitError;
            public TaskCompletionSource<DocumentState> PendingMutation, PendingState;
            public TaskCompletionSource<DesignPreview> PendingPreview;
            public Task<DesignContext> GetDesignContextAsync(DocumentState state,CancellationToken ct) => Task.FromResult(
                DesignContext.Parse(new JObject { ["document_id"]=state.DocumentId,["revision"]=state.Revision,["kind"]="part",
                    ["planes"]=JArray.Parse(@"[{""reference"":""3"",""name"":""XY"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]}]"),
                    ["faces"]=JArray.Parse(@"[{""id"":""ent_face"",""point_mm"":[0,0,10],""normal"":[0,0,1]}]"),
                    ["edges"]=JArray.Parse(@"[{""id"":""ent_edge"",""kind"":""line"",""points_mm"":[1000,2000,3000,1010,2000,3000]}]"),
                    ["sketches"]=JArray.Parse(@"[{""name"":""Existing"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]}]"),
                    ["parameters"]=new JArray(Enumerable.Range(0,ParameterCount).Select(i=>new JObject { ["name"]="P"+i,["value_mm"]=10.0+i,["unit"]="mm" })),
                    ["sketch_snapshots"]=JArray.Parse(@"[{""sketch_name"":""Existing"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},""entities"":[{""type"":""circle"",""center_mm"":[20,20],""radius_mm"":4}]}]") },state));
            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) { StateReads++; return PendingState?.Task ?? Task.FromResult(State); }
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
            {
                Commits++;
                if (CommitError != null) return Task.FromException<DocumentState>(CommitError);
                State=new DocumentState("doc_bolt","r2","v2"); return PendingMutation?.Task ?? Task.FromResult(State);
            }
            public Task<DesignHistory> GetHistoryAsync(DocumentState state,CancellationToken ct) => Task.FromResult(new DesignHistory(state,true,true,"ticket"));
            public Task<DocumentState> ApplyHistoryAsync(DesignHistory history,bool redo,CancellationToken ct)
            { HistoryCalls++; State=new DocumentState("doc_bolt","r3","v3"); return PendingMutation?.Task ?? Task.FromResult(State); }
        }
        [SetUp] public void Setup()
        {
            _hud.Clear(); _haptics.Clear(); _previousSink=Haptics.Sink; Haptics.Sink=(pulse,pen)=>_haptics.Add(pulse);
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
            _workspace.HudMessage+=_hud.Add;
            _workspace.Initialize(_view,selection,ray,eye.transform); _workspace.Bind(_backend);
            _workspace.SetScene(scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }
        private GameObject Child(string name) { var go=new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        [TearDown] public void Cleanup()
        {
            Haptics.Sink=_previousSink;
            // The chip and the ring are free-standing canvases (not children of the workspace).
            foreach(var name in new[]{"_chip","_ring"})
            {
                var field=typeof(DesignWorkspace).GetField(name,Flags);
                var view=_workspace==null ? null : field.GetValue(_workspace) as Component;
                if(view!=null) Object.DestroyImmediate(view.gameObject);
            }
            foreach(var r in _roots) if(r!=null) Object.DestroyImmediate(r);
            _roots.Clear();
            Object.DestroyImmediate(_root); Object.DestroyImmediate(_material);
        }

        private XrAction Act(string id) => _workspace.Actions.Single(a=>a.Id==id);
        private bool Enabled(string id) => Act(id).Enabled;
        /// <summary>Invokes a declared action by id, exactly like the palette, ring or commit bar would.</summary>
        private void Do(string id) { var a=Act(id); Assert.True(a.Enabled,id+": "+a.DisabledReason); Assert.True(a.TryInvoke(),id); }
        private XrAction PickItem(string label) => _workspace.Actions.Single(a=>a.Id.StartsWith("design.pick.") && a.Label==label);
        private void Pick(string label) { var a=PickItem(label); Assert.True(a.Enabled,label); Assert.True(a.TryInvoke(),label); }
        private static readonly System.Reflection.BindingFlags Flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        private T Field<T>(string name) => (T)typeof(DesignWorkspace).GetField(name,Flags).GetValue(_workspace);
        private void SetField(string name,object value) => typeof(DesignWorkspace).GetField(name,Flags).SetValue(_workspace,value);
        private string AllHud() => string.Join("\n",_hud);
        private void Commit(params double[] values)
        {
            foreach(var value in values)
            {
                var entry=_workspace.ActiveEntry; Assert.NotNull(entry,"a numeric entry is waiting for the keypad"); Assert.True(entry.Editing);
                entry.CommitValue(value,out var reason); Assert.AreEqual("",reason);
            }
        }
        private void SketchOnXy() { Do(Create); Pick("XY"); }

        [Test] public void PreviewDoesNotCommitUntilExplicitApplyAndCancelRestoresOriginal()
        {
            SketchOnXy();
            Assert.AreEqual(1,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            Assert.True(Enabled(CommitIds.Apply)); Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.AreEqual(CommitBarPhase.Ready,_workspace.CommitBar.Phase);
            Do(CommitIds.Cancel); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.AreEqual(CommitBarPhase.Empty,_workspace.CommitBar.Phase);
            Assert.AreSame(_material,_view.Instances[0].Bodies[0].Renderer.sharedMaterial);
            SketchOnXy(); Do(CommitIds.Apply);
            Assert.AreEqual(1,_backend.Commits); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.AreEqual(CommitBarPhase.Applied,_workspace.CommitBar.Phase,"MarkApplied after a successful apply");
        }
        [Test] public void HistoryIsAvailableFromFeatureTabAndOfflineRemovesRemoteActions()
        {
            Do(Undo); Assert.AreEqual(1,_backend.HistoryCalls);
            Do(Redo); Assert.AreEqual(2,_backend.HistoryCalls);
            _workspace.SetOnline(false);
            Assert.False(Enabled(Undo)); Assert.False(Enabled(Create));
            Assert.AreEqual(CommitBarPhase.Offline,_workspace.CommitBar.Phase);
        }
        [TestCase(false)][TestCase(true)]
        public void FacePickUsesRevisionBoundTopologyAndKeepsMeshHighlightId(bool staleScene)
        {
            var body=_view.Instances[0].Bodies[0];
            var range=body.Primitive.FaceMap.FaceAtTriangle(0);
            var state=new DocumentState("doc_bolt",staleScene ? "r2" : "r",staleScene ? "v2" : "v");
            var context=DesignContext.Parse(new JObject {
                ["document_id"]=state.DocumentId,["revision"]=state.Revision,["kind"]="part",
                ["faces"]=new JArray(new JObject { ["id"]="ent_different_context",["body_index"]=body.Primitive.BodyIndex,
                    ["face_ordinal"]=range.Ordinal,["point_mm"]=new JArray(0,0,10),["normal"]=new JArray(0,0,1) }) },state);
            SetField("_context",context); SetField("_state",state);
            typeof(DesignWorkspace).GetMethod("SelectPlanarFace",Flags).Invoke(_workspace,new object[]{body,0,Vector3.zero});
            var selected=Field<Selection>("_faceSelection");
            if(staleScene) { Assert.IsNull(selected); return; }
            Assert.AreEqual(range.FaceId,selected.FaceId);
            Assert.AreEqual("ent_different_context",selected.EntityId);
            Assert.True(body.GetComponentsInChildren<MeshFilter>().Length>1,"Picked face is highlighted using its mesh id.");
            Do(Create);
            Assert.AreEqual("ent_different_context",_backend.PreviewPlane);
        }
        [Test] public void NonVisualRevisionBumpKeepsTheSceneCurrentForFacePicking()
        {
            var body=_view.Instances[0].Bodies[0];
            var range=body.Primitive.FaceMap.FaceAtTriangle(0);
            // The scene reloads only on a visual revision change: the revision alone moves on (context is read at the new revision).
            var state=new DocumentState("doc_bolt","r_non_visual","v");
            var context=DesignContext.Parse(new JObject {
                ["document_id"]=state.DocumentId,["revision"]=state.Revision,["kind"]="part",
                ["faces"]=new JArray(new JObject { ["id"]="ent_face_nv",["body_index"]=body.Primitive.BodyIndex,
                    ["face_ordinal"]=range.Ordinal,["point_mm"]=new JArray(0,0,10),["normal"]=new JArray(0,0,1) }) },state);
            SetField("_context",context); SetField("_state",state);
            typeof(DesignWorkspace).GetMethod("SelectPlanarFace",Flags).Invoke(_workspace,new object[]{body,0,Vector3.zero});
            Assert.AreEqual("ent_face_nv",Field<Selection>("_faceSelection")?.EntityId);
            // A context of an older revision is still rejected (revision-bound).
            SetField("_faceSelection",null);
            SetField("_context",DesignContext.Parse(new JObject { ["document_id"]=state.DocumentId,["revision"]="r_old",["kind"]="part",["faces"]=new JArray() },new DocumentState(state.DocumentId,"r_old","v")));
            typeof(DesignWorkspace).GetMethod("SelectPlanarFace",Flags).Invoke(_workspace,new object[]{body,0,Vector3.zero});
            Assert.IsNull(Field<Selection>("_faceSelection"));
        }
        [TestCase("ent_face",true)][TestCase("ent_stale",false)]
        public void CreateSketchUsesOnlyCurrentPlanarSelection(string face,bool valid)
        {
            SetField("_face",face);
            Do(Create);
            Assert.AreEqual(valid ? 1 : 0,_backend.Previews);
            if(valid) Assert.AreEqual(face,_backend.PreviewPlane);
            else Assert.True(PickItem("XY").Enabled);
            Assert.AreEqual(0,_backend.Commits);
        }
        [UnityTest] public IEnumerator RebindingPendingApplyKeepsGuardAfterLateSuccess() => RebindDuringMutation(false,false);
        [UnityTest] public IEnumerator RebindingPendingUndoKeepsGuardAfterLostResponse() => RebindDuringMutation(true,true);
        private IEnumerator RebindDuringMutation(bool history,bool fail)
        {
            var previous=_backend; previous.PendingMutation=new TaskCompletionSource<DocumentState>();
            if(history) Do(Undo);
            else { SketchOnXy(); Do(CommitIds.Apply); }
            _workspace.Bind(null);
            var next=new Backend { Model=previous.Model,State=new DocumentState("doc_bolt","r2","v2") };
            _workspace.Bind(next); _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.Open();
            Assert.False(Enabled(CommitIds.Recover),"Recovery waits for the previous request to conclude.");
            Assert.False(Enabled(Create));
            Assert.AreEqual(CommitBarPhase.Uncertain,_workspace.CommitBar.Phase);
            if(fail) previous.PendingMutation.SetException(new IOException("Lost response"));
            else previous.PendingMutation.SetResult(previous.State);
            yield return null; yield return null;
            Assert.AreEqual(0,next.StateReads,"Late completion must not refresh a replacement backend.");
            Assert.AreEqual(0,next.Commits+next.HistoryCalls);
            Assert.AreEqual("Ho controllato il CAD",Act(CommitIds.Recover).Label);
            Do(CommitIds.Recover);
            Assert.AreEqual(1,next.StateReads); Assert.True(Enabled(Create));
            Assert.AreEqual(1,previous.Commits+previous.HistoryCalls);
        }
        [Test] public void LatePreviewAfterCloseCannotResurrectGhostOrApply()
        {
            _backend.PendingPreview=new TaskCompletionSource<DesignPreview>();
            SketchOnXy(); _workspace.Close();
            _backend.PendingPreview.SetResult(new DesignPreview("late","doc_bolt","r",DateTimeOffset.UtcNow.AddMinutes(1),_backend.Model));
            _workspace.Open(); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing); Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void ConstraintReadoutUsesNativeSnapshotAndRejectsChangedDraft()
        {
            SketchOnXy(); Do(AllConstraints);
            StringAssert.Contains("Orizzontale × 2",_workspace.Notice); StringAssert.Contains("Verticale × 1",_workspace.Notice);
            StringAssert.Contains("Orizzontale × 2",AllHud(),"the rows go to the HUD");
            Do(RemoveLast); _hud.Clear(); Do(AllConstraints);
            StringAssert.Contains("Calcola prima l’anteprima",_workspace.Notice); StringAssert.DoesNotContain("Orizzontale × 2",_workspace.Notice);
            StringAssert.Contains("Calcola prima l’anteprima",AllHud());
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void InvalidDimensionKeepsGhostAndRequiresNewValidPreview()
        {
            SketchOnXy(); Do(Extrude); Do(CommitIds.Preview);
            int previews=_backend.Previews;
            SetField("_dimension",0d); Do(CommitIds.Preview);
            Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.False(Enabled(CommitIds.Apply));
            Assert.AreEqual(CommitBarPhase.Error,_workspace.CommitBar.Phase);
            Assert.AreEqual(previews,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            StringAssert.Contains("positiva",AllHud());
            SetField("_dimension",20d); Do(CommitIds.Preview);
            Assert.AreEqual(previews+1,_backend.Previews); Assert.True(Enabled(CommitIds.Apply));
        }
        [Test] public void RemovingLastEdgeClearsItsOverlayAndDimensionHandle()
        {
            Do(Fillet);
            var selected=Field<HashSet<string>>("_edges");
            selected.Add("ent_edge"); Do(CommitIds.Preview);
            var handle=Field<LineRenderer>("_handle");
            Assert.AreEqual(2,handle.positionCount);
            selected.Clear(); Do(CommitIds.Preview);
            Assert.AreEqual(0,handle.positionCount);
            Assert.AreEqual(0,_view.transform.Find("Design local geometry/Selected edges").childCount);
            Assert.False(Enabled(CommitIds.Apply)); Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void FailedFilletHighlightsCommandEdgesAndRecoveryClearsErrorContext()
        {
            Do(Fillet);
            Field<HashSet<string>>("_edges").Add("ent_edge"); Do(CommitIds.Preview);
            _hud.Clear();
            _backend.PreviewError=new InvalidOperationException("Radius too large"); Do(CommitIds.Preview);
            var geometry=_view.GetComponentInChildren<DesignGeometryView>();
            Assert.True(geometry.HasErrorContext); Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.False(Enabled(CommitIds.Apply));
            Assert.AreEqual(CommitBarPhase.Error,_workspace.CommitBar.Phase);
            StringAssert.DoesNotContain("Radius too large",_workspace.CommitBar.Message,"the bar only carries the short message");
            StringAssert.Contains("Radius too large",AllHud()); StringAssert.Contains("In arancione",AllHud());
            _backend.PreviewError=null; Do(CommitIds.Preview);
            Assert.False(geometry.HasErrorContext); Assert.True(Enabled(CommitIds.Apply));
            Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void FirstFailedExistingSketchPreviewStillHighlightsItsCurrentProfile()
        {
            _backend.PreviewError=new InvalidOperationException("No valid extrusion");
            Do(Extrude); Pick("Existing"); Do(CommitIds.Preview);
            Assert.True(_view.GetComponentInChildren<DesignGeometryView>().HasErrorContext);
            Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
            Assert.False(Enabled(CommitIds.Apply)); Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Cancel); Assert.False(_view.GetComponentInChildren<DesignGeometryView>().HasErrorContext);
        }
        [Test] public void InvasiveSketchConstraintRequiresExplicitDraftConfirmationThenPreview()
        {
            SketchOnXy();
            var sketch=Field<SketchDraft>("_sketch");
            sketch.Add(new SketchElement(SketchShape.Line,default,new CadPoint(10,4)));
            sketch.Add(new SketchElement(SketchShape.Line,new CadPoint(20,0),new CadPoint(30,5)));
            int previews=_backend.Previews;
            Do(AddConstraint); Pick("Uguale"); Pick("Linea 1"); Pick("Linea 2");
            Assert.AreEqual(0,sketch.ConstraintCount); Assert.AreEqual(previews,_backend.Previews);
            Pick("Annulla vincolo"); Assert.AreEqual(0,sketch.ConstraintCount);
            Do(AddConstraint); Pick("Uguale"); Pick("Linea 1"); Pick("Linea 2"); Pick("Conferma nella bozza");
            Assert.AreEqual(1,sketch.ConstraintCount); Assert.False(Enabled(CommitIds.Apply));
            Assert.AreEqual(previews,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Preview); Assert.AreEqual(previews+1,_backend.Previews); Assert.True(Enabled(CommitIds.Apply));
            Do(RemoveLastConstraint); Assert.AreEqual(0,sketch.ConstraintCount);
            Assert.False(Enabled(CommitIds.Apply));
        }
        [Test] public void ConstraintPickerIncludesAndHighlightsIndividualRectangleSides()
        {
            SketchOnXy();
            var sketch=Field<SketchDraft>("_sketch");
            sketch.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(10,20),new CadPoint(40,60)));
            sketch.Add(new SketchElement(SketchShape.Line,default,new CadPoint(10,4)));
            Do(AddConstraint); Pick("Uguale"); Pick("Rettangolo 1 • lato 2");
            var highlight=_view.transform.Find("Design local geometry/Local sketch draft/Selected rectangle side").GetComponent<LineRenderer>();
            Assert.AreEqual(CadCoordinates.ToLocal(new CadPoint(40,20)),highlight.GetPosition(0));
            Assert.AreEqual(CadCoordinates.ToLocal(new CadPoint(40,60)),highlight.GetPosition(1));
            Pick("Linea 2"); Pick("Conferma nella bozza");
            Assert.AreEqual(1,sketch.ConstraintCount); Assert.AreEqual(0,_backend.Commits);
            Assert.AreEqual("line:2",(string)sketch.Operations().Last["arguments"]["entity_ids"][0]);
        }

        // ---------------------------------------------------------------- M6: declared actions

        private ActionCatalog CatalogFor()
        {
            var spaces=TestDocs.Create();
            var catalog=new ActionCatalog(spaces); catalog.SetActive(_workspace); return catalog;
        }
        private void AssertCatalogIsValid(string state)
        {
            var catalog=CatalogFor();
            var ids=_workspace.Actions.Select(a=>a.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids,state+": ids");
            Assert.True(ids.All(id=>id.StartsWith("design.")||id.StartsWith("commit.")),state+": stable id namespaces");
            var known=new HashSet<string>(_workspace.Tabs.Select(t=>t.Id)){ActionCatalog.CommitTab,DesignWorkspace.TabView};   // M9: the Vista tab is shared (ViewActions)
            foreach(var action in _workspace.Actions) Assert.True(known.Contains(action.Tab),state+": tab of "+action.Id);
            foreach(var tab in _workspace.Tabs)
                Assert.LessOrEqual(catalog.Palette(tab.Id).Count,ActionCatalog.MaxPalette,state+": "+tab.Id);
            Assert.LessOrEqual(catalog.Palette(ActionCatalog.CommitTab).Count,ActionCatalog.MaxPalette);
            Assert.False(_workspace.Actions.Any(a=>a.Label=="Precedenti"||a.Label=="Successivi"),state+": no pagination buttons");
            Assert.False(_workspace.Actions.Any(a=>a.Label.StartsWith("Blocca")||a.Label=="Torna a Ispeziona"),state);
            foreach(var action in _workspace.Actions.Where(a=>!a.Enabled)) Assert.False(string.IsNullOrEmpty(action.DisabledReason),state+": reason of "+action.Id);
        }
        [Test] public void AtRestOnlyOnTheToolsScreenWithNoDraft()
        {
            Assert.True(_workspace.AtRest);
            Do(Create); Pick("XY");
            Assert.False(_workspace.AtRest, "an open sketch is a draft: X must keep the Back chain");
        }
        [Test] public void ActionIdsAreUniqueAndEveryTabHasAtMostEightActionsInEveryScreenState()
        {
            _backend.ParameterCount=0;
            AssertCatalogIsValid("tools");
            CollectionAssert.AreEqual(new[]{"schizzo","vincoli","feature","opzioni","parametri"},_workspace.Tabs.Where(t=>!t.Hidden).Select(t=>t.Id).ToArray());
            Do(Create); AssertCatalogIsValid("planes picker");
            Pick("XY"); AssertCatalogIsValid("sketch");
            Do(AddConstraint); AssertCatalogIsValid("constraint kinds"); Pick("Tangente");
            AssertCatalogIsValid("entities empty sketch");
            var sketch=Field<SketchDraft>("_sketch");
            for(int i=0;i<12;i++) sketch.Add(new SketchElement(SketchShape.Line,new CadPoint(i*5,0),new CadPoint(i*5,4)));
            Do(AddConstraint); Pick("Tangente"); AssertCatalogIsValid("entities 12 lines");
            Assert.AreEqual(2,_workspace.Tabs.Count(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)),"13 entries -> 2 picker tabs");
            _workspace.Back(); Assert.False(_workspace.Tabs.Any(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)));
            Do(Extrude); AssertCatalogIsValid("extrude from sketch");
            Do(Hole); AssertCatalogIsValid("hole"); Do(Fillet); AssertCatalogIsValid("fillet"); Do(Chamfer); AssertCatalogIsValid("chamfer");
        }
        [Test] public void FeatureActionsFollowTheActiveFeature()
        {
            Assert.False(Enabled("design.diameter")); Assert.False(Enabled(Dimension)); Assert.False(Enabled("design.operation"));
            Do(Hole);
            Assert.True(Enabled("design.diameter")); Assert.True(Enabled("design.position")); Assert.True(Enabled(Dimension));
            Assert.False(Enabled("design.operation"));
            Assert.AreEqual("Solo per l’Estrusione.",Act("design.operation").DisabledReason);
            Do(Extrude); Pick("Existing");
            Assert.True(Enabled("design.operation")); Assert.True(Enabled("design.direction")); Assert.False(Enabled("design.diameter"));
            StringAssert.Contains("Unisci",Act("design.operation").Label);
            Do("design.operation"); StringAssert.Contains("Taglia",Act("design.operation").Label);
            Do("design.direction"); StringAssert.Contains("Negativa",Act("design.direction").Label);
        }
        [Test] public void LongPickerSplitsIntoPagesOfEightAndNothingIsDropped()
        {
            _backend.ParameterCount=19;
            _workspace.SetOnline(false); _workspace.SetOnline(true);   // reload the context with 19 parameters
            Do(Parameters);
            var pages=_workspace.Tabs.Where(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)).ToList();
            Assert.AreEqual(3,pages.Count);
            StringAssert.Contains("1/3",pages[0].Label);
            var catalog=CatalogFor();
            var labels=pages.SelectMany(p=>catalog.Palette(p.Id)).Select(a=>a.Label).ToList();
            Assert.AreEqual(19,labels.Count); CollectionAssert.AllItemsAreUnique(labels);
            Assert.AreEqual(8,catalog.Palette(pages[0].Id).Count); Assert.AreEqual(3,catalog.Palette(pages[2].Id).Count);
            Assert.True(Enumerable.Range(0,19).All(i=>labels.Any(l=>l.StartsWith("P"+i+" = "))),"every parameter is reachable");
            Pick("P18 = 28 mm");
            Assert.False(_workspace.Tabs.Any(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)),"choosing closes the list");
            Commit(29.5);
            Assert.AreEqual(1,_backend.Previews);
            Assert.AreEqual(0,_backend.Commits); Assert.True(Enabled(CommitIds.Apply));
        }
        [Test] public void ParameterKeypadCancelDropsThePromptWithoutDraftOrCommit()
        {
            _backend.ParameterCount=2;
            _workspace.SetOnline(false); _workspace.SetOnline(true);
            Do(Parameters); Pick("P1 = 11 mm");
            Assert.NotNull(_workspace.ActiveEntry);
            _workspace.ActiveEntry.CancelEdit();
            Assert.AreEqual(0,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
        }
        [UnityTest] public IEnumerator CommitBarPhasesFollowTheSessionState()
        {
            Assert.AreEqual(CommitBarPhase.Empty,_workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(CommitIds.Preview)); Assert.False(Enabled(CommitIds.Cancel));

            // previewing, then ready
            _backend.PendingPreview=new TaskCompletionSource<DesignPreview>();
            SketchOnXy();
            Assert.AreEqual(CommitBarPhase.Previewing,_workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(CommitIds.Preview));
            _backend.PendingPreview.SetResult(new DesignPreview("late","doc_bolt","r",DateTimeOffset.UtcNow.AddMinutes(1),_backend.Model));
            yield return null; yield return null;
            Assert.AreEqual(CommitBarPhase.Ready,_workspace.CommitBar.Phase);
            Assert.True(Enabled(CommitIds.Apply)); Assert.True(Enabled(CommitIds.Cancel));
            // an edit makes it a draft again and Apply goes away until the next preview
            _backend.PendingPreview=null;
            Do(RemoveLast);
            Assert.AreEqual(CommitBarPhase.Draft,_workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.True(Enabled(CommitIds.Preview));
            // error
            _backend.PreviewError=new InvalidOperationException("boom"); Do(CommitIds.Preview);
            Assert.AreEqual(CommitBarPhase.Error,_workspace.CommitBar.Phase); Assert.False(string.IsNullOrEmpty(_workspace.CommitBar.Message));
            Assert.False(Enabled(CommitIds.Apply)); Assert.True(Enabled(CommitIds.Cancel));
            _backend.PreviewError=null; Do(CommitIds.Cancel);
            Assert.AreEqual(CommitBarPhase.Empty,_workspace.CommitBar.Phase);
            // offline
            _workspace.SetOnline(false);
            Assert.AreEqual(CommitBarPhase.Offline,_workspace.CommitBar.Phase); Assert.False(Enabled(CommitIds.Apply));
        }
        [UnityTest] public IEnumerator StaleDocumentShowsRecoveryThenAppliedAfterTheRefresh()
        {
            _backend.PendingState=new TaskCompletionSource<DocumentState>();
            SketchOnXy(); Do(CommitIds.Apply);
            Assert.AreEqual(CommitBarPhase.Stale,_workspace.CommitBar.Phase);
            Assert.AreEqual("Aggiorna documento",Act(CommitIds.Recover).Label); Assert.True(Enabled(CommitIds.Recover));
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(Create)); Assert.False(Enabled(CommitIds.Preview));
            _backend.PendingState.SetResult(_backend.State);
            yield return null; yield return null;
            Assert.AreEqual(CommitBarPhase.Applied,_workspace.CommitBar.Phase);
        }
        [Test] public void UncertainOutcomeNeedsAReviewAndNeverRepeatsTheCommit()
        {
            _backend.CommitError=new IOException("Lost response");
            SketchOnXy(); Do(CommitIds.Apply);
            Assert.AreEqual(CommitBarPhase.Uncertain,_workspace.CommitBar.Phase);
            Assert.AreEqual("Ho controllato il CAD",Act(CommitIds.Recover).Label);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(Create)); Assert.True(_workspace.RequiresCadReview);
            Do(CommitIds.Recover);
            Assert.AreEqual(1,_backend.Commits); Assert.False(_workspace.RequiresCadReview); Assert.True(Enabled(Create));
        }
        [Test] public void ApplyIsDisabledUnlessThePreviewIsReadyAndEditsBlockIt()
        {
            Assert.False(Enabled(CommitIds.Apply));
            SketchOnXy(); Assert.True(Enabled(CommitIds.Apply));
            Do("design.shape.circle"); Assert.True(Enabled(CommitIds.Apply),"a shape change alone does not touch the draft");
            Do(RemoveLast); Assert.False(Enabled(CommitIds.Apply));
            Do(CommitIds.Preview); Assert.True(Enabled(CommitIds.Apply));
            _workspace.SetOnline(false); Assert.False(Enabled(CommitIds.Apply));
        }
        [Test] public void RingActionsForPlanarFaceAndEdgeUseTheSameDeclaredActions()
        {
            CollectionAssert.AreEqual(new[]{Create,Extrude,Hole},_workspace.ContextActions(UiSelectionKind.PlanarFace).Select(a=>a.Id).ToArray());
            CollectionAssert.AreEqual(new[]{Fillet,Chamfer},_workspace.ContextActions(UiSelectionKind.Edge).Select(a=>a.Id).ToArray());
            Assert.IsEmpty(_workspace.ContextActions(UiSelectionKind.None));
            Assert.IsEmpty(_workspace.ContextActions(UiSelectionKind.Face));
            Assert.IsEmpty(_workspace.ContextActions(UiSelectionKind.Component));
            var catalog=CatalogFor();
            Assert.AreEqual(3,catalog.Context(UiSelectionKind.PlanarFace).Count); Assert.AreEqual(2,catalog.Context(UiSelectionKind.Edge).Count);
            _workspace.SetOnline(false);
            Assert.True(_workspace.ContextActions(UiSelectionKind.Edge).All(a=>!a.Enabled),"the ring disables like the palette");
        }
        [Test] public void RingTableM9WithTheSharedMeasureAction()
        {
            var catalog=CatalogFor(); catalog.AddShared(new MeasureStubProvider());
            RingTable.Assert(catalog,UiSelectionKind.PlanarFace,new[]{Create,Extrude,Hole,ActionCatalog.MeasureId});
            RingTable.Assert(catalog,UiSelectionKind.Edge,new[]{Fillet,Chamfer,ActionCatalog.MeasureId});
            RingTable.Assert(catalog,UiSelectionKind.Face,new[]{ActionCatalog.MeasureId});   // non-planar face: Misura only
            RingTable.Assert(catalog,UiSelectionKind.None,new string[0]);
        }
        [Test] public void DimensionKeypadThumbstickAndDictationShareOneEntry()
        {
            SketchOnXy(); Do(Extrude); Do(CommitIds.Preview);
            Assert.AreEqual(10d,Field<double>("_dimension"));
            Assert.True(Enabled(CommitIds.Apply));
            Do(Dimension); Assert.AreEqual(DesignWorkspace.FieldDimension,_workspace.ActiveEntry.Id);
            Commit(25); Assert.AreEqual(25d,Field<double>("_dimension"));
            Assert.False(Enabled(CommitIds.Apply),"changing the value drops the preview");
            Assert.IsNull(_workspace.ActiveEntry,"the confirmed keypad entry is no longer pending");
            Assert.True(_workspace.NudgeDimension(1)); Assert.AreEqual(26d,Field<double>("_dimension"));
            _workspace.CycleDimensionStep(1); Assert.True(_workspace.NudgeDimension(-1)); Assert.AreEqual(16d,Field<double>("_dimension"));
            Assert.AreEqual(DesignWorkspace.FieldDimension,_workspace.ArmedField.Id);
            Assert.True(_workspace.SetArmedField(DesignWorkspace.FieldDimension,33));
            Assert.AreEqual(33d,Field<double>("_dimension"));
            Assert.False(_workspace.SetArmedField(DesignWorkspace.FieldDimension,0)); Assert.False(_workspace.SetArmedField(DesignWorkspace.FieldDimension,double.NaN));
            Assert.False(_workspace.SetArmedField(DesignWorkspace.FieldDimension,20000));
            Assert.False(_workspace.SetArmedField("other",3));
            Assert.AreEqual(33d,Field<double>("_dimension")); Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void SequentialPromptsFillTheHoleDraftAndDictationConfirmsTheOpenKeypad()
        {
            Do(Hole); SetField("_face","ent_face");
            Do("design.position"); Assert.AreEqual("design.ask.0",_workspace.ActiveEntry.Id);
            Commit(12); Assert.AreEqual("design.ask.1",_workspace.ActiveEntry.Id);
            Commit(0);
            Assert.AreEqual("design.ask.2",_workspace.ArmedField.Id,"dictation targets the open keypad entry");
            Assert.True(_workspace.SetArmedField("design.ask.2",10));
            Assert.IsNull(_workspace.ActiveEntry);
            var point=Field<CadPoint>("_facePoint"); Assert.AreEqual(12,point.X); Assert.AreEqual(10,point.Z);
            Do("design.diameter"); Commit(4); Assert.AreEqual(4d,Field<double>("_diameter"));
            Do("design.through"); Assert.False(Field<bool>("_through"));
            Assert.True(Act("design.through").Kind==XrActionKind.Toggle && !Act("design.through").IsOn);
            Do(Dimension); Commit(5);
            Assert.AreEqual(5d,Field<double>("_dimension")); Assert.True(Enabled(CommitIds.Preview));
        }
        [Test] public void NumericShapeUsesFourSequentialEntriesAndAddsOneLine()
        {
            SketchOnXy();
            var sketch=Field<SketchDraft>("_sketch");
            Do("design.sketch.numeric");
            Commit(0,0,30,40);
            Assert.AreEqual(1,sketch.Elements.Count); Assert.AreEqual(50,(sketch.Elements[0].B-sketch.Elements[0].A).Length,1e-9);
            Assert.False(Enabled(CommitIds.Apply)); Assert.AreEqual(0,_backend.Commits);
        }
        [Test] public void KeypadPromptsBlockStartingAnotherAndCloseSilently()
        {
            Do(Hole); Do(Dimension);
            Assert.NotNull(_workspace.ActiveEntry);
            _workspace.Close();
            Assert.IsNull(_workspace.ActiveEntry); Assert.False(_workspace.Active);
        }
        [Test] public void VoiceSurfaceUsesTheSameActionsAndApplyOnlyShowsANotice()
        {
            Assert.IsNull(_workspace.VoicePanel);
            Assert.True(_workspace.IsEnabled(CommandIds.CreateSketch)); Assert.True(_workspace.IsEnabled(CommandIds.Fillet)); Assert.True(_workspace.IsEnabled(CommandIds.Chamfer));
            Assert.True(_workspace.IsEnabled(CommandIds.Undo)); Assert.False(_workspace.IsEnabled(CommandIds.Apply)); Assert.False(_workspace.IsEnabled(CommandIds.CancelDraft));
            Assert.False(_workspace.IsEnabled(CommandIds.Flange)); Assert.False(_workspace.Invoke(CommandIds.Flange));
            Assert.True(_workspace.Invoke(CommandIds.Fillet)); Assert.True(_workspace.IsEnabled(CommandIds.CancelDraft));
            Field<HashSet<string>>("_edges").Add("ent_edge"); Do(CommitIds.Preview);
            Assert.True(_workspace.IsEnabled(CommandIds.Apply));
            Assert.True(_workspace.Invoke(CommandIds.Apply)); Assert.AreEqual(0,_backend.Commits,"voice Apply never commits");
            StringAssert.Contains("Applica",_workspace.Notice);
            var labels=_workspace.VoiceActions.ToList();
            CollectionAssert.Contains(labels.Select(l=>l.label).ToList(),"Raccordo");
            Assert.False(_workspace.InvokeVoiceAction("Applica"),"the commit bar action is never voice-invoked");
            Assert.True(_workspace.InvokeVoiceAction("Annulla comando")); Assert.AreEqual(CommitBarPhase.Empty,_workspace.CommitBar.Phase);
        }
        [Test] public void ArmedFieldIsNullWithoutAFeatureOrKeypad()
        {
            Assert.IsNull(_workspace.ArmedField);
            Do(Hole); Assert.IsNull(_workspace.ArmedField,"a through hole has no depth chip");
            Do("design.through"); Assert.AreEqual(DesignWorkspace.FieldDimension,_workspace.ArmedField.Id);
            _workspace.SetOnline(false); Assert.IsNull(_workspace.ArmedField);
        }

        // ---------------------------------------------------------------- with the shell, bench and sheet

        private UiShell AttachShell(out Workbench bench, out SketchSheetView sheet)
        {
            var catalog=new ActionCatalog(TestDocs.Create());
            var shell=UiShell.Create(Child("Left").transform,_root.transform.Find("Eye"),catalog);
            _roots.Add(shell.gameObject); _roots.Add(shell.CommitBar.Canvas.gameObject); _roots.Add(shell.Hud.Canvas.gameObject);
            bench=Child("Bench").AddComponent<Workbench>();
            sheet=Child("Sheet").AddComponent<SketchSheetView>(); sheet.Bind(_view.transform);
            _workspace.Attach(shell,bench,sheet);
            catalog.SetActive(_workspace);
            _workspace.Close(); _workspace.Open();   // a real Open with the bench in place
            return shell;
        }
        private readonly List<GameObject> _roots = new List<GameObject>();

        [Test] public void PaletteShowsTheDesignTabsAndPickerTabsAreReachableWithTheStick()
        {
            var shell=AttachShell(out _,out _);
            Assert.AreEqual("schizzo",shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("vincoli",shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+3); Assert.AreEqual("parametri",shell.Palette.CurrentTab);
            _backend.ParameterCount=11;
            _workspace.SetOnline(false); _workspace.SetOnline(true);
            Do(Parameters);
            Assert.AreEqual(DesignWorkspace.PickTabPrefix+"0",shell.Palette.CurrentTab); Assert.True(shell.Palette.InTabGroup);
            Assert.AreEqual(8,shell.Palette.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            shell.Palette.SelectTab(+1); Assert.AreEqual(DesignWorkspace.PickTabPrefix+"1",shell.Palette.CurrentTab);
            Assert.AreEqual(3,shell.Palette.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            shell.Palette.SelectTab(+1); Assert.AreEqual(DesignWorkspace.PickTabPrefix+"0",shell.Palette.CurrentTab,"stick cycles inside the list");
            _workspace.Back();
            Assert.False(shell.Palette.InTabGroup); Assert.AreEqual("parametri",shell.Palette.CurrentTab,"X returns to the tab it came from");
        }
        [Test] public void CommitBarSitsOnTheWorkPlaneAndFollowsThePhase()
        {
            var shell=AttachShell(out var bench,out _);
            Assert.NotNull(bench.Frame);
            var expected=WorkbenchLayout.CommitBarPosition(bench.Frame);
            Assert.AreEqual((float)expected.Z,shell.CommitBar.transform.position.z,1e-4f);
            Assert.AreEqual((float)expected.Y,shell.CommitBar.transform.position.y,1e-4f);
            Assert.False(shell.CommitBar.Canvas.gameObject.activeSelf);
            SketchOnXy();
            Assert.True(shell.CommitBar.Canvas.gameObject.activeSelf);
            Do(CommitIds.Cancel); Assert.False(shell.CommitBar.Canvas.gameObject.activeSelf);
        }
        [Test] public void OpeningPutsTheModelOnTheWorkPlaneAndClosingReleasesIt()
        {
            var shell=AttachShell(out var bench,out var sheet);
            bench.Snap();
            Assert.AreEqual(-(float)WorkbenchFrame.DefaultDeskDrop,_view.transform.position.y,1e-4f);
            int closed=0; _workspace.Closed+=()=>closed++;
            _workspace.Close(); _workspace.Close();
            Assert.AreEqual(1,closed,"Closed only fires for an open workspace");
            _view.transform.position=Vector3.one; bench.Snap();
            Assert.AreEqual(Vector3.one,_view.transform.position,"released: nothing moves the model any more");
            Assert.AreEqual(SketchSheetState.None,sheet.State);
        }
        [Test] public void SketchGoesOnTheSheetWithoutChangingCadCoordinates()
        {
            AttachShell(out var bench,out var sheet);
            SketchOnXy();
            Assert.AreEqual(SketchSheetState.Sheet,sheet.State);
            var frame=Field<SketchDraft>("_sketch").Frame; var origin=frame.OriginMm; var x=frame.XAxis;
            sheet.Snap();
            Assert.AreNotEqual(Quaternion.identity,_view.transform.rotation,"the sketch plane lies on the desk");
            Assert.True(Enabled("design.view.model")); Assert.False(Enabled("design.view.sheet"));
            Do("design.view.model"); Assert.AreEqual(SketchSheetState.Model,sheet.State);
            Assert.True(Enabled("design.view.sheet")); Do("design.view.sheet"); Assert.AreEqual(SketchSheetState.Sheet,sheet.State);
            var after=Field<SketchDraft>("_sketch").Frame;
            Assert.AreEqual(origin.X,after.OriginMm.X); Assert.AreEqual(x.X,after.XAxis.X); Assert.AreEqual(0,_backend.Commits);
            Do(CommitIds.Cancel);
            Assert.AreEqual(SketchSheetState.Model,sheet.State,"back to the model once the draft is gone");
        }
        [Test] public void RingAppearsForAFaceWithThreeActionsAndXClosesIt()
        {
            AttachShell(out _,out _);
            var ring=Field<RingView>("_ring");
            typeof(DesignWorkspace).GetMethod("ShowRing",Flags).Invoke(_workspace,new object[]{UiSelectionKind.PlanarFace,Vector3.zero});
            Assert.True(ring.Visible);
            Assert.AreEqual(3,ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            _workspace.Back(); Assert.False(ring.Visible);
            typeof(DesignWorkspace).GetMethod("ShowRing",Flags).Invoke(_workspace,new object[]{UiSelectionKind.Edge,Vector3.zero});
            Assert.AreEqual(2,ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>()[0].onClick.Invoke();   // Raccordo
            Assert.False(ring.Visible); Assert.AreEqual("fillet",Field<string>("_feature"));
        }
        [Test] public void ChipShowsTheDimensionOfTheFeatureAndOpensTheKeypad()
        {
            var shell=AttachShell(out _,out _);
            var chip=Field<ChipView>("_chip");
            Assert.False(chip.Canvas.gameObject.activeSelf);
            SketchOnXy(); Do(Extrude);
            Assert.True(chip.Canvas.gameObject.activeSelf); StringAssert.Contains("10 mm",chip.ValueText);
            Assert.True(chip.Modified,"a draft that has not been previewed");
            chip.Tap();
            Assert.True(shell.Palette.KeypadVisible); Assert.AreEqual(DesignWorkspace.FieldDimension,_workspace.ActiveEntry.Id);
            _workspace.ActiveEntry.Type('4'); _workspace.ActiveEntry.Type('2'); _workspace.ActiveEntry.Commit(out _);
            Assert.False(shell.Palette.KeypadVisible); Assert.AreEqual(42d,Field<double>("_dimension"));
            StringAssert.Contains("42 mm",chip.ValueText);
            Do(CommitIds.Cancel); Assert.False(chip.Canvas.gameObject.activeSelf);
        }
        [Test] public void SequentialKeypadHandsOverToTheNextEntryWithoutClosing()
        {
            var shell=AttachShell(out _,out _);
            SketchOnXy(); Do("design.sketch.numeric");
            var first=_workspace.ActiveEntry; first.Type('5'); first.Commit(out _);
            Assert.True(shell.Palette.KeypadVisible,"the next prompt is on the keypad"); Assert.AreNotSame(first,_workspace.ActiveEntry);
            shell.Palette.HideKeypad();
            typeof(DesignWorkspace).GetMethod("DropClosedKeypad",Flags).Invoke(_workspace,null);
            Assert.IsNull(_workspace.ActiveEntry,"cancelling the keypad drops the rest of the sequence");
        }
        [Test] public void ErrorDetailGoesToTheHudAndTheShortMessageToTheBar()
        {
            AttachShell(out _,out _);
            _backend.PreviewError=new InvalidOperationException("Inventor said no");
            SketchOnXy();
            StringAssert.Contains("Inventor said no",AllHud());
            StringAssert.DoesNotContain("Inventor said no",_workspace.CommitBar.Message);
            Assert.AreEqual(CommitBarPhase.Error,_workspace.CommitBar.Phase);
        }
        [Test] public void NoUnityUiPanelIsLeftInTheWorkspace()
        {
            Assert.IsNull(_workspace.GetComponentInChildren<HomePanel>(true));
            Assert.IsNull(typeof(DesignWorkspace).GetField("_panel",Flags));
            Assert.IsNull(typeof(DesignWorkspace).GetMethod("Render",Flags));
        }
        // ---------------------------------------------------------------- M6 phase 2 part B: XrInput (synthetic) drives the workspace

        private XrInput _xr;
        private XrInputFrame _frame;
        private float _clock;
        private readonly List<HapticPulse> _haptics = new List<HapticPulse>();
        private Action<HapticPulse,bool> _previousSink;


        /// <summary>Synthetic input: no OVRInput is read; the test feeds frames to the same XrInput the app uses.</summary>
        private void UseInput(UiShell shell=null,Workbench bench=null,SketchSheetView sheet=null)
        {
            _xr=Child("XrInput").AddComponent<XrInput>(); _xr.Source=new SyntheticInputSource();
            Assert.True(_xr.Synthetic,"the runner log must call this input synthetic");
            _xr.Dispatcher.StateProbe=()=>InventorXrSo.Core.Input.InputMap.Resolve(_workspace.KeypadOpen,_workspace.InputArmed,_workspace.SketchOpen,false);
            _workspace.Attach(shell,bench,sheet,_xr);
            _frame=new XrInputFrame{PenTracked=true,PaletteTracked=true};
            Poll();
        }
        private void Poll() => _xr.Poll(_frame,_clock+=0.016f);
        private void PressTrigger() { _frame.PenTrigger=true; Poll(); }
        private void ReleaseTrigger() { _frame.PenTrigger=false; Poll(); }
        private void Frame() => typeof(DesignWorkspace).GetMethod("Update",Flags).Invoke(_workspace,null);
        private Transform Hand => _root.transform.Find("Hand");
        private CadPoint Prop(string name) => (CadPoint)typeof(DesignWorkspace).GetProperty(name,Flags).GetValue(_workspace);
        private Vector3 World(CadPoint p) => _view.transform.TransformPoint(CadCoordinates.ToLocal(p));
        private double Dimension_() => Field<double>("_dimension");
        private bool Capturing => Field<bool>("_dimensionDrag");

        private void ExtrudeWithHandle() { SketchOnXy(); Do(Extrude); Do(CommitIds.Preview); }

        /// <summary>Points the pen ray at the middle of the dimension handle; returns the world direction that lengthens it.</summary>
        private Vector3 AimAtHandle()
        {
            var a=World(Prop("DimensionStart")); var b=World(Prop("DimensionEnd"));
            Hand.position=(a+b)*0.5f+Vector3.up*0.3f; Hand.rotation=Quaternion.LookRotation(Vector3.down);
            return (b-a).normalized;
        }
        private void AimAwayFromHandle() { Hand.position=new Vector3(5,5,5); Hand.rotation=Quaternion.LookRotation(Vector3.up); }
        private double MmPerMetre => 1000/_view.transform.lossyScale.x;

        private double DraftDistance()
        {
            var session=Field<DesignSession>("_session");
            var ops=(JArray)typeof(DesignSession).GetField("_operations",Flags).GetValue(session);
            return (double)ops.Last(o=>(string)o["command"]=="extrude")["arguments"]["distance_mm"];
        }

        [Test] public void WorkspaceWithoutAnInputNeverThrowsAndKeepsNoControllerState()
        {
            ExtrudeWithHandle();
            Assert.DoesNotThrow(()=>{ Frame(); _workspace.Back(); _workspace.FitView(); _workspace.RecenterView(); _workspace.ZoomView(1,0.1f); });
            Assert.False(Capturing);
        }

        [Test] public void TriggerHeldDragStartsOnlyOnTheHandleAndEditsOnlyTheDraft()
        {
            UseInput(); ExtrudeWithHandle();
            double start=Dimension_(); int previews=_backend.Previews;
            Assert.True(Enabled(CommitIds.Apply),"a preview is ready before the drag");

            AimAwayFromHandle(); PressTrigger();
            Assert.False(Capturing,"trigger away from the handle does not capture"); ReleaseTrigger();

            var axis=AimAtHandle(); PressTrigger();
            Assert.True(Capturing,"trigger held on the handle captures it");
            Assert.AreEqual(DesignStatus.Draft,Field<DesignSession>("_session").Status,"the capture only touches the draft");
            Assert.False(Enabled(CommitIds.Apply),"Apply is gone until a new preview");
            SetField("_dragDraftAt",-10f);   // let the throttled draft sync run on the next frame
            Hand.position+=axis*0.02f; Frame();
            Assert.AreEqual(start+0.02*MmPerMetre,Dimension_(),1e-3);
            Assert.AreEqual(Dimension_(),DraftDistance(),1e-9,"the session draft follows the drag");
            Assert.AreEqual(previews,_backend.Previews,"dragging never previews"); Assert.AreEqual(0,_backend.Commits,"dragging never applies");
        }

        [Test] public void ReleaseEndsTheCaptureAndPreviewsTheFinalValueWithoutApplying()
        {
            UseInput(); ExtrudeWithHandle();
            int previews=_backend.Previews;
            var axis=AimAtHandle(); PressTrigger();
            Hand.position+=axis*0.01f; Frame(); double dragged=Dimension_();
            Assert.AreNotEqual(10d,dragged);
            ReleaseTrigger();
            Assert.False(Capturing);
            Assert.AreEqual(previews+1,_backend.Previews,"release previews once"); Assert.AreEqual(0,_backend.Commits);
            Assert.AreEqual(dragged,DraftDistance(),1e-9);
            Hand.position+=axis*0.05f; Frame();
            Assert.AreEqual(dragged,Dimension_(),1e-12,"after the release the pen no longer edits");
            Assert.True(Enabled(CommitIds.Apply),"Apply is explicit, from the commit bar");
        }

        [Test] public void TrackingLossEndsTheCaptureKeepingTheLastValidValueWithAnErrorPulse()
        {
            UseInput(); ExtrudeWithHandle();
            var axis=AimAtHandle(); PressTrigger();
            Hand.position+=axis*0.01f; Frame(); double lastValid=Dimension_();
            int previews=_backend.Previews; _haptics.Clear();
            Hand.position=new Vector3(900,900,900);   // a lost controller reports garbage poses
            _frame.PenTracked=false; Poll();           // trigger still down: tracking loss, then release
            Assert.False(Capturing,"tracking loss closes the capture");
            Assert.AreEqual(lastValid,Dimension_(),1e-12);
            Assert.AreEqual(lastValid,DraftDistance(),1e-9,"the draft keeps the last valid value");
            Assert.AreEqual(previews,_backend.Previews,"a loss is not a release: no preview");
            CollectionAssert.Contains(_haptics,HapticPulse.Error);
            Frame(); Assert.AreEqual(lastValid,Dimension_(),1e-12);
            _frame.PenTracked=true; _frame.PenTrigger=false; Poll();
            Assert.AreEqual(previews,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
        }

        [Test] public void ClosingOrHidingTheWorkspaceEndsTheCapture()
        {
            UseInput(); ExtrudeWithHandle();
            AimAtHandle(); PressTrigger(); Assert.True(Capturing);
            _workspace.Close();
            Assert.False(Capturing); Assert.AreEqual(0,_backend.Commits);
            int closedPreviews=_backend.Previews;
            ReleaseTrigger(); Assert.AreEqual(closedPreviews,_backend.Previews,"a release after Close does not preview");

            _workspace.Open(); ExtrudeWithHandle();
            AimAtHandle(); PressTrigger(); Assert.True(Capturing);
            _workspace.SetVisible(false);
            Assert.False(Capturing); Assert.False(_workspace.Active);
        }

        [Test] public void PrecisionHeldMakesTheDragTenTimesSlower()
        {
            UseInput(); ExtrudeWithHandle();
            double before=Dimension_();
            var axis=AimAtHandle(); PressTrigger();
            Hand.position+=axis*0.02f; Frame(); double normal=Dimension_()-before;
            ReleaseTrigger();

            _frame.PaletteTrigger=true; Poll(); Assert.True(_xr.Precision);
            before=Dimension_();
            axis=AimAtHandle(); PressTrigger();
            Hand.position+=axis*0.02f; Frame(); double slow=Dimension_()-before;
            Assert.AreEqual(0.02*MmPerMetre,normal,1e-3);
            Assert.AreEqual(normal/10,slow,1e-3);

            // switching precision on mid-drag re-anchors instead of jumping
            ReleaseTrigger(); _frame.PaletteTrigger=false; Poll();
            before=Dimension_(); axis=AimAtHandle(); PressTrigger();
            Hand.position+=axis*0.01f; Frame(); double afterFast=Dimension_();
            _frame.PaletteTrigger=true; Poll(); Frame();
            Assert.AreEqual(afterFast,Dimension_(),1e-9,"no jump when precision toggles");
            Hand.position+=axis*0.01f; Frame();
            Assert.AreEqual(afterFast+0.01*MmPerMetre/10,Dimension_(),1e-3);
        }

        [Test] public void GripAloneIsAViewGrabAndNeverCapturesTheHandle()
        {
            UseInput(); ExtrudeWithHandle();
            double start=Dimension_();
            AimAtHandle(); _frame.PenGrip=true; Poll(); PressTrigger();
            Assert.False(Capturing,"Grip + Trigger no longer drags the handle");
            Assert.AreEqual(start,Dimension_());
            ReleaseTrigger(); _frame.PenGrip=false; Poll();
            Assert.AreEqual(DesignStatus.PreviewReady,Field<DesignSession>("_session").Status,"the draft was never touched");
        }

        [Test] public void AToggleTheSnapLockOnTheSketchOnly()
        {
            UseInput();
            _frame.A=true; Poll(); _frame.A=false; Poll();
            Assert.False(Field<bool>("_snapLocked"),"no sketch: A does nothing"); Assert.IsEmpty(_haptics);
            SketchOnXy(); _haptics.Clear();
            _frame.A=true; Poll();
            Assert.True(Field<bool>("_snapLocked")); CollectionAssert.AreEqual(new[]{HapticPulse.Tick},_haptics);
            _frame.A=false; Poll(); _frame.A=true; Poll();
            Assert.False(Field<bool>("_snapLocked"));
            Assert.AreEqual(0,_backend.Commits);
        }

        [Test] public void PenPressesOnTheSheetAddFirstThenSecondPointToTheDraftOnly()
        {
            UseInput(); SketchOnXy();
            var sketch=Field<SketchDraft>("_sketch");
            // The ray hits the sketch plane (z=0 in model space) at a known point.
            var frame=sketch.Frame;
            var plane=World(frame.ToModel(new CadPoint(0,0)));
            Hand.position=plane+new Vector3(0,0,-0.5f); Hand.rotation=Quaternion.LookRotation(Vector3.forward);
            int previews=_backend.Previews;
            PressTrigger(); ReleaseTrigger();
            Assert.True(Field<CadPoint?>("_first").HasValue,"first point");
            Hand.position+=new Vector3(0.02f,0,0);
            PressTrigger(); ReleaseTrigger();
            Assert.False(Field<CadPoint?>("_first").HasValue); Assert.AreEqual(1,sketch.Elements.Count,"second point closes the element");
            Assert.AreEqual(previews,_backend.Previews); Assert.AreEqual(0,_backend.Commits);
        }

        [Test] public void BackClosesKeypadThenRingThenPickerThenDiscardsTheLastDraftStep()
        {
            var shell=AttachShell(out var bench,out var sheet); UseInput(shell,bench,sheet);
            SketchOnXy();
            var sketch=Field<SketchDraft>("_sketch");
            sketch.Add(new SketchElement(SketchShape.Line,default,new CadPoint(10,4)));
            Do(AddConstraint); Assert.True(_workspace.Tabs.Any(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)),"a picker is open");
            typeof(DesignWorkspace).GetMethod("ShowRing",Flags).Invoke(_workspace,new object[]{UiSelectionKind.PlanarFace,Vector3.zero});
            Assert.True(Field<RingView>("_ring").Visible);
            Do("design.sketch.numeric"); Assert.True(shell.Palette.KeypadVisible); Assert.NotNull(_workspace.ActiveEntry);
            SetField("_first",(CadPoint?)new CadPoint(3,3));
            int previews=_backend.Previews;

            void PressX() { _frame.X=true; Poll(); _frame.X=false; Poll(); }
            PressX(); Assert.False(shell.Palette.KeypadVisible,"1: keypad"); Assert.True(Field<RingView>("_ring").Visible);
            Assert.True(_workspace.Tabs.Any(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)));
            PressX(); Assert.False(Field<RingView>("_ring").Visible,"2: ring"); Assert.True(_workspace.Tabs.Any(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)));
            PressX(); Assert.False(_workspace.Tabs.Any(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix)),"3: picker");
            Assert.True(Field<CadPoint?>("_first").HasValue); Assert.AreEqual(1,sketch.Elements.Count);
            PressX(); Assert.False(Field<CadPoint?>("_first").HasValue,"4: first point"); Assert.AreEqual(1,sketch.Elements.Count);
            PressX(); Assert.AreEqual(0,sketch.Elements.Count,"5: last element");
            PressX(); Assert.AreEqual(0,sketch.Elements.Count,"nothing left to discard");
            Assert.AreEqual(previews,_backend.Previews,"X never previews"); Assert.AreEqual(0,_backend.Commits,"X never touches the CAD");
        }

        [Test] public void ZoomScalesTheViewAboutItsCentreAndClampsToTheLayoutLimits()
        {
            UseInput(); SketchOnXy();
            var root=_view.transform; var session=Field<DesignSession>("_session"); var status=session.Status;
            var up=new XrInputFrame{PenTracked=true,PaletteTracked=true,PaletteStick=new Vector2(0,1)};
            float before=root.localScale.x;
            _xr.Poll(up,_clock+=0.016f);
            Assert.Greater(root.localScale.x,before,"stick up zooms in");
            for(int i=0;i<3000;i++) _xr.Poll(up,_clock+=0.016f);
            Assert.AreEqual((float)WorkbenchLayout.MaxScale,root.localScale.x,1e-6f);
            var down=up; down.PaletteStick=new Vector2(0,-1);
            for(int i=0;i<6000;i++) _xr.Poll(down,_clock+=0.016f);
            Assert.AreEqual((float)WorkbenchLayout.MinScale,root.localScale.x,1e-9f);
            Assert.AreEqual(status,session.Status,"zoom is view only"); Assert.AreEqual(0,_backend.Commits);
        }

        [Test] public void ZoomKeepsTheSheetCentreFixed()
        {
            var shell=AttachShell(out var bench,out var sheet); UseInput(shell,bench,sheet);
            SketchOnXy(); sheet.Snap(); bench.Snap();
            var sketch=Field<SketchDraft>("_sketch");
            var root=_view.transform;
            var centre=root.TransformPoint(CadCoordinates.ToLocal(sketch.Frame.OriginMm));
            _workspace.ZoomView(1,0.2f);
            Assert.Less(Vector3.Distance(centre,root.TransformPoint(CadCoordinates.ToLocal(sketch.Frame.OriginMm))),1e-4f);
        }

        [Test] public void StepNudgeTicksAndASuccessfulApplyPlaysTheSuccessPulse()
        {
            UseInput(); SketchOnXy(); Do(Extrude); _haptics.Clear();
            var right=new XrInputFrame{PenTracked=true,PaletteTracked=true,PenStick=new Vector2(0.9f,0)};
            _xr.Poll(right,_clock+=0.016f);
            CollectionAssert.AreEqual(new[]{HapticPulse.Tick},_haptics,"a step nudge ticks");
            Assert.AreNotEqual(10d,Dimension_(),"the chip value stepped");
            _xr.Poll(_frame,_clock+=0.016f);
            _haptics.Clear();
            Do(CommitIds.Preview); Do(CommitIds.Apply);
            CollectionAssert.Contains(_haptics,HapticPulse.Success);
        }

        [Test] public void HoveringTheHandleGivesAShortPulseOnEntryOnly()
        {
            UseInput(); ExtrudeWithHandle(); _haptics.Clear();
            AimAwayFromHandle(); Frame(); Assert.IsEmpty(_haptics);
            AimAtHandle(); Frame(); Frame();
            CollectionAssert.AreEqual(new[]{HapticPulse.Hover},_haptics);
        }

        [Test] public void LostTrackingBlocksPenInputAndNoOvrInputIsReadByTheWorkspace()
        {
            UseInput(); ExtrudeWithHandle();
            _frame.PenTracked=false; Poll();
            AimAtHandle(); PressTrigger();
            Assert.False(Capturing,"an untracked pen cannot start a capture");
            var source=System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,"XrSo/Xr/DesignWorkspace.cs"))
                +System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,"XrSo/Xr/DesignActions.cs"));
            StringAssert.DoesNotContain("OVRInput",source);
        }
    }
}
