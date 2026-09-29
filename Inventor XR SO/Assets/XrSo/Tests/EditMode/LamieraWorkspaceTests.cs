using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    /// <summary>M5 Lamiera workspace: draft, preview and Apply cycle, gates M5-01..M5-07 at the software level (no Quest, no Inventor).</summary>
    public class LamieraWorkspaceTests
    {
        private GameObject _root;
        private Material _material;
        private LamieraWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private LoadedScene _scene;
        private SelectionVisuals _visuals;
        private ControllerRay _ray;
        private Camera _eye;
        private HomePanel Panel => _workspace.GetComponentInChildren<HomePanel>(true);
        private string PanelText => string.Join("\n", Panel.GetComponentsInChildren<Text>().Select(t => t.text));

        private sealed class Backend : IDesignWorkspaceBackend, IDesignHistoryBackend, ISheetMetalBackend
        {
            public DocumentState State = new DocumentState("doc_bolt", "r", "v");
            public GlbModel Model;
            public bool SheetMetal = true, FlatExists;
            public int Bodies = 1, Previews, Commits, HistoryCalls, FlatMeshes;
            public JArray LastOperations;
            public Exception PreviewError;
            public FlatPatternException FlatFailure;
            public TaskCompletionSource<DesignPreview> PendingPreview;

            public Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct)
            {
                var json = SheetMetal ? JObject.Parse(@"{""is_sheet_metal"":true,""rule"":""Default_mm"",""thickness_mm"":2.0,
                    ""available_rules"":[{""rule"":""Default_mm"",""active"":true},{""rule"":""Alu_1mm"",""active"":false}],
                    ""body_count"":" + Bodies + @",""bend_count"":2,""flat_pattern"":" + (FlatExists
                        ? @"{""exists"":true,""length_mm"":120.5,""width_mm"":40,""bend_count"":2,""alignment"":{""type"":""kHorizontalAlignment""}}"
                        : @"{""exists"":false}") + "}")
                    : JObject.Parse(@"{""is_sheet_metal"":false}");
                return Task.FromResult(SheetMetalContext.Parse(json, state, state));
            }

            public Task<FlatPatternMesh> GetFlatPatternMeshAsync(DocumentState state, CancellationToken ct)
            {
                FlatMeshes++;
                if (FlatFailure != null) return Task.FromException<FlatPatternMesh>(FlatFailure);
                return Task.FromResult(new FlatPatternMesh(state.DocumentId, state.Revision, "h1", "a_" + new string('0', 64), Model, 120.5, 40, 2, 2.0));
            }

            public Task<DesignContext> GetDesignContextAsync(DocumentState state, CancellationToken ct) => Task.FromResult(
                DesignContext.Parse(new JObject
                {
                    ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "part",
                    ["faces"] = JArray.Parse(@"[{""id"":""ent_top"",""point_mm"":[0,0,2],""normal"":[0,0,1]},{""id"":""ent_bottom"",""point_mm"":[0,0,0],""normal"":[0,0,-1]}]"),
                    ["edges"] = JArray.Parse(@"[{""id"":""ent_edge"",""kind"":""line"",""points_mm"":[0,0,2,100,0,2]},{""id"":""ent_edge2"",""kind"":""line"",""points_mm"":[0,50,2,100,50,2]}]"),
                    ["sketches"] = JArray.Parse(@"[{""name"":""Contour"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},{""name"":""Open"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]}]"),
                    ["sketch_snapshots"] = JArray.Parse(@"[
                        {""sketch_name"":""Contour"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},""entities"":[
                            {""type"":""line"",""start_mm"":[0,0],""end_mm"":[50,0]},{""type"":""line"",""start_mm"":[50,0],""end_mm"":[50,30]},
                            {""type"":""line"",""start_mm"":[50,30],""end_mm"":[0,30]},{""type"":""line"",""start_mm"":[0,30],""end_mm"":[0,0]}]},
                        {""sketch_name"":""Open"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},""entities"":[
                            {""type"":""line"",""start_mm"":[0,0],""end_mm"":[50,0]},{""type"":""line"",""start_mm"":[50,0],""end_mm"":[50,30]}]}]"),
                }, state));

            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => Task.FromResult(State);

            public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
            {
                Previews++; LastOperations = operations;
                if (PreviewError != null) return Task.FromException<DesignPreview>(PreviewError);
                return PendingPreview?.Task ?? Task.FromResult(new DesignPreview("p" + Previews, state.DocumentId, state.Revision,
                    DateTimeOffset.UtcNow.AddMinutes(1), Model));
            }

            public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct)
            {
                Commits++;
                if (LastOperations != null && LastOperations.Any(o => (string)o["command"] == "create_flat_pattern")) FlatExists = true;
                State = new DocumentState("doc_bolt", "r2", "v2");
                return Task.FromResult(State);
            }

            public Task<DesignHistory> GetHistoryAsync(DocumentState state, CancellationToken ct) => Task.FromResult(new DesignHistory(state, true, true, "ticket"));
            public Task<DocumentState> ApplyHistoryAsync(DesignHistory history, bool redo, CancellationToken ct)
            { HistoryCalls++; State = new DocumentState("doc_bolt", "r3", "v3"); return Task.FromResult(State); }
        }

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Lamiera UI test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _eye = Child("Eye").AddComponent<Camera>();
            var hand = Child("Hand"); _ray = hand.AddComponent<ControllerRay>();
            var line = hand.AddComponent<LineRenderer>(); line.sharedMaterial = _material; _ray.Configure(hand.transform, line);
            _view = Child("Model").AddComponent<CadSceneView>(); _view.BodyMaterial = _material;
            _visuals = _view.gameObject.AddComponent<SelectionVisuals>(); _visuals.Configure(_view, null, null);
            _backend = new Backend { Model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)) };
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Bolt"",""definition_kind"":""part"",""definition_document_id"":""doc_bolt"",""children"":[]}}"));
            _scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { { "doc_bolt", _backend.Model } },
                new Dictionary<string, string> { { "doc_bolt", "a_test" } }, new List<string>());
            _view.Show(_scene);
            _workspace = Child("Workspace").AddComponent<LamieraWorkspace>();
            _workspace.Initialize(_view, _visuals, _ray, _eye.transform);
            Start(_backend);
        }

        private void Start(Backend backend)
        {
            _workspace.Bind(backend, backend);
            _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        private Button Button(string text) => Panel.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == text);
        private Button ButtonStartingWith(string text) => Panel.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text.StartsWith(text));
        private void Click(string text) { var b = Button(text); Assert.True(b.interactable, text); b.onClick.Invoke(); }
        private void ArmFlangeWithEdge() { Click("Flangia"); _workspace.Flange.ToggleEdge("ent_edge"); }
        private FlangeManipulator Manipulator => _view.GetComponentInChildren<FlangeManipulator>(true);
        private DesignPreviewView PreviewView => _view.transform.Find("Lamiera preview").GetComponent<DesignPreviewView>();
        [TearDown] public void Cleanup() { Object.DestroyImmediate(_root); Object.DestroyImmediate(_material); }

        // M5-01
        [Test] public void SheetMetalPartIsPrimaryAndOrdinaryPartIsNot()
        {
            Assert.True(_workspace.IsPrimary); Assert.True(_workspace.Mode.CanWrite);
            StringAssert.Contains("Regola: Default_mm", PanelText); StringAssert.Contains("Spessore: 2 mm", PanelText); StringAssert.Contains("Pieghe: 2", PanelText);
            _backend.SheetMetal = false; Start(_backend);
            Assert.False(_workspace.IsPrimary); Assert.False(_workspace.IsEnabled(CommandIds.Flange));
            StringAssert.Contains("non è in lamiera", PanelText);
            Assert.False(Panel.GetComponentsInChildren<Button>().Any(b => b.GetComponentInChildren<Text>().text == "Flangia"));
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void PrimaryChangedFiresOnTransitionsOnly()
        {
            var events = new List<bool>();
            _workspace.PrimaryChanged += events.Add;
            _workspace.Bind(_backend, _backend);
            _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
            Assert.AreEqual(new[] { false, true }, events.ToArray());
        }

        [Test] public void ButtonsUseTheSharedCommandIds()
        {
            var names = Panel.GetComponentsInChildren<Button>().Select(b => b.name).ToArray();
            foreach (var id in new[] { CommandIds.Flange, CommandIds.FlatPatternCreate, CommandIds.SheetMetalFace, CommandIds.SheetMetalCut,
                CommandIds.SheetMetalRule, CommandIds.Undo, CommandIds.Redo })
                CollectionAssert.Contains(names, id);
        }

        // M5-03 (software part): preview mandatory, Apply only after a rendered preview
        [Test] public void FlangeNeedsRenderedPreviewBeforeApplyAndCommitsOnce()
        {
            ArmFlangeWithEdge();
            Assert.AreEqual(DesignStatus.Draft, _workspace.Session.Status);
            Assert.False(Button("Applica").interactable); Assert.False(_workspace.IsEnabled(CommandIds.Apply));
            Assert.True(Manipulator.Visible);
            Click("Anteprima");
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.True(PreviewView.IsShowing); Assert.True(Button("Applica").interactable);
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("sheet_metal_flange", (string)op["command"]);
            Assert.AreEqual(10, (double)op["arguments"]["height_mm"]); Assert.AreEqual(90, (double)op["arguments"]["angle_degrees"]);
            Assert.AreEqual("ent_edge", (string)op["arguments"]["edge_ids"][0]); Assert.AreEqual("outer", (string)op["arguments"]["height_datum"]);
            Click("Applica");
            Assert.AreEqual(1, _backend.Commits); Assert.False(PreviewView.IsShowing);
        }

        [UnityTest] public IEnumerator NumericFieldEditsTheSharedDraftAndInvalidatesApplyUntilNewPreview()
        {
            ArmFlangeWithEdge(); Click("Anteprima");
            Assert.True(Button("Applica").interactable);
            _backend.PendingPreview = new TaskCompletionSource<DesignPreview>();
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight));
            Assert.AreEqual(QuantityUnit.Millimeters, _workspace.ArmedField.Unit);
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 25));
            Assert.AreEqual(25, _workspace.Flange.HeightMm); Assert.AreEqual(2, _backend.Previews);
            Assert.AreEqual(25, (double)_backend.LastOperations[0]["arguments"]["height_mm"]);
            Assert.False(_workspace.Session.CanApply); Assert.False(Button("Applica").interactable);
            _backend.PendingPreview.SetResult(new DesignPreview("p9", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model));
            yield return null; yield return null; // the awaiting session resumes on Unity's context
            Assert.True(Button("Applica").interactable); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ArmedFieldRefusesOtherFieldsNonFiniteAndOutOfRangeValues()
        {
            Assert.False(_workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight), "Flangia must be armed first.");
            Click("Flangia");
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight));
            var field = _workspace.ArmedField;
            Assert.AreEqual(LamieraWorkspace.FieldFlangeHeight, field.Id); Assert.Greater(field.Min, 0); Assert.AreEqual(10000, field.Max);
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeAngle, 45)); Assert.AreEqual(90, _workspace.Flange.AngleDegrees);
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, double.NaN));
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, double.PositiveInfinity));
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 20000));
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, -3));
            Assert.AreEqual(10, _workspace.Flange.HeightMm); StringAssert.Contains("intervallo", _workspace.LastFieldError);
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 12.5)); Assert.AreEqual(12.5, _workspace.Flange.HeightMm);
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldFlangeAngle));
            Assert.AreEqual(QuantityUnit.Degrees, _workspace.ArmedField.Unit);
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeAngle, 45)); Assert.AreEqual(45, _workspace.Flange.AngleDegrees);
            _workspace.DisarmField(); Assert.IsNull(_workspace.ArmedField);
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 5));
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ManipulatorReleaseAsksForPreviewNeverCommits()
        {
            ArmFlangeWithEdge();
            _workspace.Flange.ApplyManipulatorHeight(30); _workspace.Flange.ReleaseManipulator();
            Assert.AreEqual(0, _backend.Previews, "Dragging alone never computes a preview.");
            typeof(LamieraWorkspace).GetMethod("PreviewPending", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_workspace, null);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.AreEqual(30, (double)_backend.LastOperations[0]["arguments"]["height_mm"]);
            Assert.False(_workspace.Flange.PreviewRequested);
        }

        [Test] public void ApplyByCommandIdOnlyExplainsAndNeverCommits()
        {
            ArmFlangeWithEdge(); Click("Anteprima");
            Assert.True(_workspace.IsEnabled(CommandIds.Apply));
            Assert.True(_workspace.Invoke(CommandIds.Apply));
            Assert.AreEqual(0, _backend.Commits);
            StringAssert.Contains("Conferma premendo Applica", PanelText);
            Assert.False(_workspace.IsEnabled(CommandIds.Chamfer)); Assert.False(_workspace.Invoke(CommandIds.Chamfer));
            Assert.False(_workspace.Invoke(CommandIds.Measure));
        }

        [Test] public void CancelDiscardsTheDraftAndClearsEveryVisual()
        {
            ArmFlangeWithEdge(); Click("Anteprima");
            Assert.True(_workspace.IsEnabled(CommandIds.CancelDraft));
            Click("Annulla comando");
            Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status); Assert.AreEqual(0, _workspace.Flange.EdgeIds.Count);
            Assert.False(PreviewView.IsShowing); Assert.False(Manipulator.Visible);
            Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed); Assert.AreSame(_material, _view.Instances[0].Bodies[0].Renderer.sharedMaterial);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void FailedPreviewIsReadableAndKeepsTheDraftEditable()
        {
            ArmFlangeWithEdge(); _backend.PreviewError = new InvalidOperationException("Altezza troppo grande per il bordo");
            Click("Anteprima");
            StringAssert.Contains("Altezza troppo grande", PanelText); StringAssert.Contains("resta modificabile", PanelText);
            Assert.False(Button("Applica").interactable); Assert.True(_workspace.IsEnabled(CommandIds.CancelDraft));
            _backend.PreviewError = null; _workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight);
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 5));
            Assert.True(Button("Applica").interactable); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void LocalValidationErrorLeavesNoWriteAndNoPreview()
        {
            Click("Flangia");
            _workspace.Flange.SetHeight(15); // no edge selected
            StringAssert.Contains("Seleziona da 1 a 256 spigoli", PanelText);
            Click("Anteprima"); Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.False(Button("Applica").interactable);
        }

        // M5-04
        [Test] public void FaceFromClosedSketchShowsProfilePreviewsAndApplies()
        {
            Assert.True(_workspace.Invoke(CommandIds.SheetMetalFace));
            StringAssert.Contains("Contour • chiuso", PanelText); StringAssert.Contains("Open • aperto", PanelText);
            Click("Contour • chiuso");
            Assert.AreEqual(1, _backend.Previews);
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("sheet_metal_face", (string)op["command"]); Assert.AreEqual("Contour", (string)op["arguments"]["sketch_name"]);
            StringAssert.Contains("Profilo usato: Contour", PanelText);
            Assert.True(_view.transform.Find("Lamiera local geometry/Selected edges") != null, "The profile is highlighted.");
            Assert.True(Button("Applica").interactable); Click("Applica"); Assert.AreEqual(1, _backend.Commits);
        }

        [Test] public void OpenProfileIsRefusedWithAReadableReasonAndNoDraft()
        {
            _workspace.Invoke(CommandIds.SheetMetalCut);
            Click("Open • aperto");
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status);
            StringAssert.Contains("Profilo aperto", PanelText); Assert.False(Button("Applica").interactable);
            Click("Contour • chiuso");
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("sheet_metal_cut", (string)op["command"]); Assert.AreEqual("thickness", (string)op["arguments"]["extent"]);
            Click("Direzione: positiva"); Assert.AreEqual("negative", (string)_backend.LastOperations[0]["arguments"]["direction"]);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void RuleAndThicknessAreASeparateDraft()
        {
            Assert.True(_workspace.Invoke(CommandIds.SheetMetalRule));
            Click("Alu_1mm");
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("set_sheet_metal_rule", (string)op["command"]); Assert.AreEqual("Alu_1mm", (string)op["arguments"]["rule"]);
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldThickness));
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldThickness, 1.5));
            Assert.AreEqual(1.5, (double)_backend.LastOperations[0]["arguments"]["thickness_mm"]);
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldThickness, 80));
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void UndoAndRedoUseTheXrHistory()
        {
            Assert.True(_workspace.IsEnabled(CommandIds.Undo)); Assert.True(_workspace.IsEnabled(CommandIds.Redo));
            Click("Annulla modifica XR"); Assert.AreEqual(1, _backend.HistoryCalls);
            // The document moved: history stays blocked until the scene of the new revision arrives.
            Assert.False(_workspace.IsEnabled(CommandIds.Undo)); Assert.False(_workspace.IsEnabled(CommandIds.Redo));
        }

        // M5-05: distinct outcomes
        [Test] public void FlatPatternOutcomesAreDistinct()
        {
            Assert.True(_workspace.IsEnabled(CommandIds.FlatPatternCreate));
            _backend.FlatExists = true; Start(_backend);
            Assert.False(_workspace.IsEnabled(CommandIds.FlatPatternCreate)); StringAssert.Contains("presente", PanelText);
            Assert.AreEqual(FlatPatternCreation.AlreadyExists, _workspace.Mode.CheckFlatPattern(out var exists)); StringAssert.Contains("esiste già", exists);
            _backend.FlatExists = false; _backend.Bodies = 2; Start(_backend);
            Assert.False(_workspace.IsEnabled(CommandIds.FlatPatternCreate));
            Assert.AreEqual(FlatPatternCreation.MultiBody, _workspace.Mode.CheckFlatPattern(out var multi)); StringAssert.Contains("un solo body", multi);
            Assert.AreEqual(0, _backend.Previews);
        }

        // M5-05 / M5-06
        [Test] public void CreateFlatPatternThenShowBesideFoldedPartAndDetachIsViewOnly()
        {
            Assert.True(_workspace.Invoke(CommandIds.FlatPatternCreate));
            Assert.AreEqual("create_flat_pattern", (string)_backend.LastOperations[0]["command"]);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Click("Applica");
            Assert.AreEqual(1, _backend.Commits);
            // After the commit the context is re-read; the created pattern is fetched and shown, verified for the new revision.
            Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State);
            Assert.AreEqual("r2", _workspace.FlatPattern.Asset.Revision);
            var root = _view.transform.Find("Flat pattern (view only)");
            Assert.NotNull(root); Assert.True(root.gameObject.activeSelf);
            Assert.AreEqual(FlatPatternDisplay.NoRaycastLayer, root.gameObject.layer);
            Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length); Assert.AreEqual(0, root.GetComponentsInChildren<CadBody>(true).Length);
            var label = _workspace.GetComponentInChildren<FlatPatternDisplay>(true);
            StringAssert.Contains("Sviluppo — sola vista", label.LabelText); StringAssert.Contains("120,5", label.LabelText);
            int previews = _backend.Previews, commits = _backend.Commits;
            var before = root.localPosition;
            _workspace.FlatPattern.Detach(); Assert.True(_workspace.FlatPattern.MoveLocal(0.25, 0.1, 0));
            Assert.That(root.localPosition.x, Is.EqualTo(before.x + 0.25f).Within(1e-5)); Assert.That(root.localPosition.y, Is.EqualTo(before.y + 0.1f).Within(1e-5));
            Assert.AreEqual(previews, _backend.Previews); Assert.AreEqual(commits, _backend.Commits, "Moving the view never touches CAD.");
            Assert.False(_workspace.FlatPattern.IsCadSelectable);
        }

        [Test] public void FlatPatternFailureShowsItsReasonAndNoEstimate()
        {
            _backend.FlatExists = true; _backend.FlatFailure = new FlatPatternException(FlatPatternFailure.TooLarge, "Lo sviluppo supera il budget geometrico del visore.");
            Start(_backend); Click("Mostra sviluppo");
            Assert.AreEqual(FlatPatternState.Unavailable, _workspace.FlatPattern.State);
            StringAssert.Contains("budget geometrico", PanelText);
            Assert.Null(_view.transform.Find("Flat pattern (view only)"));
        }

        // M5-07
        [Test] public void OfflineKeepsPatternReadOnlyBlocksWritesAndReverifiesOnReconnect()
        {
            _backend.FlatExists = true; Start(_backend); Click("Mostra sviluppo");
            Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State);
            _workspace.SetOnline(false);
            Assert.AreEqual(FlatPatternState.StaleReadOnly, _workspace.FlatPattern.State);
            Assert.AreEqual(SheetMetalAvailability.Offline, _workspace.Mode.Availability);
            Assert.False(_workspace.IsEnabled(CommandIds.Flange)); StringAssert.Contains("Offline", PanelText);
            StringAssert.Contains("sola ispezione", _workspace.GetComponentInChildren<FlatPatternDisplay>(true).LabelText);
            int meshes = _backend.FlatMeshes;
            _workspace.SetOnline(true);
            Assert.AreEqual(meshes + 1, _backend.FlatMeshes); Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State);
        }

        [Test] public void RevisionChangeDropsDraftPreviewSelectionAndHidesThePattern()
        {
            _backend.FlatExists = true; Start(_backend); Click("Mostra sviluppo");
            ArmFlangeWithEdge(); Click("Anteprima");
            Assert.True(PreviewView.IsShowing);
            _workspace.SetDocumentState(new DocumentState("doc_bolt", "r9", "v9"));
            Assert.AreEqual(0, _workspace.Flange.EdgeIds.Count); Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status);
            Assert.False(PreviewView.IsShowing); Assert.False(Manipulator.Visible);
            Assert.AreEqual(FlatPatternState.Hidden, _workspace.FlatPattern.State); Assert.Null(_view.transform.Find("Flat pattern (view only)"));
            Assert.False(_workspace.Session.CanApply); Assert.AreEqual(0, _backend.Commits);
            Assert.False(_workspace.IsEnabled(CommandIds.Flange), "The scene still shows the old revision.");
        }

        [UnityTest] public IEnumerator LatePreviewAfterCloseCannotResurrectGhostOrApply()
        {
            _backend.PendingPreview = new TaskCompletionSource<DesignPreview>();
            ArmFlangeWithEdge(); Click("Anteprima"); _workspace.Close();
            _backend.PendingPreview.SetResult(new DesignPreview("late", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model));
            yield return null; yield return null;
            _workspace.Open();
            Assert.False(PreviewView.IsShowing); Assert.False(_workspace.Session.CanApply); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void SceneRebuildInvalidatesTheRenderedPreview()
        {
            ArmFlangeWithEdge(); Click("Anteprima");
            Assert.True(_workspace.Session.CanApply);
            _view.Show(_scene); _workspace.SetScene(_scene);
            Assert.False(_workspace.Session.CanApply); Assert.False(PreviewView.IsShowing);
            Click("Anteprima"); Assert.True(_workspace.Session.CanApply);
        }

        [Test] public void ClosingReleasesPickingAndHidesPanelAndManipulator()
        {
            ArmFlangeWithEdge(); _workspace.Close();
            Assert.False(_workspace.Active); Assert.False(Panel.gameObject.activeSelf); Assert.False(Manipulator.Visible); Assert.True(_ray.CanPick);
            Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status);
            _workspace.Open(); Assert.False(_ray.CanPick);
        }

        [TestCase("tools")][TestCase("flange")]
        public void PanelButtonsStayInsidePanel(string screen)
        {
            if (screen == "flange") ArmFlangeWithEdge();
            var rect = (RectTransform)Panel.transform; LayoutRebuilder.ForceRebuildLayoutImmediate(rect); Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            foreach (var button in Panel.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var p = rect.InverseTransformPoint(corner);
                    Assert.That(p.x, Is.InRange(rect.rect.xMin - 1, rect.rect.xMax + 1), button.name);
                    Assert.That(p.y, Is.InRange(rect.rect.yMin - 1, rect.rect.yMax + 1), button.name);
                }
            }
        }
    }
}
