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
    /// <summary>
    /// Lamiera workspace on the M6 shell: draft, preview and Apply cycle, gates M5-01..M5-07 at the software level (no Quest, no
    /// Inventor). Every command goes through the declared actions by stable id, like the palette, ring and commit bar do.
    /// </summary>
    public class LamieraWorkspaceTests
    {
        private const string Rule = LamieraWorkspace.IdRule, FlangeAction = LamieraWorkspace.IdFlange, Height = LamieraWorkspace.IdFlangeHeight,
            Angle = LamieraWorkspace.IdFlangeAngle, Datum = LamieraWorkspace.IdFlangeDatum, ClearEdges = LamieraWorkspace.IdFlangeClear,
            Undo = LamieraWorkspace.IdUndo, Redo = LamieraWorkspace.IdRedo, Face = LamieraWorkspace.IdFace, Cut = LamieraWorkspace.IdCut,
            ChangeSketch = LamieraWorkspace.IdSketchChange, Extent = LamieraWorkspace.IdCutExtent, Direction = LamieraWorkspace.IdCutDirection,
            AcrossBends = LamieraWorkspace.IdCutAcrossBends, FlatCreate = LamieraWorkspace.IdFlatCreate, FlatShow = LamieraWorkspace.IdFlatShow,
            FlatHide = LamieraWorkspace.IdFlatHide, FlatDetach = LamieraWorkspace.IdFlatDetach, FlatAttach = LamieraWorkspace.IdFlatAttach;

        private GameObject _root;
        private Material _material;
        private LamieraWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private LoadedScene _scene;
        private SelectionVisuals _visuals;
        private ControllerRay _ray;
        private Camera _eye;
        private readonly List<string> _hud = new List<string>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Backend : IDesignWorkspaceBackend, IDesignHistoryBackend, ISheetMetalBackend
        {
            public DocumentState State = new DocumentState("doc_bolt", "r", "v");
            public GlbModel Model;
            public bool SheetMetal = true, FlatExists;
            public int Bodies = 1, Previews, Commits, HistoryCalls, FlatMeshes, ExtraRules, ExtraSketches;
            public JArray LastOperations;
            public Exception PreviewError, CommitError;
            public FlatPatternException FlatFailure;
            public TaskCompletionSource<DesignPreview> PendingPreview;

            public Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct)
            {
                var rules = @"{""rule"":""Default_mm"",""active"":true},{""rule"":""Alu_1mm"",""active"":false}"
                    + string.Concat(Enumerable.Range(0, ExtraRules).Select(i => @",{""rule"":""Extra_" + i + @""",""active"":false}"));
                var json = SheetMetal ? JObject.Parse(@"{""is_sheet_metal"":true,""rule"":""Default_mm"",""thickness_mm"":2.0,
                    ""available_rules"":[" + rules + @"],
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

            public Task<DesignContext> GetDesignContextAsync(DocumentState state, CancellationToken ct)
            {
                var sketches = JArray.Parse(@"[{""name"":""Contour"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},{""name"":""Open"",""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]}]");
                for (int i = 0; i < ExtraSketches; i++)
                    sketches.Add(new JObject { ["name"] = "Extra_" + i, ["origin_mm"] = new JArray(0, 0, 0), ["x_axis"] = new JArray(1, 0, 0), ["y_axis"] = new JArray(0, 1, 0) });
                return Task.FromResult(DesignContext.Parse(new JObject
                {
                    ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "part",
                    ["faces"] = JArray.Parse(@"[{""id"":""ent_top"",""point_mm"":[0,0,2],""normal"":[0,0,1]},{""id"":""ent_bottom"",""point_mm"":[0,0,0],""normal"":[0,0,-1]}]"),
                    ["edges"] = JArray.Parse(@"[{""id"":""ent_edge"",""kind"":""line"",""points_mm"":[0,0,2,100,0,2]},{""id"":""ent_edge2"",""kind"":""line"",""points_mm"":[0,50,2,100,50,2]}]"),
                    ["sketches"] = sketches,
                    ["sketch_snapshots"] = JArray.Parse(@"[
                        {""sketch_name"":""Contour"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},""entities"":[
                            {""type"":""line"",""start_mm"":[0,0],""end_mm"":[50,0]},{""type"":""line"",""start_mm"":[50,0],""end_mm"":[50,30]},
                            {""type"":""line"",""start_mm"":[50,30],""end_mm"":[0,30]},{""type"":""line"",""start_mm"":[0,30],""end_mm"":[0,0]}]},
                        {""sketch_name"":""Open"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},""entities"":[
                            {""type"":""line"",""start_mm"":[0,0],""end_mm"":[50,0]},{""type"":""line"",""start_mm"":[50,0],""end_mm"":[50,30]}]}]"),
                }, state));
            }

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
                if (CommitError != null) return Task.FromException<DocumentState>(CommitError);
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
            _hud.Clear(); _haptics.Clear(); _xr = null; _previousSink = Haptics.Sink; Haptics.Sink = (pulse, pen) => _haptics.Add(pulse);
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
            _workspace.HudMessage += _hud.Add;
            _workspace.Initialize(_view, _visuals, _ray, _eye.transform);
            Start(_backend);
        }

        private void Start(Backend backend)
        {
            _workspace.Bind(backend, backend);
            _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }

        [TearDown] public void Cleanup()
        {
            Haptics.Sink = _previousSink;
            // The chips and the ring are free-standing canvases (not children of the workspace).
            foreach (var name in new[] { "_heightChip", "_angleChip", "_thicknessChip", "_ring" })
            {
                var view = _workspace == null ? null : typeof(LamieraWorkspace).GetField(name, Flags).GetValue(_workspace) as Component;
                if (view != null) Object.DestroyImmediate(view.gameObject);
            }
            foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r);
            _roots.Clear();
            Object.DestroyImmediate(_root); Object.DestroyImmediate(_material);
        }

        private XrAction Act(string id) => _workspace.Actions.Single(a => a.Id == id);
        private bool Enabled(string id) => Act(id).Enabled;
        /// <summary>Invokes a declared action by id, exactly like the palette, ring or commit bar would.</summary>
        private void Do(string id) { var a = Act(id); Assert.True(a.Enabled, id + ": " + a.DisabledReason); Assert.True(a.TryInvoke(), id); }
        private XrAction PickItem(string label) => _workspace.Actions.Single(a => a.Id.StartsWith(LamieraWorkspace.IdPickPrefix) && a.Label == label);
        private void Pick(string label) { var a = PickItem(label); Assert.True(a.Enabled, label); Assert.True(a.TryInvoke(), label); }
        private T Field<T>(string name) => (T)typeof(LamieraWorkspace).GetField(name, Flags).GetValue(_workspace);
        private string AllHud() => string.Join("\n", _hud);
        private void ArmFlangeWithEdge() { Do(FlangeAction); _workspace.Flange.ToggleEdge("ent_edge"); }
        private FlangeManipulator Manipulator => _view.GetComponentInChildren<FlangeManipulator>(true);
        private DesignPreviewView PreviewView => _view.transform.Find("Lamiera preview").GetComponent<DesignPreviewView>();
        private void Commit(params double[] values)
        {
            foreach (var value in values)
            {
                var entry = _workspace.ActiveEntry; Assert.NotNull(entry, "a numeric entry is waiting for the keypad"); Assert.True(entry.Editing);
                entry.CommitValue(value, out var reason); Assert.AreEqual("", reason);
            }
        }

        // M5-01
        [Test] public void SheetMetalPartIsPrimaryAndOrdinaryPartIsNot()
        {
            Assert.True(_workspace.IsPrimary); Assert.True(_workspace.Mode.CanWrite);
            StringAssert.Contains("Regola: Default_mm", _workspace.StatusSummary()); StringAssert.Contains("Spessore: 2 mm", _workspace.StatusSummary());
            StringAssert.Contains("Pieghe: 2", _workspace.StatusSummary());
            StringAssert.Contains("Regola: Default_mm", AllHud(), "the status summary is announced on the HUD when Lamiera opens");
            _hud.Clear(); _backend.SheetMetal = false; Start(_backend);
            Assert.False(_workspace.IsPrimary); Assert.False(_workspace.IsEnabled(CommandIds.Flange));
            StringAssert.Contains("non è in lamiera", AllHud());
            Assert.False(Enabled(FlangeAction)); StringAssert.Contains("non è in lamiera", Act(FlangeAction).DisabledReason);
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

        [Test] public void ActionsUseTheStableIdsAndTheSharedCommandIds()
        {
            var ids = _workspace.Actions.Select(a => a.Id).ToArray();
            foreach (var id in new[] { Rule, FlangeAction, Face, Cut, FlatCreate, Undo, Redo, FlatShow, FlatHide, FlatDetach, FlatAttach,
                CommitIds.Preview, CommitIds.Apply, CommitIds.Cancel, CommitIds.Recover })
                CollectionAssert.Contains(ids, id);
            Assert.True(_workspace.Invoke(CommandIds.Flange), "the voice command id runs the same action");
            Assert.AreEqual(SheetMetalCommand.Flange, _workspace.Mode.Armed);
        }

        // M5-03 (software part): preview mandatory, Apply only after a rendered preview
        [Test] public void FlangeNeedsRenderedPreviewBeforeApplyAndCommitsOnce()
        {
            ArmFlangeWithEdge();
            Assert.AreEqual(DesignStatus.Draft, _workspace.Session.Status);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(_workspace.IsEnabled(CommandIds.Apply));
            Assert.True(Manipulator.Visible);
            Do(CommitIds.Preview);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.True(PreviewView.IsShowing); Assert.True(Enabled(CommitIds.Apply));
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("sheet_metal_flange", (string)op["command"]);
            Assert.AreEqual(10, (double)op["arguments"]["height_mm"]); Assert.AreEqual(90, (double)op["arguments"]["angle_degrees"]);
            Assert.AreEqual("ent_edge", (string)op["arguments"]["edge_ids"][0]); Assert.AreEqual("outer", (string)op["arguments"]["height_datum"]);
            Do(CommitIds.Apply);
            Assert.AreEqual(1, _backend.Commits); Assert.False(PreviewView.IsShowing);
            Assert.AreEqual(CommitBarPhase.Applied, _workspace.CommitBar.Phase, "MarkApplied after a successful apply");
        }

        [Test] public void ConfirmingTheSameValueStillPreviewsADraftWithoutPreview()
        {
            ArmFlangeWithEdge();
            Assert.AreEqual(DesignStatus.Draft, _workspace.Session.Status); Assert.AreEqual(0, _backend.Previews);
            Do(LamieraWorkspace.IdFlangeHeight);
            Commit(_workspace.Flange.HeightMm);   // the keypad confirms the value the draft already has
            Assert.AreEqual(1, _backend.Previews, "an unchanged value still asks for the missing preview");
            Assert.AreEqual(0, _backend.Commits);
        }

        [UnityTest] public IEnumerator NumericFieldEditsTheSharedDraftAndInvalidatesApplyUntilNewPreview()
        {
            ArmFlangeWithEdge(); Do(CommitIds.Preview);
            Assert.True(Enabled(CommitIds.Apply));
            _backend.PendingPreview = new TaskCompletionSource<DesignPreview>();
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight));
            Assert.AreEqual(QuantityUnit.Millimeters, _workspace.ArmedField.Unit);
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 25));
            Assert.AreEqual(25, _workspace.Flange.HeightMm); Assert.AreEqual(2, _backend.Previews);
            Assert.AreEqual(25, (double)_backend.LastOperations[0]["arguments"]["height_mm"]);
            Assert.False(_workspace.Session.CanApply); Assert.False(Enabled(CommitIds.Apply));
            _backend.PendingPreview.SetResult(new DesignPreview("p9", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model));
            yield return null; yield return null; // the awaiting session resumes on Unity's context
            Assert.True(Enabled(CommitIds.Apply)); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ArmedFieldRefusesOtherFieldsNonFiniteAndOutOfRangeValues()
        {
            Assert.False(_workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight), "Flangia must be armed first.");
            Do(FlangeAction);
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
            typeof(LamieraWorkspace).GetMethod("PreviewPending", Flags).Invoke(_workspace, null);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.AreEqual(30, (double)_backend.LastOperations[0]["arguments"]["height_mm"]);
            Assert.False(_workspace.Flange.PreviewRequested);
        }

        [Test] public void ApplyByCommandIdOnlyExplainsAndNeverCommits()
        {
            ArmFlangeWithEdge(); Do(CommitIds.Preview);
            Assert.True(_workspace.IsEnabled(CommandIds.Apply));
            _hud.Clear();
            Assert.True(_workspace.Invoke(CommandIds.Apply));
            Assert.AreEqual(0, _backend.Commits);
            StringAssert.Contains("Conferma premendo Applica", _workspace.Notice); StringAssert.Contains("Conferma premendo Applica", AllHud());
            Assert.False(_workspace.IsEnabled(CommandIds.Chamfer)); Assert.False(_workspace.Invoke(CommandIds.Chamfer));
            Assert.False(_workspace.Invoke(CommandIds.Measure));
        }

        [Test] public void VoiceSurfaceUsesTheSameActionsAndNeverVoiceInvokesApply()
        {
            Assert.IsNull(_workspace.VoicePanel);
            var labels = _workspace.VoiceActions.ToList();
            foreach (var label in new[] { "Flangia", "Regola / Spessore", "Faccia da schizzo", "Taglio da schizzo", "Crea sviluppo", "Annulla modifica XR" })
                CollectionAssert.Contains(labels.Select(l => l.label).ToList(), label);
            Assert.True(_workspace.InvokeVoiceAction("Flangia")); Assert.AreEqual(SheetMetalCommand.Flange, _workspace.Mode.Armed);
            _workspace.Flange.ToggleEdge("ent_edge"); Do(CommitIds.Preview);
            Assert.True(Enabled(CommitIds.Apply));
            Assert.False(_workspace.InvokeVoiceAction("Applica"), "the commit bar action is never voice-invoked");
            Assert.AreEqual(0, _backend.Commits);
            Assert.True(_workspace.InvokeVoiceAction("Annulla comando")); Assert.AreEqual(CommitBarPhase.Empty, _workspace.CommitBar.Phase);
        }

        [Test] public void CancelDiscardsTheDraftAndClearsEveryVisual()
        {
            ArmFlangeWithEdge(); Do(CommitIds.Preview);
            Assert.True(_workspace.IsEnabled(CommandIds.CancelDraft));
            Do(CommitIds.Cancel);
            Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status); Assert.AreEqual(0, _workspace.Flange.EdgeIds.Count);
            Assert.False(PreviewView.IsShowing); Assert.False(Manipulator.Visible);
            Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed); Assert.AreSame(_material, _view.Instances[0].Bodies[0].Renderer.sharedMaterial);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void FailedPreviewIsReadableOnTheHudAndKeepsTheDraftEditable()
        {
            ArmFlangeWithEdge(); _backend.PreviewError = new InvalidOperationException("Altezza troppo grande per il bordo");
            _hud.Clear(); Do(CommitIds.Preview);
            StringAssert.Contains("Altezza troppo grande", AllHud()); StringAssert.Contains("resta modificabile", AllHud());
            StringAssert.DoesNotContain("Altezza troppo grande", _workspace.CommitBar.Message, "the bar only carries the short message");
            Assert.AreEqual(CommitBarPhase.Error, _workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.True(_workspace.IsEnabled(CommandIds.CancelDraft));
            _backend.PreviewError = null; _workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight);
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 5));
            Assert.True(Enabled(CommitIds.Apply)); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void LocalValidationErrorLeavesNoWriteAndNoPreview()
        {
            Do(FlangeAction);
            _hud.Clear();
            _workspace.Flange.SetHeight(15); // no edge selected
            StringAssert.Contains("Seleziona da 1 a 256 spigoli", AllHud());
            Assert.AreEqual(CommitBarPhase.Error, _workspace.CommitBar.Phase);
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.False(Enabled(CommitIds.Apply));
        }

        // M5-04
        [Test] public void FaceFromClosedSketchShowsProfilePreviewsAndApplies()
        {
            Assert.True(_workspace.Invoke(CommandIds.SheetMetalFace));
            Assert.True(PickItem("Contour • chiuso").Enabled); Assert.True(PickItem("Open • aperto").Enabled);
            Pick("Contour • chiuso");
            Assert.AreEqual(1, _backend.Previews);
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("sheet_metal_face", (string)op["command"]); Assert.AreEqual("Contour", (string)op["arguments"]["sketch_name"]);
            Assert.AreEqual("Contour", Field<string>("_sketchName"));
            Assert.True(_view.transform.Find("Lamiera local geometry/Selected edges") != null, "The profile is highlighted.");
            Assert.True(Enabled(CommitIds.Apply)); Do(CommitIds.Apply); Assert.AreEqual(1, _backend.Commits);
        }

        [Test] public void OpenProfileIsRefusedWithAReadableReasonAndNoDraft()
        {
            _workspace.Invoke(CommandIds.SheetMetalCut);
            _hud.Clear();
            Pick("Open • aperto");
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status);
            StringAssert.Contains("Profilo aperto", AllHud()); Assert.False(Enabled(CommitIds.Apply));
            Pick("Contour • chiuso");   // the list stayed on offer
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("sheet_metal_cut", (string)op["command"]); Assert.AreEqual("thickness", (string)op["arguments"]["extent"]);
            StringAssert.Contains("positiva", Act(Direction).Label);
            Do(Direction); Assert.AreEqual("negative", (string)_backend.LastOperations[0]["arguments"]["direction"]);
            StringAssert.Contains("negativa", Act(Direction).Label);
            Assert.True(Enabled(AcrossBends)); Do(Extent); Assert.False(Enabled(AcrossBends), "only with the thickness extent");
            Assert.AreEqual("through_all", (string)_backend.LastOperations[0]["arguments"]["extent"]);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ChangeSketchReopensTheListWithoutLosingTheCommand()
        {
            Assert.False(Enabled(ChangeSketch)); Assert.AreEqual("Scegli prima Faccia o Taglio da schizzo.", Act(ChangeSketch).DisabledReason);
            Do(Cut); Pick("Contour • chiuso");
            Do(ChangeSketch);
            Assert.AreEqual(SheetMetalCommand.Cut, _workspace.Mode.Armed); Assert.True(PickItem("Contour • chiuso").Enabled);
        }

        [Test] public void RuleAndThicknessAreASeparateDraft()
        {
            Assert.True(_workspace.Invoke(CommandIds.SheetMetalRule));
            Pick("Alu_1mm");
            var op = (JObject)_backend.LastOperations[0];
            Assert.AreEqual("set_sheet_metal_rule", (string)op["command"]); Assert.AreEqual("Alu_1mm", (string)op["arguments"]["rule"]);
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldThickness));
            Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldThickness, 1.5));
            Assert.AreEqual(1.5, (double)_backend.LastOperations[0]["arguments"]["thickness_mm"]);
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldThickness, 80));
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ThicknessIsTheLastEntryOfTheRuleListAndUsesTheKeypad()
        {
            Do(Rule);
            Assert.True(PickItem("● Default_mm").Enabled, "the active rule is marked");
            Pick("Spessore: 2 mm");
            Assert.AreEqual(LamieraWorkspace.FieldThickness, _workspace.ActiveEntry.Id);
            Assert.AreEqual(LamieraWorkspace.FieldThickness, _workspace.ArmedField.Id);
            Commit(1.5);
            Assert.AreEqual(1.5, (double)_backend.LastOperations[0]["arguments"]["thickness_mm"]);
            Assert.IsNull(_workspace.ActiveEntry); Assert.True(Enabled(CommitIds.Apply)); Assert.AreEqual(0, _backend.Commits);
            Do(Rule);   // the same screen: the list is back, the draft is kept
            Assert.True(PickItem("Spessore: 1,5 mm").Enabled);
        }

        [Test] public void UndoAndRedoUseTheXrHistory()
        {
            Assert.True(_workspace.IsEnabled(CommandIds.Undo)); Assert.True(_workspace.IsEnabled(CommandIds.Redo));
            Do(Undo); Assert.AreEqual(1, _backend.HistoryCalls);
            // The document moved: history stays blocked until the scene of the new revision arrives.
            Assert.False(_workspace.IsEnabled(CommandIds.Undo)); Assert.False(_workspace.IsEnabled(CommandIds.Redo));
            Assert.False(Enabled(Undo)); Assert.False(string.IsNullOrEmpty(Act(Undo).DisabledReason));
        }

        // M5-05: distinct outcomes
        [Test] public void FlatPatternOutcomesAreDistinct()
        {
            Assert.True(_workspace.IsEnabled(CommandIds.FlatPatternCreate));
            _backend.FlatExists = true; Start(_backend);
            Assert.False(_workspace.IsEnabled(CommandIds.FlatPatternCreate)); StringAssert.Contains("presente", _workspace.StatusSummary());
            Assert.AreEqual(FlatPatternCreation.AlreadyExists, _workspace.Mode.CheckFlatPattern(out var exists)); StringAssert.Contains("esiste già", exists);
            StringAssert.Contains("esiste già", Act(FlatCreate).DisabledReason);
            _backend.FlatExists = false; _backend.Bodies = 2; Start(_backend);
            Assert.False(_workspace.IsEnabled(CommandIds.FlatPatternCreate));
            Assert.AreEqual(FlatPatternCreation.MultiBody, _workspace.Mode.CheckFlatPattern(out var multi)); StringAssert.Contains("un solo body", multi);
            StringAssert.Contains("un solo body", Act(FlatCreate).DisabledReason);
            Assert.AreEqual(0, _backend.Previews);
        }

        // M5-05 / M5-06
        [Test] public void CreateFlatPatternThenShowBesideFoldedPartAndDetachIsViewOnly()
        {
            Assert.True(_workspace.Invoke(CommandIds.FlatPatternCreate));
            Assert.AreEqual("create_flat_pattern", (string)_backend.LastOperations[0]["command"]);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Do(CommitIds.Apply);
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
            Assert.True(Enabled(FlatDetach)); Assert.False(Enabled(FlatAttach));
            Do(FlatDetach); Assert.True(Enabled(FlatAttach)); Assert.False(Enabled(FlatDetach));
            Assert.True(_workspace.FlatPattern.MoveLocal(0.25, 0.1, 0));
            Assert.That(root.localPosition.x, Is.EqualTo(before.x + 0.25f).Within(1e-5)); Assert.That(root.localPosition.y, Is.EqualTo(before.y + 0.1f).Within(1e-5));
            Assert.AreEqual(previews, _backend.Previews); Assert.AreEqual(commits, _backend.Commits, "Moving the view never touches CAD.");
            Assert.False(_workspace.FlatPattern.IsCadSelectable);
            Do(FlatAttach); Assert.AreEqual(0, _workspace.FlatPattern.OffsetX);
            Do(FlatHide); Assert.AreEqual(FlatPatternState.Hidden, _workspace.FlatPattern.State);
            Assert.True(Enabled(FlatShow)); Assert.False(Enabled(FlatHide));
        }

        [Test] public void FlatPatternFailureShowsItsReasonOnTheHudAndNoEstimate()
        {
            _backend.FlatExists = true; _backend.FlatFailure = new FlatPatternException(FlatPatternFailure.TooLarge, "Lo sviluppo supera il budget geometrico del visore.");
            Start(_backend); _hud.Clear(); Do(FlatShow);
            Assert.AreEqual(FlatPatternState.Unavailable, _workspace.FlatPattern.State);
            StringAssert.Contains("budget geometrico", AllHud());
            Assert.Null(_view.transform.Find("Flat pattern (view only)"));
        }

        // ---------------------------------------------------------------- M6 phase 3 part B: XrInput (synthetic) drives the workspace

        private XrInput _xr;
        private XrInputFrame _frame;
        private float _clock;
        private readonly List<HapticPulse> _haptics = new List<HapticPulse>();
        private Action<HapticPulse, bool> _previousSink;

        /// <summary>Synthetic input: no OVRInput is read; the test feeds frames to the same XrInput the app uses.</summary>
        private void UseInput()
        {
            _xr = Child("XrInput").AddComponent<XrInput>(); _xr.Source = new SyntheticInputSource();
            Assert.True(_xr.Synthetic, "the runner log must call this input synthetic");
            _workspace.Attach(null, null, null, _xr);
            _frame = new XrInputFrame { PenTracked = true, PaletteTracked = true };
            Poll();
        }

        private void Poll() => _xr.Poll(_frame, _clock += 0.016f);
        private void PressTrigger() { _frame.PenTrigger = true; Poll(); }
        private void ReleaseTrigger() { _frame.PenTrigger = false; Poll(); }
        private void Frame() => typeof(LamieraWorkspace).GetMethod("Update", Flags).Invoke(_workspace, null);
        private bool Capturing => Manipulator.Dragging;
        private Vector3 AxisWorld() => _view.transform.TransformDirection(CadCoordinates.ToLocal(Manipulator.Axis)).normalized;

        private void AimAtKnob()
        {
            var axis = AxisWorld();
            var side = Vector3.Cross(axis, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(axis, Vector3.right).normalized;
            _ray.Origin.SetPositionAndRotation(Manipulator.KnobWorldPosition + side * 0.15f, Quaternion.LookRotation(-side));
        }

        private void AimAwayFromKnob() => _ray.Origin.SetPositionAndRotation(new Vector3(5, 5, 5), Quaternion.LookRotation(Vector3.up));

        /// <summary>Moves the controller by a physical distance along the flange axis: the same CAD millimetres at any view scale.</summary>
        private void MoveHandAlongAxis(double cadMm) => _ray.Origin.position += AxisWorld() * (float)(cadMm * 0.001 * _view.transform.lossyScale.x);

        [TestCase(1f)][TestCase(0.25f)]
        public void TriggerHeldOnTheKnobDragsOnlyTheDraftByTheSameMillimetresAtAnyScale(float scale)
        {
            UseInput(); ArmFlangeWithEdge();
            _view.transform.localScale = Vector3.one * scale;
            AimAtKnob(); PressTrigger();
            Assert.True(Capturing, "Trigger held on the knob captures it");
            MoveHandAlongAxis(10); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(20).Within(0.05));
            Assert.AreEqual(0, _backend.Previews, "Dragging alone never computes a preview.");
            Assert.AreEqual(0, _backend.Commits); Assert.False(Enabled(CommitIds.Apply), "only the draft changed");
            ReleaseTrigger();
            Assert.False(Capturing);
            Assert.AreEqual(1, _backend.Previews, "the release asks for exactly one preview"); Assert.AreEqual(0, _backend.Commits);
            Assert.That((double)_backend.LastOperations[0]["arguments"]["height_mm"], Is.EqualTo(20).Within(0.05));
            Assert.True(PreviewView.IsShowing);
        }

        [Test] public void OnlyAPressThatStartsOnTheKnobCaptures()
        {
            UseInput(); ArmFlangeWithEdge();
            AimAwayFromKnob(); PressTrigger();
            Assert.False(Capturing, "Trigger away from the knob does not capture");
            AimAtKnob(); Frame();
            Assert.False(Capturing, "moving onto the knob with the Trigger already held does not capture either");
            Assert.AreEqual(10, _workspace.Flange.HeightMm);
            ReleaseTrigger();
            Assert.AreEqual(0, _backend.Previews, "a release that captured nothing previews nothing");
            AimAtKnob(); PressTrigger(); Assert.True(Capturing);
        }

        [Test] public void GripNeverCapturesTheKnobAndAloneItOnlyMovesTheView()
        {
            UseInput(); ArmFlangeWithEdge();
            int version = _workspace.Flange.Version;
            AimAtKnob();
            _frame.PenGrip = true; Poll(); PressTrigger();
            Assert.False(Capturing, "Grip + Trigger no longer drags the knob");
            MoveHandAlongAxis(10); Frame();
            Assert.AreEqual(10, _workspace.Flange.HeightMm); Assert.AreEqual(version, _workspace.Flange.Version);
            ReleaseTrigger(); _frame.PenGrip = false; Poll();
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void PlainGripNeverChangesTheFlangeDraft()
        {
            UseInput(); ArmFlangeWithEdge();
            int version = _workspace.Flange.Version;
            AimAtKnob();
            _frame.PenGrip = true; Poll();
            MoveHandAlongAxis(10); Frame();
            Assert.False(Capturing);
            Assert.AreEqual(10, _workspace.Flange.HeightMm); Assert.AreEqual(version, _workspace.Flange.Version);
            _frame.PenGrip = false; Poll();
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ReleasingTheTriggerEndsTheCaptureAndFurtherMotionIsIgnored()
        {
            UseInput(); ArmFlangeWithEdge(); AimAtKnob(); PressTrigger();
            MoveHandAlongAxis(5); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(15).Within(0.05));
            ReleaseTrigger();
            Assert.False(Capturing); Assert.AreEqual(1, _backend.Previews);
            MoveHandAlongAxis(20); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(15).Within(0.05));
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void TrackingLossKeepsTheLastValidHeightWithAnErrorPulseAndNoPreview()
        {
            UseInput(); ArmFlangeWithEdge(); AimAtKnob(); PressTrigger();
            MoveHandAlongAxis(8); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(18).Within(0.05));
            _haptics.Clear();
            MoveHandAlongAxis(500);                       // garbage pose reported while tracking is lost
            _frame.PenTracked = false; Poll();            // Trigger still down: tracking loss first, then the release
            Assert.False(Capturing);
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(18).Within(0.05));
            Assert.AreEqual(0, _backend.Previews, "a loss is not a release: no preview"); Assert.AreEqual(0, _backend.Commits);
            CollectionAssert.Contains(_haptics, HapticPulse.Error);
            Frame(); Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(18).Within(0.05));
            _frame.PenTracked = true; Poll();             // the Trigger is still held: a new press far from the knob
            Assert.False(Capturing, "no capture can start from a pose that is not on the knob");
            _frame.PenTrigger = false; Poll();
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(18).Within(0.05));
        }

        [Test] public void ClosingOrHidingTheWorkspaceEndsTheCapture()
        {
            UseInput(); ArmFlangeWithEdge(); AimAtKnob(); PressTrigger(); Assert.True(Capturing);
            _workspace.Close();
            Assert.False(Capturing); Assert.AreEqual(0, _backend.Commits);
            int previews = _backend.Previews;
            ReleaseTrigger(); Assert.AreEqual(previews, _backend.Previews, "a release after Close does not preview");

            _workspace.Open(); ArmFlangeWithEdge(); AimAtKnob(); PressTrigger(); Assert.True(Capturing);
            _workspace.SetVisible(false);
            Assert.False(Capturing); Assert.False(_workspace.Active); Assert.AreEqual(0, _backend.Commits);
            ReleaseTrigger(); Assert.AreEqual(previews, _backend.Previews);
        }

        [Test] public void KnobCaptureNeedsTheFlangeCommandArmedWithAnEdge()
        {
            UseInput();
            PressTrigger(); Assert.False(Manipulator.Visible); Assert.False(Capturing); ReleaseTrigger();
            Do(FlangeAction);   // armed, still no edge
            PressTrigger(); Assert.False(Capturing); ReleaseTrigger();
        }

        [Test] public void PrecisionHeldMakesTheDragTenTimesSlowerAndReAnchorsWithoutJumping()
        {
            UseInput(); ArmFlangeWithEdge();
            AimAtKnob(); PressTrigger();
            MoveHandAlongAxis(10); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(20).Within(0.05));
            ReleaseTrigger();

            _frame.PaletteTrigger = true; Poll(); Assert.True(_xr.Precision);
            AimAtKnob(); PressTrigger();
            MoveHandAlongAxis(10); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(21).Within(0.05), "10 mm of hand = 1 mm of height with the left trigger held");
            ReleaseTrigger();

            // switching precision on in the middle of a drag re-anchors instead of jumping
            _frame.PaletteTrigger = false; Poll();
            AimAtKnob(); PressTrigger();
            MoveHandAlongAxis(10); Frame(); double fast = _workspace.Flange.HeightMm;
            Assert.That(fast, Is.EqualTo(31).Within(0.05));
            _frame.PaletteTrigger = true; Poll(); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(fast).Within(1e-9), "no jump when precision toggles");
            MoveHandAlongAxis(10); Frame();
            Assert.That(_workspace.Flange.HeightMm, Is.EqualTo(fast + 1).Within(0.05));
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void WorkspaceWithoutAnInputNeverThrowsAndKeepsNoControllerState()
        {
            ArmFlangeWithEdge();
            Assert.DoesNotThrow(() => { Frame(); _workspace.Back(); _workspace.FitView(); _workspace.RecenterView(); _workspace.ZoomView(1, 0.1f); });
            Assert.False(Capturing);
        }

        private bool PickerTabs() => _workspace.Tabs.Any(t => t.Id.StartsWith(LamieraWorkspace.PickTabPrefix));
        private void PressX() { _frame.X = true; Poll(); _frame.X = false; Poll(); }

        [Test] public void BackClosesKeypadThenRingThenPickerThenTheArmedFieldThenTheLastDraftStep()
        {
            UseInput(); var shell = AttachShell(out _);
            var ring = Field<RingView>("_ring");
            Do(Rule); Assert.True(PickerTabs(), "the rule list is open");
            typeof(LamieraWorkspace).GetMethod("AskNumber", Flags).Invoke(_workspace, new object[] { LamieraWorkspace.FieldThickness });
            Assert.True(shell.Palette.KeypadVisible); Assert.NotNull(_workspace.ActiveEntry); Assert.NotNull(_workspace.ArmedField);
            typeof(LamieraWorkspace).GetMethod("ShowRing", Flags).Invoke(_workspace, new object[] { UiSelectionKind.PlanarFace, Vector3.zero });
            Assert.True(ring.Visible);
            int previews = _backend.Previews;

            PressX(); Assert.False(shell.Palette.KeypadVisible, "1: keypad"); Assert.True(ring.Visible); Assert.True(PickerTabs()); Assert.NotNull(_workspace.ArmedField);
            PressX(); Assert.False(ring.Visible, "2: ring"); Assert.True(PickerTabs()); Assert.NotNull(_workspace.ArmedField);
            PressX(); Assert.False(PickerTabs(), "3: picker"); Assert.NotNull(_workspace.ArmedField);
            PressX(); Assert.IsNull(_workspace.ArmedField, "4: pending pick (the armed chip)"); Assert.AreEqual(SheetMetalCommand.Rule, _workspace.Mode.Armed);
            PressX(); Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed, "5: last draft step (the command)");
            PressX(); Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed, "nothing left to discard");
            Assert.AreEqual(previews, _backend.Previews, "X never previews"); Assert.AreEqual(0, _backend.Commits, "X never touches the CAD");
        }

        [Test] public void BackDropsTheLastFlangeEdgeBeforeTheCommand()
        {
            UseInput(); ArmFlangeWithEdge();
            _workspace.Flange.ToggleEdge("ent_edge2");
            PressX(); CollectionAssert.AreEqual(new[] { "ent_edge" }, _workspace.Flange.EdgeIds.ToArray());
            PressX(); Assert.AreEqual(0, _workspace.Flange.EdgeIds.Count); Assert.AreEqual(SheetMetalCommand.Flange, _workspace.Mode.Armed);
            PressX(); Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ZoomScalesTheModelAndClampsToTheLayoutLimits()
        {
            UseInput(); ArmFlangeWithEdge();
            var root = _view.transform; var status = _workspace.Session.Status;
            var up = new XrInputFrame { PenTracked = true, PaletteTracked = true, PaletteStick = new Vector2(0, 1) };
            float before = root.localScale.x;
            _xr.Poll(up, _clock += 0.016f);
            Assert.Greater(root.localScale.x, before, "stick up zooms in");
            for (int i = 0; i < 3000; i++) _xr.Poll(up, _clock += 0.016f);
            Assert.AreEqual((float)WorkbenchLayout.MaxScale, root.localScale.x, 1e-6f);
            var down = up; down.PaletteStick = new Vector2(0, -1);
            for (int i = 0; i < 6000; i++) _xr.Poll(down, _clock += 0.016f);
            Assert.AreEqual((float)WorkbenchLayout.MinScale, root.localScale.x, 1e-9f);
            Assert.AreEqual(status, _workspace.Session.Status, "zoom is view only"); Assert.AreEqual(0, _backend.Commits);
        }

        private void Flick(float x, float y)
        {
            _xr.Poll(new XrInputFrame { PenTracked = true, PaletteTracked = true, PenStick = new Vector2(x, y) }, _clock += 0.016f);
            _xr.Poll(new XrInputFrame { PenTracked = true, PaletteTracked = true }, _clock += 0.016f);
        }

        [Test] public void RightStickNudgesTheArmedChipAndCyclesTheStepWithTicks()
        {
            UseInput(); ArmFlangeWithEdge();
            Flick(0.9f, 0); Assert.AreEqual(10, _workspace.Flange.HeightMm, "no chip armed: the stick does nothing"); Assert.IsEmpty(_haptics);
            Assert.True(_workspace.TryArmField(LamieraWorkspace.FieldFlangeHeight)); _haptics.Clear();
            Flick(0.9f, 0); Assert.AreEqual(11, _workspace.Flange.HeightMm, 1e-9, "right = +1 step"); CollectionAssert.AreEqual(new[] { HapticPulse.Tick }, _haptics);
            Flick(0, 0.9f); Assert.AreEqual(11, _workspace.Flange.HeightMm, "cycling the step changes no value");
            Flick(0.9f, 0); Assert.AreEqual(21, _workspace.Flange.HeightMm, 1e-9, "step is now 10");
            Flick(0, -0.9f); Flick(0, -0.9f);
            Flick(-0.9f, 0); Assert.AreEqual(20.9, _workspace.Flange.HeightMm, 1e-9, "step is now 0,1; left = -1 step");
            Assert.AreEqual(6, _haptics.Count(p => p == HapticPulse.Tick), "every step and every step change ticks");
            Assert.AreEqual(0, _backend.Commits);
            _workspace.DisarmField();
        }

        [Test] public void HapticsFollowTheProfilesForApplySuccessAndError()
        {
            UseInput(); ArmFlangeWithEdge(); Do(CommitIds.Preview); _haptics.Clear();
            Do(CommitIds.Apply);
            CollectionAssert.Contains(_haptics, HapticPulse.Success);
            Assert.AreEqual(1, _haptics.Count(p => p == HapticPulse.Success), "one success pulse");
            _haptics.Clear();
            Start(_backend); ArmFlangeWithEdge();
            _backend.PreviewError = new InvalidOperationException("boom"); Do(CommitIds.Preview);
            CollectionAssert.Contains(_haptics, HapticPulse.Error);
        }

        [Test] public void HoveringTheKnobGivesAShortPulseOnEntryOnly()
        {
            UseInput(); ArmFlangeWithEdge(); _haptics.Clear();
            AimAwayFromKnob(); Frame(); Assert.IsEmpty(_haptics);
            AimAtKnob(); Frame(); Frame();
            CollectionAssert.AreEqual(new[] { HapticPulse.Hover }, _haptics);
        }

        [Test] public void LamieraSourcesNeverReadOvrInput()
        {
            foreach (var path in new[] { "XrSo/Xr/LamieraWorkspace.cs", "XrSo/Xr/LamieraActions.cs", "XrSo/Runtime/Scene/FlangeManipulator.cs", "XrSo/Runtime/Scene/FlatPatternDisplay.cs" })
                StringAssert.DoesNotContain("OVRInput", File.ReadAllText(Path.Combine(Application.dataPath, path)), path);
        }

        private void AimAtEdge(string edgeId)
        {
            var context = Field<DesignContext>("_designContext");
            var target = context.Edges.Single(e => e.Id == edgeId);
            Assert.True(FlangeManipulator.TryFrame(target, context.Faces, 2.0, out var origin, out var normal, out _));
            var point = _view.transform.TransformPoint(CadCoordinates.ToLocal(origin));
            var away = _view.transform.TransformDirection(CadCoordinates.ToLocal(normal)).normalized;
            _ray.Origin.SetPositionAndRotation(point + away * 0.3f, Quaternion.LookRotation(-away));
        }

        [Test] public void TriggerRayOnAnUnobstructedEdgeTogglesItInTheFlangeDraft()
        {
            UseInput(); Do(FlangeAction);
            var draft = _workspace.Flange;
            AimAtEdge("ent_edge");
            PressTrigger(); ReleaseTrigger();
            CollectionAssert.AreEqual(new[] { "ent_edge" }, draft.EdgeIds.ToArray());
            // the knob of the first edge stands on that edge's normal: a press there takes the knob, not the edge
            AimAtEdge("ent_edge2");
            PressTrigger(); ReleaseTrigger();
            CollectionAssert.AreEqual(new[] { "ent_edge", "ent_edge2" }, draft.EdgeIds.ToArray());
            PressTrigger(); ReleaseTrigger();
            CollectionAssert.AreEqual(new[] { "ent_edge" }, draft.EdgeIds.ToArray());
            Assert.AreEqual(0, _backend.Commits);
        }

        // M5-06: Grip moves only the detached flat pattern view
        private FlatPatternDisplay ShowFlatAndAimAt()
        {
            _backend.FlatExists = true; Start(_backend); Do(FlatShow);
            var display = _workspace.GetComponentInChildren<FlatPatternDisplay>(true);
            Assert.True(display.IsShowing);
            var center = _view.transform.TransformPoint(display.LocalBounds.center);
            _ray.Origin.SetPositionAndRotation(center + Vector3.back * 0.5f, Quaternion.identity);
            return display;
        }

        [Test] public void GripOnTheDetachedFlatPatternMovesOnlyTheFlatMeshRoot()
        {
            UseInput();
            var display = ShowFlatAndAimAt();
            Do(FlatDetach);
            var model = _view.transform;
            Vector3 rootBefore = display.MeshRoot.localPosition, modelPosition = model.position, modelScale = model.localScale;
            var modelRotation = model.rotation;
            int previews = _backend.Previews, commits = _backend.Commits;
            var delta = new Vector3(0.05f, 0.02f, 0f);
            _frame.PenGrip = true; Poll();
            _ray.Origin.position += delta; Frame();
            var expected = rootBefore + model.InverseTransformVector(delta);
            Assert.That(Vector3.Distance(display.MeshRoot.localPosition, expected), Is.LessThan(1e-4f));
            Assert.AreEqual(modelPosition, model.position); Assert.AreEqual(modelScale, model.localScale); Assert.AreEqual(modelRotation, model.rotation);
            _frame.PenGrip = false; Poll();
            _ray.Origin.position += delta; Frame();
            Assert.That(Vector3.Distance(display.MeshRoot.localPosition, expected), Is.LessThan(1e-4f), "Released grip stops the movement.");
            Assert.AreEqual(previews, _backend.Previews); Assert.AreEqual(commits, _backend.Commits, "Moving the view never touches CAD.");
        }

        [Test] public void GripDoesNotGrabTheFlatPatternWhenItIsNotDetached()
        {
            UseInput();
            var display = ShowFlatAndAimAt();
            Assert.False(_workspace.FlatPattern.Detached);
            var rootBefore = display.MeshRoot.localPosition;
            _frame.PenGrip = true; Poll();
            _ray.Origin.position += new Vector3(0.05f, 0.02f, 0f); Frame();
            Assert.AreEqual(rootBefore, display.MeshRoot.localPosition);
            Assert.AreEqual(0, _workspace.FlatPattern.OffsetX);
            Assert.AreEqual(0, _backend.Commits);
        }

        // ---------------------------------------------------------------- the flat pattern on the work plane

        private static Bounds WorldBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        private FlatPatternDisplay ShowFlatOnThePlane(out Workbench bench)
        {
            UseInput(); AttachShell(out bench);
            _backend.FlatExists = true; Start(_backend); Do(FlatShow);
            bench.Snap();
            var display = _workspace.GetComponentInChildren<FlatPatternDisplay>(true);
            display.Snap();
            return display;
        }

        [Test] public void FlatPatternLiesOnTheWorkPlaneInFrontOfTheUserFittedToTheSheetArea()
        {
            _backend.FlatExists = true;
            UseInput(); AttachShell(out var bench); Start(_backend); Do(FlatShow);
            var display = _workspace.GetComponentInChildren<FlatPatternDisplay>(true);
            Assert.True(display.HasWorkPlane); Assert.True(display.IsShowing);
            Assert.True(display.Tweening, "the pattern eases onto the plane (PoseTween), no jump");
            var model = ScenePlacement.LocalBounds(_view.transform).center;
            Assert.Less(Vector3.Distance(display.MeshRoot.position, _view.transform.TransformPoint(model)), 0.05f, "it starts out of the folded part");
            bench.Snap(); display.Snap();
            Assert.False(display.Tweening);
            var bounds = WorldBounds(display.MeshRoot);
            var floor = WorkbenchLayout.Part(bench.Frame, 0.4).Position;
            Assert.AreEqual((float)floor.X, bounds.center.x, 2e-3f); Assert.AreEqual((float)floor.Z, bounds.center.z, 2e-3f);
            Assert.AreEqual((float)floor.Y + 0.002f + bounds.extents.y, bounds.center.y, 2e-3f, "it rests on the plane");
            Assert.LessOrEqual(bounds.size.x, (float)WorkbenchLayout.SheetWidth + 2e-3f); Assert.LessOrEqual(bounds.size.z, (float)WorkbenchLayout.SheetDepth + 2e-3f);
            Assert.LessOrEqual(bounds.size.y, Mathf.Min(bounds.size.x, bounds.size.z) + 1e-4f, "the sheet normal is vertical");
            Assert.True(display.FoldedHidden, "the pattern replaces the folded part on the plane");
            Assert.True(_view.GetComponentsInChildren<CadBody>(true).All(b => !b.GetComponent<MeshRenderer>().enabled));
            // Recentre: the pattern follows the new frame
            _eye.transform.position = new Vector3(0, 1.6f, 0); _eye.transform.rotation = Quaternion.Euler(0, 90, 0);
            Do(LamieraWorkspace.IdRecenter); bench.Snap(); display.Snap();
            var moved = WorldBounds(display.MeshRoot); var next = WorkbenchLayout.Part(bench.Frame, 0.4).Position;
            Assert.AreEqual((float)next.X, moved.center.x, 2e-3f); Assert.AreEqual((float)next.Z, moved.center.z, 2e-3f);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void DetachMovesOnlyThePatternAndAttachReturnsItToThePlacedPose()
        {
            var display = ShowFlatOnThePlane(out _);
            var model = _view.transform;
            Vector3 placed = display.MeshRoot.position, modelPosition = model.position, modelScale = model.localScale; var modelRotation = model.rotation;
            int previews = _backend.Previews;
            Do(FlatDetach); display.Snap();
            Assert.That(Vector3.Distance(display.MeshRoot.position, placed), Is.LessThan(1e-4f), "detaching alone moves nothing");
            Assert.True(_workspace.FlatPattern.MoveLocal(0.10, 0, 0.05));
            Assert.That(Vector3.Distance(display.MeshRoot.position, placed + new Vector3(0.10f, 0, 0.05f)), Is.LessThan(1e-4f), "offset in plane metres");
            Assert.AreEqual(modelPosition, model.position); Assert.AreEqual(modelScale, model.localScale); Assert.AreEqual(modelRotation, model.rotation);
            Do(FlatAttach); display.Snap();
            Assert.That(Vector3.Distance(display.MeshRoot.position, placed), Is.LessThan(1e-4f), "attach returns to the placed pose");
            Assert.AreEqual(0, _workspace.FlatPattern.OffsetX);
            Assert.AreEqual(previews, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void GripMovesTheDetachedPatternOnThePlaneAndNeverTheModel()
        {
            var display = ShowFlatOnThePlane(out _);
            Do(FlatDetach);
            var model = _view.transform;
            Vector3 before = display.MeshRoot.position, modelPosition = model.position, modelScale = model.localScale; var modelRotation = model.rotation;
            var centre = _view.transform.TransformPoint(display.LocalBounds.center);
            _ray.Origin.SetPositionAndRotation(centre + Vector3.back * 0.5f, Quaternion.identity);
            var delta = new Vector3(0.05f, 0.0f, 0.02f);
            _frame.PenGrip = true; Poll();
            _ray.Origin.position += delta; Frame();
            Assert.That(Vector3.Distance(display.MeshRoot.position, before + delta), Is.LessThan(1e-4f));
            Assert.AreEqual(modelPosition, model.position); Assert.AreEqual(modelScale, model.localScale); Assert.AreEqual(modelRotation, model.rotation);
            _frame.PenGrip = false; Poll();
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ZoomOnThePlaneScalesThePatternWithinTheLayoutLimits()
        {
            var display = ShowFlatOnThePlane(out _);
            float modelScale = _view.transform.localScale.x;
            var up = new XrInputFrame { PenTracked = true, PaletteTracked = true, PaletteStick = new Vector2(0, 1) };
            float before = WorldBounds(display.MeshRoot).size.x;   // the small test mesh is already fitted at the maximum scale
            var down = up; down.PaletteStick = new Vector2(0, -1);
            _xr.Poll(down, _clock += 0.016f); display.Snap();
            Assert.Less(WorldBounds(display.MeshRoot).size.x, before, "stick down zooms the pattern out");
            for (int i = 0; i < 4000; i++) _xr.Poll(up, _clock += 0.016f);
            display.Snap();
            Assert.LessOrEqual(display.MeshRoot.lossyScale.x, (float)WorkbenchLayout.MaxScale + 1e-4f);
            for (int i = 0; i < 9000; i++) _xr.Poll(down, _clock += 0.016f);
            display.Snap();
            Assert.GreaterOrEqual(display.MeshRoot.lossyScale.x, (float)WorkbenchLayout.MinScale - 1e-6f);
            Assert.AreEqual(modelScale, _view.transform.localScale.x, "the folded part is not zoomed");
            Do(LamieraWorkspace.IdFit); display.Snap();
            Assert.That(WorldBounds(display.MeshRoot).size.x, Is.EqualTo(before).Within(2e-3f), "Adatta returns to the fitted size");
        }

        [Test] public void ViewActionsToggleBetweenFoldedAndFlatOnlyWhenAFlatPatternExists()
        {
            Assert.False(Enabled(LamieraWorkspace.IdViewFlat)); Assert.False(Enabled(LamieraWorkspace.IdViewFolded));
            StringAssert.Contains("non esiste", Act(LamieraWorkspace.IdViewFlat).DisabledReason);
            Assert.False(string.IsNullOrEmpty(Act(LamieraWorkspace.IdViewFolded).DisabledReason));
            Assert.AreEqual(LamieraWorkspace.TabView, Act(LamieraWorkspace.IdViewFlat).Tab); Assert.AreEqual(LamieraWorkspace.TabView, Act(LamieraWorkspace.IdViewFolded).Tab);
            _backend.FlatExists = true; Start(_backend);
            Assert.True(Enabled(LamieraWorkspace.IdViewFlat)); Assert.False(Enabled(LamieraWorkspace.IdViewFolded), "already folded");
            Do(LamieraWorkspace.IdViewFlat);
            Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State);
            var display = _workspace.GetComponentInChildren<FlatPatternDisplay>(true);
            Assert.True(display.IsShowing); Assert.True(_workspace.FlatViewOn);
            Assert.True(Enabled(LamieraWorkspace.IdViewFolded)); Assert.False(Enabled(LamieraWorkspace.IdViewFlat));
            int meshes = _backend.FlatMeshes;
            Do(LamieraWorkspace.IdViewFolded);
            Assert.False(display.IsShowing, "Piegato: the pattern is hidden"); Assert.False(_workspace.FlatViewOn);
            Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State, "only the view changed, the verified asset is kept");
            Assert.True(Enabled(LamieraWorkspace.IdViewFlat)); Assert.False(Enabled(LamieraWorkspace.IdViewFolded));
            Do(LamieraWorkspace.IdViewFlat); Assert.True(display.IsShowing);
            Assert.AreEqual(meshes, _backend.FlatMeshes, "toggling never refetches the pattern");
            Do(FlangeAction);
            Assert.False(display.IsShowing, "editing the part brings the folded view back"); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void FoldedViewBringsTheFoldedPartBackOnThePlane()
        {
            var display = ShowFlatOnThePlane(out _);
            Assert.True(_view.GetComponentsInChildren<CadBody>(true).All(b => !b.GetComponent<MeshRenderer>().enabled));
            Do(LamieraWorkspace.IdViewFolded);
            Assert.False(display.FoldedHidden);
            Assert.True(_view.GetComponentsInChildren<CadBody>(true).All(b => b.GetComponent<MeshRenderer>().enabled));
            Do(LamieraWorkspace.IdViewFlat); Do(FlatHide);
            Assert.True(_view.GetComponentsInChildren<CadBody>(true).All(b => b.GetComponent<MeshRenderer>().enabled), "hiding the pattern restores the part");
            _workspace.Close();
            Assert.True(_view.GetComponentsInChildren<CadBody>(true).All(b => b.GetComponent<MeshRenderer>().enabled));
        }

        // ---------------------------------------------------------------- the idle ring

        private bool InvokeSelectPlanarFace(CadBody body, int triangle)
            => (bool)typeof(LamieraWorkspace).GetMethod("SelectPlanarFace", Flags).Invoke(_workspace, new object[] { body, triangle, Vector3.zero });

        [Test] public void RingOpensOnAnEdgeAndOnlyOnARealPlanarFace()
        {
            UseInput(); var shell = AttachShell(out _);
            var ring = Field<RingView>("_ring");
            // an edge
            AimAtEdge("ent_edge"); PressTrigger(); ReleaseTrigger();
            Assert.True(ring.Visible); Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed);
            // empty space closes it
            AimAwayFromKnob(); PressTrigger(); ReleaseTrigger();
            Assert.False(ring.Visible, "a press on nothing closes the ring");
            // a body hit that the CAD context does not list as a planar face never opens it
            var body = _view.Instances[0].Bodies[0]; var range = body.Primitive.FaceMap.FaceAtTriangle(0);
            Assert.False(InvokeSelectPlanarFace(body, 0), "the default context has no face for this mesh face");
            StringAssert.Contains("non piana", _workspace.Notice);
            // the same hit with a matching planar face (same revision, same body and face ordinal)
            var state = Field<DocumentState>("_state");
            var context = DesignContext.Parse(new JObject
            {
                ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "part",
                ["faces"] = new JArray(new JObject { ["id"] = "ent_planar", ["body_index"] = body.Primitive.BodyIndex, ["face_ordinal"] = range.Ordinal,
                    ["point_mm"] = new JArray(0, 0, 10), ["normal"] = new JArray(0, 0, 1) }),
            }, state);
            typeof(LamieraWorkspace).GetField("_designContext", Flags).SetValue(_workspace, context);
            Assert.True(InvokeSelectPlanarFace(body, 0));
            // a context of another revision is not trusted
            var stale = DesignContext.Parse(new JObject { ["document_id"] = state.DocumentId, ["revision"] = "r_old", ["kind"] = "part",
                ["faces"] = new JArray(new JObject { ["id"] = "ent_planar", ["body_index"] = body.Primitive.BodyIndex, ["face_ordinal"] = range.Ordinal,
                    ["point_mm"] = new JArray(0, 0, 10), ["normal"] = new JArray(0, 0, 1) }) }, new DocumentState(state.DocumentId, "r_old", "v"));
            typeof(LamieraWorkspace).GetField("_designContext", Flags).SetValue(_workspace, stale);
            Assert.False(InvokeSelectPlanarFace(body, 0));
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void NonVisualRevisionBumpKeepsTheSceneCurrentButAVisualOneDoesNot()
        {
            var body = _view.Instances[0].Bodies[0]; var range = body.Primitive.FaceMap.FaceAtTriangle(0);
            bool PickAt(DocumentState state)
            {
                _workspace.SetDocumentState(state);
                typeof(LamieraWorkspace).GetField("_designContext", Flags).SetValue(_workspace, DesignContext.Parse(new JObject
                {
                    ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "part",
                    ["faces"] = new JArray(new JObject { ["id"] = "ent_planar", ["body_index"] = body.Primitive.BodyIndex, ["face_ordinal"] = range.Ordinal,
                        ["point_mm"] = new JArray(0, 0, 10), ["normal"] = new JArray(0, 0, 1) }),
                }, state));
                return InvokeSelectPlanarFace(body, 0);
            }
            // The scene reloads only on a visual revision change: the revision alone moves on.
            Assert.True(PickAt(new DocumentState("doc_bolt", "r_non_visual", "v")), "a non-visual bump keeps the scene current");
            Assert.True(typeof(LamieraWorkspace).GetProperty("SceneCurrent", Flags).GetValue(_workspace) as bool?);
            // A visual change without a scene reload is stale.
            Assert.False(PickAt(new DocumentState("doc_bolt", "r_visual", "v_new")));
            Assert.False(typeof(LamieraWorkspace).GetProperty("SceneCurrent", Flags).GetValue(_workspace) as bool?);
        }

        [Test] public void IdleTriggerRayOnAnEdgeOpensTheRing()
        {
            UseInput(); AttachShell(out _);
            var ring = Field<RingView>("_ring");
            AimAtEdge("ent_edge");
            PressTrigger();
            Assert.True(ring.Visible); Assert.AreEqual(SheetMetalCommand.None, _workspace.Mode.Armed);
            Assert.AreEqual(0, _backend.Commits);
        }

        // M5-07
        [Test] public void OfflineKeepsPatternReadOnlyBlocksWritesAndReverifiesOnReconnect()
        {
            _backend.FlatExists = true; Start(_backend); Do(FlatShow);
            Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State);
            _hud.Clear();
            _workspace.SetOnline(false);
            Assert.AreEqual(FlatPatternState.StaleReadOnly, _workspace.FlatPattern.State);
            Assert.AreEqual(SheetMetalAvailability.Offline, _workspace.Mode.Availability);
            Assert.False(_workspace.IsEnabled(CommandIds.Flange)); StringAssert.Contains("Offline", AllHud());
            Assert.AreEqual(CommitBarPhase.Offline, _workspace.CommitBar.Phase);
            StringAssert.Contains("Offline", Act(FlangeAction).DisabledReason);
            StringAssert.Contains("sola ispezione", _workspace.GetComponentInChildren<FlatPatternDisplay>(true).LabelText);
            int meshes = _backend.FlatMeshes;
            _workspace.SetOnline(true);
            Assert.AreEqual(meshes + 1, _backend.FlatMeshes); Assert.AreEqual(FlatPatternState.Ready, _workspace.FlatPattern.State);
        }

        [Test] public void RevisionChangeDropsDraftPreviewSelectionAndHidesThePattern()
        {
            _backend.FlatExists = true; Start(_backend); Do(FlatShow);
            ArmFlangeWithEdge(); Do(CommitIds.Preview);
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
            ArmFlangeWithEdge(); Do(CommitIds.Preview); _workspace.Close();
            _backend.PendingPreview.SetResult(new DesignPreview("late", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model));
            yield return null; yield return null;
            _workspace.Open();
            Assert.False(PreviewView.IsShowing); Assert.False(_workspace.Session.CanApply); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void SceneRebuildInvalidatesTheRenderedPreview()
        {
            ArmFlangeWithEdge(); Do(CommitIds.Preview);
            Assert.True(_workspace.Session.CanApply);
            _view.Show(_scene); _workspace.SetScene(_scene);
            Assert.False(_workspace.Session.CanApply); Assert.False(PreviewView.IsShowing);
            Do(CommitIds.Preview); Assert.True(_workspace.Session.CanApply);
        }

        [Test] public void ClosingReleasesPickingAndHidesTheManipulator()
        {
            ArmFlangeWithEdge(); _workspace.Close();
            Assert.False(_workspace.Active); Assert.False(Manipulator.Visible); Assert.True(_ray.CanPick);
            Assert.AreEqual(DesignStatus.Empty, _workspace.Session.Status);
            Assert.AreEqual(CommitBarPhase.Empty, _workspace.CommitBar.Phase);
            _workspace.Open(); Assert.False(_ray.CanPick);
        }

        [UnityTest] public IEnumerator UncertainOutcomeNeedsAReviewAndNeverRepeatsTheCommit()
        {
            _backend.CommitError = new IOException("Lost response");
            ArmFlangeWithEdge(); Do(CommitIds.Preview); Do(CommitIds.Apply);
            yield return null; yield return null;
            Assert.AreEqual(CommitBarPhase.Uncertain, _workspace.CommitBar.Phase);
            Assert.AreEqual("Ho controllato il CAD", Act(CommitIds.Recover).Label);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(FlangeAction)); Assert.True(_workspace.RequiresCadReview);
            Assert.AreEqual(1, _backend.Commits);
            Do(CommitIds.Recover);
            Assert.AreEqual(1, _backend.Commits); Assert.False(_workspace.RequiresCadReview);
        }

        // ---------------------------------------------------------------- M6: declared actions

        private ActionCatalog CatalogFor()
        {
            var spaces = TestDocs.Create();
            var catalog = new ActionCatalog(spaces); catalog.SetActive(_workspace); return catalog;
        }

        private void AssertCatalogIsValid(string state)
        {
            var catalog = CatalogFor();
            var ids = _workspace.Actions.Select(a => a.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids, state + ": ids");
            Assert.True(ids.All(id => id.StartsWith("lamiera.") || id.StartsWith("commit.")), state + ": stable id namespaces");
            var known = new HashSet<string>(_workspace.Tabs.Select(t => t.Id)) { ActionCatalog.CommitTab };
            foreach (var action in _workspace.Actions) Assert.True(known.Contains(action.Tab), state + ": tab of " + action.Id);
            foreach (var tab in _workspace.Tabs)
                Assert.LessOrEqual(catalog.Palette(tab.Id).Count, ActionCatalog.MaxPalette, state + ": " + tab.Id);
            Assert.LessOrEqual(catalog.Palette(ActionCatalog.CommitTab).Count, ActionCatalog.MaxPalette);
            Assert.False(_workspace.Actions.Any(a => a.Label == "Precedenti" || a.Label == "Successivi"), state + ": no pagination buttons");
            Assert.False(_workspace.Actions.Any(a => a.Label.StartsWith("Blocca") || a.Label == "Ispeziona" || a.Label == "Dettagli errore"), state);
            foreach (var action in _workspace.Actions.Where(a => !a.Enabled)) Assert.False(string.IsNullOrEmpty(action.DisabledReason), state + ": reason of " + action.Id);
            CollectionAssert.AreEqual(new[] { "lamiera", "schizzo", "sviluppo", "vista" }, _workspace.Tabs.Where(t => !t.Hidden).Select(t => t.Id).ToArray(), state);
            Assert.AreEqual(new[] { "Lamiera", "Schizzo", "Sviluppo", "Vista", "Documento" }, catalog.Tabs.Select(t => t.Label).ToArray(), state);
        }

        [Test] public void ActionIdsAreUniqueAndEveryTabHasAtMostEightActionsInEveryState()
        {
            _backend.ExtraRules = 17; _backend.ExtraSketches = 11; _backend.FlatExists = false; Start(_backend);
            AssertCatalogIsValid("tools");
            ArmFlangeWithEdge(); AssertCatalogIsValid("flange draft");
            Do(CommitIds.Preview); AssertCatalogIsValid("flange preview");
            _backend.PreviewError = new InvalidOperationException("boom"); Do(Datum); AssertCatalogIsValid("flange error");
            _backend.PreviewError = null; Do(CommitIds.Cancel); AssertCatalogIsValid("cancelled");
            Do(Rule); AssertCatalogIsValid("rule list");
            Assert.AreEqual(3, _workspace.Tabs.Count(t => t.Id.StartsWith(LamieraWorkspace.PickTabPrefix)), "19 rules + thickness -> 3 list tabs");
            Pick("Alu_1mm"); AssertCatalogIsValid("rule draft");
            Do(CommitIds.Cancel);
            Do(Cut); AssertCatalogIsValid("sketch list");
            Assert.AreEqual(2, _workspace.Tabs.Count(t => t.Id.StartsWith(LamieraWorkspace.PickTabPrefix)), "13 sketches -> 2 list tabs");
            Pick("Contour • chiuso"); AssertCatalogIsValid("cut");
            Do(CommitIds.Cancel);
            Do(FlatCreate); AssertCatalogIsValid("flat pattern draft");
            Do(CommitIds.Cancel);
            _workspace.SetOnline(false); AssertCatalogIsValid("offline");
            _workspace.Close(); AssertCatalogIsValid("closed");
        }

        [Test] public void LongListsSplitIntoPagesOfEightAndNothingIsDropped()
        {
            _backend.ExtraRules = 17; Start(_backend);
            Do(Rule);
            var pages = _workspace.Tabs.Where(t => t.Id.StartsWith(LamieraWorkspace.PickTabPrefix)).ToList();
            Assert.AreEqual(3, pages.Count); StringAssert.Contains("1/3", pages[0].Label);
            var catalog = CatalogFor();
            var labels = pages.SelectMany(p => catalog.Palette(p.Id)).Select(a => a.Label).ToList();
            Assert.AreEqual(20, labels.Count); CollectionAssert.AllItemsAreUnique(labels);
            Assert.AreEqual(8, catalog.Palette(pages[0].Id).Count); Assert.AreEqual(4, catalog.Palette(pages[2].Id).Count);
            Assert.True(Enumerable.Range(0, 17).All(i => labels.Contains("Extra_" + i)), "every rule is reachable");
            Pick("Extra_16");
            Assert.False(_workspace.Tabs.Any(t => t.Id.StartsWith(LamieraWorkspace.PickTabPrefix)), "choosing closes the list");
            Assert.AreEqual("Extra_16", (string)_backend.LastOperations[0]["arguments"]["rule"]);
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void ActionsFollowTheArmedCommand()
        {
            Assert.False(Enabled(Height)); Assert.False(Enabled(Datum)); Assert.False(Enabled(ClearEdges)); Assert.False(Enabled(Extent));
            Assert.AreEqual("Scegli prima Flangia.", Act(Height).DisabledReason);
            Do(FlangeAction);
            Assert.True(Enabled(Height)); Assert.True(Enabled(Angle)); Assert.True(Enabled(Datum)); Assert.False(Enabled(ClearEdges));
            StringAssert.Contains("10 mm", Act(Height).Label); StringAssert.Contains("90", Act(Angle).Label); StringAssert.Contains("esterno", Act(Datum).Label);
            Assert.AreEqual(XrActionKind.Numeric, Act(Height).Kind);
            _workspace.Flange.ToggleEdge("ent_edge"); Assert.True(Enabled(ClearEdges));
            Do(Datum); StringAssert.Contains("interno", Act(Datum).Label);
            Assert.AreEqual("inner", (string)_backend.LastOperations[0]["arguments"]["height_datum"]);
            Do(ClearEdges); Assert.AreEqual(0, _workspace.Flange.EdgeIds.Count);
            Assert.False(Enabled(Extent));
        }

        [Test] public void FaceScreenHasNoCutOptions()
        {
            Assert.True(Enabled(Face)); Assert.True(Enabled(Cut));
            Do(Face);
            Assert.False(Enabled(Extent), "the face screen has no cut options");
            Assert.True(Enabled(ChangeSketch));
        }

        [UnityTest] public IEnumerator CommitBarPhasesFollowTheSessionState()
        {
            Assert.AreEqual(CommitBarPhase.Empty, _workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(CommitIds.Preview)); Assert.False(Enabled(CommitIds.Cancel));
            // draft: just armed
            Do(FlangeAction);
            Assert.AreEqual(CommitBarPhase.Draft, _workspace.CommitBar.Phase);
            Assert.True(Enabled(CommitIds.Preview)); Assert.True(Enabled(CommitIds.Cancel)); Assert.False(Enabled(CommitIds.Apply));
            // previewing, then ready
            _backend.PendingPreview = new TaskCompletionSource<DesignPreview>();
            _workspace.Flange.ToggleEdge("ent_edge"); Do(CommitIds.Preview);
            Assert.AreEqual(CommitBarPhase.Previewing, _workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.False(Enabled(CommitIds.Preview));
            _backend.PendingPreview.SetResult(new DesignPreview("late", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model));
            yield return null; yield return null;
            Assert.AreEqual(CommitBarPhase.Ready, _workspace.CommitBar.Phase);
            Assert.True(Enabled(CommitIds.Apply)); Assert.True(Enabled(CommitIds.Cancel));
            // an edit makes it a draft again and Apply goes away until the next preview
            _backend.PendingPreview = null;
            _workspace.Flange.SetHeight(30);
            Assert.AreEqual(CommitBarPhase.Draft, _workspace.CommitBar.Phase);
            Assert.False(Enabled(CommitIds.Apply)); Assert.True(Enabled(CommitIds.Preview));
            // error
            _backend.PreviewError = new InvalidOperationException("boom"); Do(CommitIds.Preview);
            Assert.AreEqual(CommitBarPhase.Error, _workspace.CommitBar.Phase); Assert.False(string.IsNullOrEmpty(_workspace.CommitBar.Message));
            Assert.False(Enabled(CommitIds.Apply)); Assert.True(Enabled(CommitIds.Cancel));
            _backend.PreviewError = null; Do(CommitIds.Cancel);
            Assert.AreEqual(CommitBarPhase.Empty, _workspace.CommitBar.Phase);
            // offline
            _workspace.SetOnline(false);
            Assert.AreEqual(CommitBarPhase.Offline, _workspace.CommitBar.Phase); Assert.False(Enabled(CommitIds.Apply));
        }

        [Test] public void ApplyIsEnabledOnlyWhenThePreviewIsReadyAndEditsBlockIt()
        {
            Assert.False(Enabled(CommitIds.Apply));
            ArmFlangeWithEdge(); Assert.False(Enabled(CommitIds.Apply), "a draft is not applicable");
            Do(CommitIds.Preview); Assert.True(Enabled(CommitIds.Apply));
            _workspace.Flange.SetHeight(30); Assert.False(Enabled(CommitIds.Apply), "an edit drops the preview");
            Do(CommitIds.Preview); Assert.True(Enabled(CommitIds.Apply));
            _workspace.SetOnline(false); Assert.False(Enabled(CommitIds.Apply));
        }

        [Test] public void RingActionsForPlanarFaceAndEdgeUseTheSameDeclaredActions()
        {
            CollectionAssert.AreEqual(new[] { FlangeAction, Face, Cut }, _workspace.ContextActions(UiSelectionKind.PlanarFace).Select(a => a.Id).ToArray());
            CollectionAssert.AreEqual(new[] { FlangeAction }, _workspace.ContextActions(UiSelectionKind.Edge).Select(a => a.Id).ToArray());
            Assert.IsEmpty(_workspace.ContextActions(UiSelectionKind.None));
            Assert.IsEmpty(_workspace.ContextActions(UiSelectionKind.Face));
            Assert.IsEmpty(_workspace.ContextActions(UiSelectionKind.Component));
            var catalog = CatalogFor();
            Assert.AreEqual(3, catalog.Context(UiSelectionKind.PlanarFace).Count); Assert.AreEqual(1, catalog.Context(UiSelectionKind.Edge).Count);
            Assert.True(_workspace.ContextActions(UiSelectionKind.Edge).All(a => a.Enabled));
            _workspace.SetOnline(false);
            Assert.True(_workspace.ContextActions(UiSelectionKind.PlanarFace).All(a => !a.Enabled), "the ring disables like the palette");
        }

        // ---------------------------------------------------------------- with the shell and the bench

        private UiShell AttachShell(out Workbench bench)
        {
            var catalog = new ActionCatalog(TestDocs.Create());
            var shell = UiShell.Create(Child("Left").transform, _root.transform.Find("Eye"), catalog);
            _roots.Add(shell.gameObject); _roots.Add(shell.CommitBar.Canvas.gameObject); _roots.Add(shell.Hud.Canvas.gameObject);
            bench = Child("Bench").AddComponent<Workbench>();
            _workspace.Attach(shell, bench, null, _xr);
            catalog.SetActive(_workspace);
            _workspace.Close(); _workspace.Open();   // a real Open with the bench in place
            return shell;
        }

        [Test] public void PaletteShowsTheLamieraTabsAndPickerTabsAreReachableWithTheStick()
        {
            var shell = AttachShell(out _);
            Assert.AreEqual("lamiera", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("schizzo", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("sviluppo", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("vista", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("documento", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("lamiera", shell.Palette.CurrentTab);
            _backend.ExtraRules = 8; Start(_backend);
            Do(Rule);
            Assert.AreEqual(LamieraWorkspace.PickTabPrefix + "0", shell.Palette.CurrentTab); Assert.True(shell.Palette.InTabGroup);
            Assert.AreEqual(8, shell.Palette.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            shell.Palette.SelectTab(+1); Assert.AreEqual(LamieraWorkspace.PickTabPrefix + "1", shell.Palette.CurrentTab);
            Assert.AreEqual(3, shell.Palette.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            shell.Palette.SelectTab(+1); Assert.AreEqual(LamieraWorkspace.PickTabPrefix + "0", shell.Palette.CurrentTab, "stick cycles inside the list");
            _workspace.Back();
            Assert.False(shell.Palette.InTabGroup); Assert.AreEqual("lamiera", shell.Palette.CurrentTab, "X returns to the tab it came from");
        }

        [Test] public void CommitBarSitsOnTheWorkPlaneAndFollowsThePhase()
        {
            var shell = AttachShell(out var bench);
            Assert.NotNull(bench.Frame);
            var expected = WorkbenchLayout.CommitBarPosition(bench.Frame);
            Assert.AreEqual((float)expected.Z, shell.CommitBar.transform.position.z, 1e-4f);
            Assert.AreEqual((float)expected.Y, shell.CommitBar.transform.position.y, 1e-4f);
            Assert.False(shell.CommitBar.Canvas.gameObject.activeSelf);
            ArmFlangeWithEdge();
            Assert.True(shell.CommitBar.Canvas.gameObject.activeSelf);
            Do(CommitIds.Cancel); Assert.False(shell.CommitBar.Canvas.gameObject.activeSelf);
        }

        [Test] public void OpeningPutsTheModelOnTheWorkPlaneAndClosingReleasesIt()
        {
            AttachShell(out var bench);
            bench.Snap();
            Assert.AreEqual(-(float)WorkbenchFrame.DefaultDeskDrop, _view.transform.position.y, 1e-4f);
            int closed = 0; _workspace.Closed += () => closed++;
            _workspace.Close(); _workspace.Close();
            Assert.AreEqual(1, closed, "Closed only fires for an open workspace");
            _view.transform.position = Vector3.one; bench.Snap();
            Assert.AreEqual(Vector3.one, _view.transform.position, "released: nothing moves the model any more");
        }

        [Test] public void ViewActionsFitAndRecenterTheWorkPlane()
        {
            AttachShell(out var bench);
            Assert.True(Enabled(LamieraWorkspace.IdFit)); Assert.True(Enabled(LamieraWorkspace.IdRecenter));
            _eye.transform.position = new Vector3(0, 1.6f, 0); _eye.transform.rotation = Quaternion.Euler(0, 90, 0);
            Do(LamieraWorkspace.IdRecenter);
            Assert.AreEqual(90, bench.Frame.YawDegrees, 1e-3);
            Do(LamieraWorkspace.IdFit);
            _workspace.Close();
            Assert.False(Enabled(LamieraWorkspace.IdFit), "closed: no view actions");
        }

        [Test] public void RingOnAnEdgeOffersFlangeWithTheEdgeAlreadySelected()
        {
            AttachShell(out _);
            var ring = Field<RingView>("_ring");
            typeof(LamieraWorkspace).GetMethod("SelectEdge", Flags).Invoke(_workspace, new object[] { "ent_edge" });
            Assert.True(ring.Visible);
            Assert.AreEqual(1, ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            _workspace.Back(); Assert.False(ring.Visible, "X closes the ring");
            typeof(LamieraWorkspace).GetMethod("SelectEdge", Flags).Invoke(_workspace, new object[] { "ent_edge" });
            ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>()[0].onClick.Invoke();   // Flangia
            Assert.False(ring.Visible); Assert.AreEqual(SheetMetalCommand.Flange, _workspace.Mode.Armed);
            CollectionAssert.AreEqual(new[] { "ent_edge" }, _workspace.Flange.EdgeIds.ToArray());
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public void RingOnAFaceOffersFlangeFaceAndCut()
        {
            AttachShell(out _);
            var ring = Field<RingView>("_ring");
            typeof(LamieraWorkspace).GetMethod("ShowRing", Flags).Invoke(_workspace, new object[] { UiSelectionKind.PlanarFace, Vector3.zero });
            Assert.True(ring.Visible); Assert.AreEqual(3, ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
        }

        [Test] public void ChipsAppearWithTheFlangeAndTheKeypadSetsTheDraft()
        {
            var shell = AttachShell(out _);
            var height = Field<ChipView>("_heightChip"); var angle = Field<ChipView>("_angleChip");
            Assert.False(height.Canvas.gameObject.activeSelf);
            ArmFlangeWithEdge();
            Assert.True(height.Canvas.gameObject.activeSelf); Assert.True(angle.Canvas.gameObject.activeSelf);
            StringAssert.Contains("10 mm", height.ValueText); StringAssert.Contains("90", angle.ValueText);
            Assert.True(height.Modified, "a draft that has not been previewed");
            height.Tap();
            Assert.True(shell.Palette.KeypadVisible); Assert.AreEqual(LamieraWorkspace.FieldFlangeHeight, _workspace.ActiveEntry.Id);
            Assert.AreEqual(LamieraWorkspace.FieldFlangeHeight, _workspace.ArmedField.Id);
            _workspace.ActiveEntry.Type('4'); _workspace.ActiveEntry.Type('2'); _workspace.ActiveEntry.Commit(out _);
            Assert.False(shell.Palette.KeypadVisible); Assert.AreEqual(42, _workspace.Flange.HeightMm);
            StringAssert.Contains("42 mm", height.ValueText);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.AreEqual(42, (double)_backend.LastOperations[0]["arguments"]["height_mm"]);
            angle.Tap(); Commit(45); Assert.AreEqual(45, _workspace.Flange.AngleDegrees);
            Do(CommitIds.Cancel); Assert.False(height.Canvas.gameObject.activeSelf);
        }

        [Test] public void KeypadCancelKeepsTheValueAndDictationConfirmsTheOpenKeypad()
        {
            var shell = AttachShell(out _);
            ArmFlangeWithEdge();
            Do(Height); Assert.True(shell.Palette.KeypadVisible);
            _workspace.Back();   // X closes the keypad first
            Assert.False(shell.Palette.KeypadVisible); Assert.IsNull(_workspace.ActiveEntry);
            Assert.AreEqual(10, _workspace.Flange.HeightMm); Assert.AreEqual(0, _backend.Previews);
            Do(Angle); Assert.True(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeAngle, 60), "dictation confirms the open keypad");
            Assert.AreEqual(60, _workspace.Flange.AngleDegrees); Assert.IsNull(_workspace.ActiveEntry); Assert.False(shell.Palette.KeypadVisible);
            Assert.False(_workspace.SetArmedField(LamieraWorkspace.FieldFlangeHeight, 20), "only the armed field accepts dictation");
        }

        [Test] public void ThicknessChipAppearsWithTheRuleCommand()
        {
            AttachShell(out _);
            var chip = Field<ChipView>("_thicknessChip");
            Assert.False(chip.Canvas.gameObject.activeSelf);
            Do(Rule);
            Assert.True(chip.Canvas.gameObject.activeSelf); StringAssert.Contains("2 mm", chip.ValueText);
            chip.Tap(); Commit(1.2);
            Assert.AreEqual(1.2, (double)_backend.LastOperations[0]["arguments"]["thickness_mm"]);
            StringAssert.Contains("1,2 mm", chip.ValueText);
        }

        [Test] public void ErrorDetailGoesToTheHudAndTheShortMessageToTheBar()
        {
            AttachShell(out _);
            _backend.PreviewError = new InvalidOperationException("Inventor said no");
            ArmFlangeWithEdge(); _hud.Clear(); Do(CommitIds.Preview);
            StringAssert.Contains("Inventor said no", AllHud());
            StringAssert.DoesNotContain("Inventor said no", _workspace.CommitBar.Message);
            Assert.AreEqual(CommitBarPhase.Error, _workspace.CommitBar.Phase);
            int count = _hud.Count(h => h.Contains("Inventor said no"));
            Do(CommitIds.Preview);
            Assert.AreEqual(count + 1, _hud.Count(h => h.Contains("Inventor said no")), "a new error of the same text is shown again once the previous one cleared");
        }

        [Test] public void NoUnityUiPanelIsLeftInTheWorkspace()
        {
            Assert.IsNull(_workspace.GetComponentInChildren<HomePanel>(true));
            Assert.IsNull(typeof(LamieraWorkspace).GetField("_panel", Flags));
            Assert.IsNull(typeof(LamieraWorkspace).GetMethod("Render", Flags));
            Assert.IsNull(typeof(LamieraWorkspace).GetMethod("Page", Flags));
            Assert.IsNull(_workspace.VoicePanel);
        }

        [Test] public void LamieraVoiceTargetWorksWithoutAPanel()
        {
            var target = InventorXrSo.Xr.Voice.WorkspaceVoiceTarget.ForLamiera(_workspace, CatalogFor());
            target.InSession = true;
            Assert.True(target.TryResolveAction("flangia", out var action));
            Assert.True(target.IsEnabled(action.Id));
            Assert.True(target.Invoke(action.Id));
            Assert.AreEqual(SheetMetalCommand.Flange, _workspace.Mode.Armed);
            Assert.False(target.TryResolveAction("applica", out _), "Applica is never resolved by voice");
        }
    }
}
