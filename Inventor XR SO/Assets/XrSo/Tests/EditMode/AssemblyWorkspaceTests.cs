using System;
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
using InventorXrSo.Xr.Voice;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

namespace InventorXrSo.Tests
{
    /// <summary>
    /// Assieme on the M6 shell: references, drafts, preview and Apply with no panel. Every command goes through the declared
    /// actions by stable id, like the palette, the ring and the commit bar do. Gestures arrive as synthetic XrInput frames.
    /// </summary>
    public sealed class AssemblyWorkspaceTests
    {
        private const string Apply = CommitIds.Apply;
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _root;
        private Material _material;
        private AssemblyWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private LoadedScene _scene;
        private Camera _eye;
        private ControllerRay _ray;
        private readonly List<string> _hud = new List<string>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<HapticPulse> _haptics = new List<HapticPulse>();
        private Action<HapticPulse, bool> _previousSink;
        private XrInput _xr;
        private XrInputFrame _frame;
        private float _clock;

        private sealed class Backend : IAssemblyWorkspaceBackend, IInspectionBackend
        {
            public DocumentState State = new DocumentState("doc_bolt", "r", "v");
            public GlbModel Model;
            public int Previews, Commits, ExtraOccurrences, Activations;
            public JArray LastOperations;
            public bool Grounded, DofComplete = true;
            public Exception PreviewError;
            public string LastActivated;
            public TaskCompletionSource<DocumentState> Mutation;
            public TaskCompletionSource<DesignPreview> Preview;
            public AssemblyContext Context(DocumentState state, string selected)
            {
                JObject Occ(string id) => new JObject { ["occurrence_id"] = id, ["name"] = id, ["editable"] = true, ["grounded"] = Grounded,
                    ["suppressed"] = false, ["adaptive"] = false, ["dof_translation"] = 1, ["dof_rotation"] = 1, ["dof_complete"] = DofComplete,
                    ["definition_document_id"] = "doc_bolt", ["definition_kind"] = "part",
                    ["translation_axes"] = new JArray { new JArray(1, 0, 0) }, ["rotation_axes"] = new JArray { new JArray(0, 0, 1) }, ["rotation_center_mm"] = new JArray(10, 20, 30) };
                var all = new JArray(Occ("ent_a"), Occ("ent_b"));
                for (int i = 0; i < ExtraOccurrences; i++) all.Add(Occ("ent_x" + i));
                var occurrences = selected == null ? all : new JArray(Occ(selected));
                var refs = selected == null ? new JArray() : new JArray(new JObject { ["id"] = selected + "_face", ["occurrence_id"] = selected,
                    ["name"] = "Faccia " + selected, ["kind"] = "face", ["geometry"] = "kPlaneSurface", ["point_mm"] = new JArray(10, 20, 30), ["axis"] = new JArray(0, 0, 1) });
                return AssemblyContext.Parse(new JObject { ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "assembly",
                    ["truncated"] = false, ["occurrences"] = occurrences, ["references"] = refs }, state);
            }
            public Task<AssemblyContext> GetAssemblyContextAsync(DocumentState state, string selected, CancellationToken ct) => Task.FromResult(Context(state, selected));
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
            {
                Previews++; LastOperations = (JArray)operations.DeepClone();
                if (PreviewError != null) return Task.FromException<DesignPreview>(PreviewError);
                return Preview?.Task ?? Task.FromResult(new DesignPreview("p" + Previews, state.DocumentId, state.Revision, DateTimeOffset.UtcNow.AddMinutes(1), Model, isAssembly: true));
            }
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct) { Commits++; State = new DocumentState(State.DocumentId, "r2", "v2"); return Mutation?.Task ?? Task.FromResult(State); }
            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => Task.FromResult(State);
            public Task<DesignHistory> GetHistoryAsync(DocumentState state, CancellationToken ct) => Task.FromResult(new DesignHistory(state, false, false, null));
            public Task<DocumentState> ApplyHistoryAsync(DesignHistory history, bool redo, CancellationToken ct) => Task.FromResult(State);
            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) => Task.FromResult<InspectionInfo>(null);
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<OpenDocument>>(new[] { new OpenDocument("doc_bolt", "Bolt", "kPartDocumentObject") });
            public Task ActivateOpenAsync(string documentId, CancellationToken ct) { Activations++; LastActivated = documentId; return Task.CompletedTask; }
        }

        [SetUp] public void Setup()
        {
            _hud.Clear(); _haptics.Clear(); _xr = null; _previousSink = Haptics.Sink; Haptics.Sink = (pulse, pen) => _haptics.Add(pulse);
            _root = new GameObject("Assembly test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _eye = Child("eye").AddComponent<Camera>();
            var hand = Child("hand"); _ray = hand.AddComponent<ControllerRay>(); var line = hand.AddComponent<LineRenderer>();
            line.sharedMaterial = _material; _ray.Configure(hand.transform, line);
            _view = Child("model").AddComponent<CadSceneView>(); _view.BodyMaterial = _material;
            _backend = new Backend { Model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)) };
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Test"",""definition_kind"":""assembly"",""children"":[
                {""name"":""Bolt"",""occurrence_id"":""ent_a"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]},
                {""name"":""Bolt 2"",""occurrence_id"":""ent_b"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",
                 ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.03,0,0,1],""children"":[]}]}}"));
            _scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { ["doc_bolt"] = _backend.Model }, new Dictionary<string, string> { ["doc_bolt"] = "a_test" }, new List<string>());
            _view.Show(_scene); _workspace = Child("workspace").AddComponent<AssemblyWorkspace>();
            _workspace.HudMessage += _hud.Add;
            _workspace.Initialize(_view, _ray, _eye.transform);
            Start();
        }

        private void Start()
        {
            _workspace.Bind(_backend); _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        private Transform Hand => _root.transform.Find("hand");

        [TearDown] public void TearDown()
        {
            Haptics.Sink = _previousSink;
            foreach (var name in new[] { "_moveChip", "_valueChip", "_clearChip", "_ring" })
            {
                var view = _workspace == null ? null : typeof(AssemblyWorkspace).GetField(name, Flags).GetValue(_workspace) as Component;
                if (view != null) Object.DestroyImmediate(view.gameObject);
            }
            foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r);
            _roots.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        private XrAction Act(string id) => _workspace.Actions.Single(a => a.Id == id);
        private bool Enabled(string id) => _workspace.Actions.Any(a => a.Id == id && a.Enabled);
        private bool Has(string id) => _workspace.Actions.Any(a => a.Id == id);
        /// <summary>Invokes a declared action by id, exactly like the palette, ring or commit bar would.</summary>
        private void Do(string id) { var a = Act(id); Assert.True(a.Enabled, id + ": " + a.DisabledReason); Assert.True(a.TryInvoke(), id); }
        private T Field<T>(string name) => (T)typeof(AssemblyWorkspace).GetField(name, Flags).GetValue(_workspace);
        private void Update() => typeof(AssemblyWorkspace).GetMethod("Update", Flags).Invoke(_workspace, null);
        private string AllHud() => string.Join("\n", _hud);
        private async Task Select(string id = "ent_a") => await _workspace.SelectOccurrenceAsync(id);
        private AssemblyReference Ref(string occurrence) => _backend.Context(_backend.State, occurrence).References[0];
        private DesignPreviewView PreviewView => _view.GetComponent<DesignPreviewView>();
        private void Commit(params double[] values)
        {
            foreach (var value in values)
            {
                var entry = _workspace.ActiveEntry; Assert.NotNull(entry, "a numeric entry is waiting for the keypad"); Assert.True(entry.Editing);
                Assert.True(entry.CommitValue(value, out var reason), reason);
            }
        }

        // ---------------------------------------------------------------- synthetic input

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
        private bool Dragging => (bool)typeof(AssemblyWorkspace).GetField("_dragging", Flags).GetValue(_workspace);

        private UiShell AttachShell(out Workbench bench, out ActionCatalog catalog)
        {
            catalog = new ActionCatalog(new SpacesActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => true, () => true, () => true, () => true));
            var shell = UiShell.Create(Child("Left").transform, _eye.transform, catalog);
            _roots.Add(shell.gameObject); _roots.Add(shell.CommitBar.Canvas.gameObject); _roots.Add(shell.Hud.Canvas.gameObject);
            bench = Child("Bench").AddComponent<Workbench>();
            _workspace.Attach(shell, bench, null, _xr);
            catalog.SetActive(_workspace);
            _workspace.Close(); _workspace.Open();   // a real Open with the bench in place
            return shell;
        }

        private void AimAtHandle(float scale = 1f)
        {
            _view.transform.localScale = Vector3.one * scale;
            var start = _view.transform.TransformPoint(new Vector3(-0.01f, 0.02f, 0.03f));
            var end = _view.transform.TransformPoint(new Vector3(-0.11f, 0.02f, 0.03f));
            var position = Vector3.Lerp(start, end, 0.5f) + Vector3.forward * 0.1f;
            Hand.SetPositionAndRotation(position, Quaternion.LookRotation((Vector3.Lerp(start, end, 0.5f) - position).normalized));
        }

        private void AimAtSelectedBody(float scale = 1f)
        {
            _view.transform.localScale = Vector3.one * scale;
            Physics.SyncTransforms();
            var body = _view.GetComponentsInChildren<CadBody>().First(b => b.Instance.OccurrenceId == "ent_a");
            var center = body.GetComponent<Collider>().bounds.center;
            var position = center + Vector3.back * 0.5f;
            Hand.SetPositionAndRotation(position, Quaternion.LookRotation(center - position));
            Physics.SyncTransforms();
            Assert.True(CadRaycaster.TryPick(new Ray(Hand.position, Hand.forward), 20, out var hit, out _, out _));
            Assert.AreSame(body, hit);
        }

        private void AimAtRotationHandle(float scale)
        {
            _view.transform.localScale = Vector3.one * scale;
            var center = _view.transform.TransformPoint(CadCoordinates.ToLocal(new CadPoint(10, 20, 30)));
            var axis = _view.transform.TransformDirection(CadCoordinates.ToLocal(new CadPoint(0, 0, 1))).normalized;
            var radial = _view.transform.TransformDirection(Vector3.right).normalized * (0.06f * scale);
            var target = center + radial;
            var position = target + axis * (0.1f * scale);
            Hand.SetPositionAndRotation(position, Quaternion.LookRotation((target - position).normalized));
        }

        // ---------------------------------------------------------------- apply cycle

        [Test] public async Task ApplyRequiresRenderedPreviewAndRefreshesAfterCommit()
        {
            await Select(); _workspace.BeginMove();
            Assert.False(Enabled(Apply));
            await _workspace.PreviewAsync(); Assert.True(Enabled(Apply));
            Assert.True(PreviewView.IsShowing);
            Do(Apply); Assert.AreEqual(1, _backend.Commits);
            Assert.False(Enabled(Apply)); Assert.False(_workspace.RequiresCadReview);
            Assert.AreEqual(CommitBarPhase.Applied, _workspace.CommitBar.Phase, "MarkApplied after a successful apply");
        }

        [Test] public async Task RebuildingSameRevisionSceneRevokesPreviewUntilRenderedAgain()
        {
            await Select(); _workspace.BeginMove(); await _workspace.PreviewAsync();
            Assert.True(Enabled(Apply)); Assert.True(PreviewView.IsShowing);

            _view.Show(_scene); _workspace.SetScene(_scene);

            Assert.False(Enabled(Apply));
            await _workspace.ApplyAsync(); Assert.AreEqual(0, _backend.Commits);
            await _workspace.PreviewAsync(); Assert.True(Enabled(Apply));
            Do(Apply); Assert.AreEqual(1, _backend.Commits);
        }

        [Test] public async Task ClearingRenderedPreviewRevokesApplyProof()
        {
            await Select(); _workspace.BeginMove(); await _workspace.PreviewAsync();
            var preview = _view.GetComponents<DesignPreviewView>().First(p => p.IsShowing);
            preview.Clear();

            Assert.False(Enabled(Apply), "no rendered ghost, no Apply");
            await _workspace.ApplyAsync();

            Assert.AreEqual(0, _backend.Commits); Assert.False(Enabled(Apply));
        }

        [Test] public async Task SecondReferenceAutomaticallyPreviewsSelectedConstraint()
        {
            _workspace.ChooseConstraint("mate");
            _workspace.ChooseReference(Ref("ent_a")); Assert.AreEqual(0, _backend.Previews);
            _workspace.ChooseReference(Ref("ent_b"));
            Assert.AreEqual(1, _backend.Previews); Assert.True(Enabled(Apply)); await Task.CompletedTask;
        }

        [Test] public void SelectingReferencesFirstOffersCompatibleConstraintsWithoutAutomaticPreview()
        {
            _workspace.ChooseReference(Ref("ent_a"));
            _workspace.ChooseReference(Ref("ent_b"));
            Assert.AreEqual(0, _backend.Previews);
            Assert.True(Has(AssemblyWorkspace.IdConstraintPrefix + "mate")); Assert.True(Has(AssemblyWorkspace.IdConstraintPrefix + "flush"));
            Assert.True(Has(AssemblyWorkspace.IdConstraintPrefix + "angle"));
            Assert.False(Has(AssemblyWorkspace.IdConstraintPrefix + "insert")); Assert.False(Enabled(Apply));
            Do(AssemblyWorkspace.IdConstraintPrefix + "flush");
            Assert.AreEqual(1, _backend.Previews); Assert.True(Enabled(Apply));
            Assert.AreEqual("assembly_constraint", (string)_backend.LastOperations[0]["command"]);
        }

        [Test] public async Task SelectingReferencesAfterCadMoveDoesNotPreviewStaleMove()
        {
            await Select(); _workspace.BeginMove();
            await _workspace.PreviewAsync(); Assert.True(Enabled(Apply));
            int previewsBefore = _backend.Previews;

            _workspace.ChooseReference(Ref("ent_a"));
            _workspace.ChooseReference(Ref("ent_b"));

            Assert.AreEqual(previewsBefore, _backend.Previews);
            Assert.False(Enabled(Apply));
            Assert.True(Has(AssemblyWorkspace.IdConstraintPrefix + "mate"));
            Assert.True(Has(AssemblyWorkspace.IdConstraintPrefix + "flush"));
            Assert.True(Has(AssemblyWorkspace.IdConstraintPrefix + "angle"));
            Assert.False(PreviewView.IsShowing);
        }

        [Test] public async Task DisconnectInvalidatesPreviewAndLateResults()
        {
            await Select(); _workspace.BeginMove();
            _backend.Preview = new TaskCompletionSource<DesignPreview>(); var request = _workspace.PreviewAsync();
            _workspace.SetOnline(false);
            _backend.Preview.SetResult(new DesignPreview("late", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model, isAssembly: true));
            await request; Assert.False(Enabled(Apply)); Assert.False(PreviewView.IsShowing);
            Assert.AreEqual(CommitBarPhase.Offline, _workspace.CommitBar.Phase);
        }

        [Test] public async Task PendingAndUnknownMutationSurviveCloseAndRebind()
        {
            await Select(); _workspace.BeginMove(); await _workspace.PreviewAsync();
            _backend.Mutation = new TaskCompletionSource<DocumentState>(); var apply = _workspace.ApplyAsync();
            Assert.True(_workspace.RequiresCadReview); _workspace.Close(); _workspace.Bind(null); Assert.True(_workspace.RequiresCadReview);
            _backend.Mutation.SetException(new IOException("connection lost")); await apply;
            _workspace.Bind(_backend); Assert.True(_workspace.RequiresCadReview);
        }

        [Test] public void OtherWorkspaceCanPreventEntry()
        { _workspace.Close(); _workspace.CanEnter = () => false; _workspace.Open(); Assert.False(_workspace.Active); }

        [Test] public void RotationIgnoresSwingAndMeasuresOnlyAllowedAxisTwist()
        {
            var start = Quaternion.Euler(15, 30, 50);
            Assert.That(AssemblyManipulation.TwistDegrees(start, Quaternion.AngleAxis(40, Vector3.up) * start, Vector3.up), Is.EqualTo(40).Within(0.001));
            Assert.That(AssemblyManipulation.TwistDegrees(start, Quaternion.AngleAxis(40, Vector3.right) * start, Vector3.up), Is.EqualTo(0).Within(0.001));
            Assert.That(AssemblyManipulation.TwistDegrees(start, Quaternion.AngleAxis(40, Vector3.right) * Quaternion.AngleAxis(-25, Vector3.up) * start, Vector3.up), Is.EqualTo(-25).Within(0.001));
        }

        [Test] public async Task NativeDofGizmoHasNoCadCollidersAndFollowsModelScale()
        {
            await Select(); _workspace.BeginMove();
            var gizmo = _view.GetComponentsInChildren<LineRenderer>().First(l => l.name == "DOF traslazione 1");
            Assert.AreEqual(CadCoordinates.ToLocal(new CadPoint(10, 20, 30)), gizmo.GetPosition(0));
            _view.transform.localScale = Vector3.one * 0.1f;
            Assert.AreEqual(0, gizmo.GetComponents<Collider>().Length);
            Assert.That(Vector3.Distance(gizmo.transform.TransformPoint(gizmo.GetPosition(0)), gizmo.transform.TransformPoint(gizmo.GetPosition(1))), Is.EqualTo(0.01f).Within(0.00001f));
        }

        // ---------------------------------------------------------------- declared actions, tabs, commit bar

        [Test] public void ActionsUseTheStableIdsAndTheSharedCommandIds()
        {
            var ids = _workspace.Actions.Select(a => a.Id).ToArray();
            foreach (var id in new[] { AssemblyWorkspace.IdComponents, AssemblyWorkspace.IdIsolate, AssemblyWorkspace.IdRelease, AssemblyWorkspace.IdMove,
                AssemblyWorkspace.IdOpen, AssemblyWorkspace.IdOpenDesign, AssemblyWorkspace.IdOpenLamiera, AssemblyWorkspace.IdActivate,
                AssemblyWorkspace.IdConstrain, AssemblyWorkspace.IdJoint, AssemblyWorkspace.IdReferences, AssemblyWorkspace.IdClearance,
                AssemblyWorkspace.IdFit, AssemblyWorkspace.IdRecenter, AssemblyWorkspace.IdUndo, AssemblyWorkspace.IdRedo,
                CommitIds.Preview, CommitIds.Apply, CommitIds.Cancel, CommitIds.Recover })
                CollectionAssert.Contains(ids, id);
            Assert.True(ids.All(id => id.StartsWith("assembly.") || id.StartsWith("commit.")), "every id is assembly.* or commit.*");
            Assert.AreEqual(ids.Length, ids.Distinct().Count(), "ids are unique");
            CollectionAssert.AreEqual(new[] { "componenti", "vincoli", "vista" }, _workspace.Tabs.Select(t => t.Id).ToArray());
            Assert.True(_workspace.Actions.Where(a => a.Tab != ActionCatalog.CommitTab).All(a => _workspace.Tabs.Any(t => t.Id == a.Tab)));
        }

        [Test] public async Task EveryTabHasAtMostEightActionsAndListsAreSplitIntoPickerTabs()
        {
            _backend.ExtraOccurrences = 20; Start();
            var catalog = new ActionCatalog(new SpacesActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => true, () => true, () => true, () => true));
            catalog.SetActive(_workspace);   // throws on a duplicate id
            foreach (var tab in catalog.Tabs) Assert.LessOrEqual(catalog.Palette(tab.Id).Count, ActionCatalog.MaxPalette, tab.Id);
            Do(AssemblyWorkspace.IdComponents);
            Assert.AreEqual(3, _workspace.Tabs.Count(t => t.Id.StartsWith(AssemblyWorkspace.PickTabPrefix)));
            Assert.AreEqual(8, catalog.Palette(AssemblyWorkspace.PickTabPrefix + "0").Count);
            Assert.AreEqual(8, catalog.Palette(AssemblyWorkspace.PickTabPrefix + "1").Count);
            Assert.AreEqual(6, catalog.Palette(AssemblyWorkspace.PickTabPrefix + "2").Count);
            Assert.False(_workspace.Actions.Any(a => a.Label == "Precedenti" || a.Label == "Successivi"));
            var last = _workspace.Actions.Single(a => a.Label == "ent_x19");
            Assert.True(last.TryInvoke());
            await Task.CompletedTask;
            Assert.AreEqual(0, _workspace.Tabs.Count(t => t.Id.StartsWith(AssemblyWorkspace.PickTabPrefix)), "choosing closes the list");
        }

        [Test] public async Task ReferencePickerListsTheSelectedComponentFacesAndEdgesAndRayModeToggles()
        {
            Assert.False(Enabled(AssemblyWorkspace.IdReferences), "no component selected");
            StringAssert.Contains("Seleziona prima un componente", Act(AssemblyWorkspace.IdReferences).DisabledReason);
            await Select();
            Do(AssemblyWorkspace.IdReferences);
            var item = _workspace.Actions.Single(a => a.Id.StartsWith(AssemblyWorkspace.IdPickPrefix));
            StringAssert.Contains("Faccia ent_a", item.Label);
            Do(item.Id);
            StringAssert.Contains("A: Faccia ent_a", AllHud());
            Assert.False(Act(AssemblyWorkspace.IdRayMode).IsOn);
            Do(AssemblyWorkspace.IdRayMode); Assert.True(Act(AssemblyWorkspace.IdRayMode).IsOn);
        }

        [Test] public async Task CommitBarFollowsTheDraftPreviewApplyAndCancelPhases()
        {
            var bar = _workspace.CommitBar;
            Assert.AreEqual(CommitBarPhase.Empty, bar.Phase);
            await Select(); Assert.AreEqual(CommitBarPhase.Empty, bar.Phase, "selecting alone drafts nothing");
            _workspace.BeginMove();
            Assert.AreEqual(CommitBarPhase.Draft, bar.Phase); Assert.True(Enabled(CommitIds.Preview)); Assert.False(Enabled(Apply)); Assert.True(Enabled(CommitIds.Cancel));
            Do(CommitIds.Preview);
            Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(CommitBarPhase.Ready, bar.Phase); Assert.True(Enabled(Apply));
            Do(CommitIds.Cancel);
            Assert.AreEqual(CommitBarPhase.Empty, bar.Phase); Assert.False(Enabled(Apply)); Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public async Task ApplyIsOnlyOnTheCommitBarAndNeverFromVoiceOrOtherActions()
        {
            await Select(); _workspace.BeginMove(); Do(CommitIds.Preview);
            Assert.True(Enabled(Apply));
            Assert.False(_workspace.Actions.Any(a => a.Id != Apply && a.Label.StartsWith("Applica")), "no other Applica");
            Assert.False(Act(Apply).VoiceInvokes);
            Assert.True(_workspace.IsEnabled(CommandIds.Apply));
            Assert.True(_workspace.Invoke(CommandIds.Apply), "the voice id only shows the plan");
            Assert.AreEqual(0, _backend.Commits);
            StringAssert.Contains("Applica", AllHud());
            Assert.False(_workspace.InvokeVoiceAction("Applica"), "Applica is never run by voice");
            Assert.AreEqual(0, _backend.Commits);
            var target = WorkspaceVoiceTarget.ForWorkspaces(null, null, _workspace, null); target.InSession = true;
            Assert.False(target.TryResolveAction("applica", out _), "Applica is never resolved by voice");
            Do(Apply); Assert.AreEqual(1, _backend.Commits);
        }

        [Test] public async Task PreviewErrorGoesToTheHudAndTheShortMessageToTheBar()
        {
            _backend.PreviewError = new InvalidOperationException("Inventor said no");
            await Select(); _workspace.BeginMove(); _hud.Clear(); Do(CommitIds.Preview);
            StringAssert.Contains("Inventor said no", AllHud());
            StringAssert.DoesNotContain("Inventor said no", _workspace.CommitBar.Message);
            Assert.AreEqual(CommitBarPhase.Error, _workspace.CommitBar.Phase);
            Assert.False(Enabled(Apply)); Assert.True(_haptics.Contains(HapticPulse.Error));
        }

        [Test] public async Task OccurrenceSummaryAndBlockersGoToTheHud()
        {
            _backend.Grounded = true;
            await Select();
            StringAssert.Contains("ent_a • Gradi di libertà: 2 • Fissato", AllHud());
            Assert.False(Enabled(AssemblyWorkspace.IdMove)); StringAssert.Contains("fissato", Act(AssemblyWorkspace.IdMove).DisabledReason);
            Assert.False(Enabled(CommitIds.Preview));
        }

        [Test] public async Task OfflineDisablesEveryCadActionWithItsReason()
        {
            await Select();
            _workspace.SetOnline(false);
            foreach (var id in new[] { AssemblyWorkspace.IdComponents, AssemblyWorkspace.IdMove, AssemblyWorkspace.IdConstrain, AssemblyWorkspace.IdJoint,
                AssemblyWorkspace.IdClearance, AssemblyWorkspace.IdRefresh, CommitIds.Preview, Apply })
            { Assert.False(Enabled(id), id); }
            StringAssert.Contains("Offline", Act(AssemblyWorkspace.IdMove).DisabledReason);
            StringAssert.Contains("Offline", AllHud());
            Assert.AreEqual(CommitBarPhase.Offline, _workspace.CommitBar.Phase);
        }

        // ---------------------------------------------------------------- chips and keypad

        [Test] public async Task MoveChipAndKeypadSetTheDraftDistanceAndPreview()
        {
            UseInput();
            var shell = AttachShell(out _, out _);
            var chip = Field<ChipView>("_moveChip");
            await Select(); Assert.False(chip.Canvas.gameObject.activeSelf);
            _workspace.BeginMove();
            Assert.True(chip.Canvas.gameObject.activeSelf); Assert.True(Field<ChipView>("_clearChip").Canvas.gameObject.activeSelf);
            Assert.False(Field<ChipView>("_valueChip").Canvas.gameObject.activeSelf);
            Assert.True(chip.Modified, "a draft that has not been previewed");
            chip.Tap();
            Assert.True(shell.Palette.KeypadVisible); Assert.AreEqual(AssemblyWorkspace.FieldMoveMm, _workspace.ActiveEntry.Id);
            Assert.AreEqual(AssemblyWorkspace.FieldMoveMm, _workspace.ArmedField.Id);
            _workspace.ActiveEntry.Type('1'); _workspace.ActiveEntry.Type('2'); Assert.True(_workspace.ActiveEntry.Commit(out _));
            Assert.False(shell.Palette.KeypadVisible); Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.AreEqual(12, (double)_backend.LastOperations[0]["arguments"]["translation_mm"][0], 1e-9);
            StringAssert.Contains("12 mm", chip.ValueText);
            Do(AssemblyWorkspace.IdMoveMode);   // rotation: another entry, another unit
            Do(AssemblyWorkspace.IdMoveValue); Commit(30);
            Assert.AreEqual(30, (double)_backend.LastOperations[0]["arguments"]["rotation_degrees"], 1e-9);
            Do(CommitIds.Cancel); Assert.False(chip.Canvas.gameObject.activeSelf);
        }

        [Test] public async Task ConfirmingTheSameValueStillPreviewsADraftWithoutPreview()
        {
            UseInput(); AttachShell(out _, out _);
            await Select(); _workspace.BeginMove();
            Assert.AreEqual(0, _backend.Previews);
            Do(AssemblyWorkspace.IdMoveValue); Commit(0);
            Assert.AreEqual(1, _backend.Previews, "an unchanged value still asks for the missing preview");
            Assert.AreEqual(0, _backend.Commits);
        }

        [Test] public async Task RelationValueChipFollowsTheConstraintUnit()
        {
            UseInput(); AttachShell(out _, out _);
            _workspace.ChooseReference(Ref("ent_a")); _workspace.ChooseReference(Ref("ent_b"));
            Do(AssemblyWorkspace.IdConstraintPrefix + "angle");
            var chip = Field<ChipView>("_valueChip");
            Assert.True(chip.Canvas.gameObject.activeSelf); StringAssert.Contains("Angolo", chip.ValueText);
            Assert.AreEqual(1, _backend.Previews);
            Do(AssemblyWorkspace.IdRelationValue); Assert.AreEqual(AssemblyWorkspace.FieldValueDeg, _workspace.ActiveEntry.Id);
            Commit(45);
            Assert.AreEqual(45, (double)_backend.LastOperations[0]["arguments"]["angle_degrees"], 1e-9);
            Do(AssemblyWorkspace.IdFlip); Assert.AreEqual(3, _backend.Previews);
            await Task.CompletedTask;
        }

        [Test] public void ClearanceUsesTheKeypadAndDictationConfirmsTheOpenKeypad()
        {
            UseInput(); var shell = AttachShell(out _, out _);
            Do(AssemblyWorkspace.IdClearance); Assert.True(shell.Palette.KeypadVisible);
            _workspace.Back();   // X closes the keypad first
            Assert.False(shell.Palette.KeypadVisible); Assert.IsNull(_workspace.ActiveEntry);
            Do(AssemblyWorkspace.IdClearance);
            Assert.True(_workspace.SetArmedField(AssemblyWorkspace.FieldClearance, 2.5), "dictation confirms the open keypad");
            StringAssert.Contains("2,5 mm", Act(AssemblyWorkspace.IdClearance).Label);
            Assert.False(shell.Palette.KeypadVisible);
            Assert.False(_workspace.SetArmedField(AssemblyWorkspace.FieldMoveMm, 20), "only the armed field accepts dictation");
            Assert.False(_workspace.SetArmedField(AssemblyWorkspace.FieldClearance, 20000), "out of range is refused");
        }

        // ---------------------------------------------------------------- gestures (synthetic XrInput)

        [Test] public async Task PlainGripMovesViewWithoutPreview()
        {
            UseInput(); await Select(); var before = _view.transform.position;
            Hand.SetPositionAndRotation(new Vector3(0, 0, -1), Quaternion.identity);
            _frame.PenGrip = true; Poll();
            Hand.position += Vector3.right * 0.25f; Update();
            Assert.That(Vector3.Distance(_view.transform.position, before), Is.EqualTo(0.25f).Within(0.001)); Assert.AreEqual(0, _backend.Previews);
            _frame.PenGrip = false; Poll();
        }

        [TestCase(1f)] [TestCase(0.1f)] public async Task TriggerHeldOnTheHandleMovesTenMillimetresAndReleasePreviewsOnce(float scale)
        {
            UseInput(); await Select();
            _view.transform.SetPositionAndRotation(new Vector3(0.3f, -0.2f, 0.4f), Quaternion.Euler(15f, 25f, -10f));
            _workspace.BeginMove(); AimAtHandle(scale);
            PressTrigger(); Assert.True(Dragging, "Trigger held on the handle captures it");
            Hand.position += _view.transform.TransformVector(new Vector3(-0.01f, 0, 0));
            Update();
            Assert.AreEqual(0, _backend.Previews, "nothing is previewed while the Trigger is held");
            ReleaseTrigger();
            Assert.False(Dragging); Assert.AreEqual(1, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            var op = (JObject)_backend.LastOperations[0]; Assert.AreEqual("assembly_move", (string)op["command"]);
            var delta = (JArray)op["arguments"]["translation_mm"];
            Assert.That((double)delta[0], Is.EqualTo(10).Within(0.05));
            Assert.That((double)delta[1], Is.EqualTo(0).Within(0.05)); Assert.That((double)delta[2], Is.EqualTo(0).Within(0.05));
        }

        [TestCase(1f)] [TestCase(0.1f)] public async Task SelectedBodyStartsIntentionalMoveAtBothScales(float scale)
        {
            UseInput(); await Select(); _workspace.BeginMove(); AimAtSelectedBody(scale);
            PressTrigger(); Assert.True(Dragging);
            Hand.position += _view.transform.TransformVector(new Vector3(-0.01f, 0, 0));
            Update(); ReleaseTrigger();
            Assert.AreEqual(1, _backend.Previews);
            var delta = (JArray)((JObject)_backend.LastOperations[0])["arguments"]["translation_mm"];
            Assert.That((double)delta[0], Is.EqualTo(10).Within(0.05));
        }

        [Test] public async Task BodyGestureBeforeCadMoveCannotStartPreview()
        {
            UseInput(); await Select(); AimAtSelectedBody();
            PressTrigger(); Assert.False(Dragging); Hand.position += Vector3.left * 0.01f;
            Update(); ReleaseTrigger();
            Assert.AreEqual(0, _backend.Previews); Assert.False(Enabled(Apply));
        }

        [TestCase(1f)] [TestCase(0.1f)] public async Task TriggerHeldOnTheRingRotatesThirtyCadDegrees(float scale)
        {
            UseInput(); await Select();
            _view.transform.SetPositionAndRotation(new Vector3(-0.25f, 0.18f, 0.3f), Quaternion.Euler(-12f, 18f, 9f));
            _view.transform.localScale = Vector3.one * scale;
            _workspace.BeginMove(); Do(AssemblyWorkspace.IdMoveMode);
            AimAtRotationHandle(scale);
            PressTrigger(); Assert.True(Dragging);
            var axis = _view.transform.TransformDirection(CadCoordinates.ToLocal(new CadPoint(0, 0, 1))).normalized;
            Hand.rotation = Quaternion.AngleAxis(-30f, axis) * Hand.rotation;
            Update(); ReleaseTrigger();
            Assert.AreEqual(1, _backend.Previews);
            var args = (JObject)((JObject)_backend.LastOperations[0])["arguments"];
            Assert.That((double)args["rotation_degrees"], Is.EqualTo(30).Within(0.1));
        }

        [Test] public async Task PrecisionTriggerMakesTheDragTenTimesSlower()
        {
            UseInput(); await Select(); _workspace.BeginMove(); AimAtHandle();
            PressTrigger(); _frame.PaletteTrigger = true; Poll(); Assert.True(_xr.Precision);
            Update();   // the next frame re-anchors the drag at the slower rate
            Hand.position += _view.transform.TransformVector(new Vector3(-0.01f, 0, 0));
            Update(); ReleaseTrigger();
            var delta = (JArray)((JObject)_backend.LastOperations[0])["arguments"]["translation_mm"];
            Assert.That((double)delta[0], Is.EqualTo(1).Within(0.05), "10 mm of hand = 1 mm of component with precision");
        }

        [Test] public async Task TrackingLossCancelsArmedGestureWithoutPreview()
        {
            UseInput(); await Select(); _workspace.BeginMove(); AimAtHandle();
            PressTrigger(); Assert.True(Dragging);
            _frame.PenTracked = false; Poll(); ReleaseTrigger();
            Assert.False(Dragging); Assert.AreEqual(0, _backend.Previews);
            Assert.False(Enabled(Apply)); Assert.False(PreviewView.IsShowing);
            StringAssert.Contains("Tracking perso", _workspace.Notice); StringAssert.Contains("Tracking perso", AllHud());
            Assert.True(_haptics.Contains(HapticPulse.Error));
        }

        [Test] public async Task UiHitBlocksTheGesture()
        {
            UseInput(); await Select(); _workspace.BeginMove(); AimAtHandle();
            _workspace.UiHitOverride = () => true;
            PressTrigger(); Assert.False(Dragging); Hand.position += Vector3.left * 0.1f;
            Update(); ReleaseTrigger(); Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task GripHeldTriggerIsViewOnlyAndNeverCapturesTheHandle()
        {
            UseInput(); await Select(); _workspace.BeginMove(); AimAtHandle();
            _frame.PenGrip = true; Poll();
            PressTrigger(); Assert.False(Dragging, "Grip+Trigger is no longer the move gesture");
            ReleaseTrigger(); _frame.PenGrip = false; Poll();
            Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task ClosingOrHidingTheWorkspaceEndsTheCapture()
        {
            UseInput(); await Select(); _workspace.BeginMove(); AimAtHandle();
            PressTrigger(); Assert.True(Dragging);
            _workspace.Close(); Assert.False(Dragging);
            Hand.position += Vector3.left * 0.1f; ReleaseTrigger();
            Assert.AreEqual(0, _backend.Previews, "a capture closed by the workspace never previews");

            Start(); await Select(); _workspace.BeginMove(); AimAtHandle();
            PressTrigger(); Assert.True(Dragging);
            _workspace.SetVisible(false); Assert.False(Dragging);
            ReleaseTrigger(); Assert.AreEqual(0, _backend.Previews);
        }

        [TestCase(true, true)] [TestCase(false, false)] public async Task GroundedOrIncompleteDofCannotBegin(bool grounded, bool complete)
        {
            _backend.Grounded = grounded; _backend.DofComplete = complete;
            UseInput(); await Select(); _workspace.BeginMove(); AimAtHandle();
            Assert.False(Enabled(AssemblyWorkspace.IdMove));
            PressTrigger(); Assert.False(Dragging); ReleaseTrigger();
            Assert.AreEqual(0, _backend.Previews); Assert.False(Enabled(Apply));
        }

        // ---------------------------------------------------------------- ring and isolation

        [Test] public void RingOnAComponentOffersIsolateMoveConstrainOpen()
        {
            AttachShell(out _, out var catalog);
            var labels = _workspace.ContextActions(UiSelectionKind.Component).Select(a => a.Label).ToArray();
            CollectionAssert.AreEqual(new[] { "Isola", "Sposta", "Vincola", "Apri" }, labels);
            Assert.AreEqual(4, catalog.Context(UiSelectionKind.Component).Count);
            Assert.AreEqual(0, catalog.Context(UiSelectionKind.PlanarFace).Count);
            var enabled = _workspace.ContextActions(UiSelectionKind.Component).Where(a => a.Enabled).Select(a => a.Label).ToArray();
            CollectionAssert.AreEqual(new[] { "Vincola" }, enabled, "no component selected yet: the ring disables like the palette (Vincola opens the list of constraint types)");
        }

        [Test] public async Task TriggerOnAComponentSelectsItAndOpensTheRing()
        {
            UseInput(); AttachShell(out _, out _);
            var ring = Field<RingView>("_ring");
            AimAtSelectedBody();
            PressTrigger(); ReleaseTrigger();
            Assert.True(ring.Visible); Assert.AreEqual(4, ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            StringAssert.Contains("ent_a", AllHud());
            Assert.True(Enabled(AssemblyWorkspace.IdIsolate)); Assert.True(Enabled(AssemblyWorkspace.IdMove));
            _workspace.Back(); Assert.False(ring.Visible, "X closes the ring");
            await Task.CompletedTask;
        }

        [Test] public async Task RingMoveStartsTheMoveDraftAndHidesTheRing()
        {
            UseInput(); AttachShell(out _, out _);
            var ring = Field<RingView>("_ring");
            AimAtSelectedBody(); PressTrigger(); ReleaseTrigger();
            Assert.True(ring.Visible);
            ring.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>()[1].onClick.Invoke();   // Sposta
            Assert.False(ring.Visible); Assert.AreEqual(CommitBarPhase.Draft, _workspace.CommitBar.Phase);
            Assert.AreEqual(0, _backend.Commits); Assert.AreEqual(0, _backend.Previews);
            await Task.CompletedTask;
        }

        private Vector3 CenterOf(string occurrence)
        {
            var instance = _view.Find(occurrence);
            return instance.transform.TransformPoint(ScenePlacement.LocalBounds(instance.transform).center);
        }

        private Renderer[] Renderers(string occurrence) => _view.Find(occurrence).Bodies.Select(b => b.Renderer).ToArray();

        [Test] public async Task IsolateBringsTheComponentHalfwayAndFadesTheRestWithoutTouchingTheCad()
        {
            _view.transform.position = new Vector3(0, 0, 2);
            await Select();
            var iso = _workspace.Isolation; var home = _view.Find("ent_a").transform.localPosition;
            var before = CenterOf("ent_a"); var eyeOrigin = _eye.transform.position;
            Do(AssemblyWorkspace.IdIsolate);
            Assert.True(iso.Active); Assert.AreEqual("ent_a", iso.OccurrenceId); Assert.True(iso.Tweening, "no jump: it moves over ~250 ms");
            Assert.AreEqual(before, CenterOf("ent_a"), "not moved yet");
            iso.Snap();
            Assert.That(Vector3.Distance(CenterOf("ent_a"), (before + eyeOrigin) * 0.5f), Is.LessThan(1e-3f), "halfway to the user");
            Assert.True(Renderers("ent_b").All(r => !r.enabled), "the rest is replaced by a ghost");
            Assert.AreEqual(Renderers("ent_b").Length, iso.FadedBodies);
            var ghost = _view.Find("ent_b").GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "IsolationGhost");
            Assert.AreEqual(ComponentIsolation.FadeAlpha, ghost.sharedMaterial.GetColor("_Color").a, 1e-4f);
            Assert.True(Renderers("ent_a").All(r => r.enabled), "the isolated component keeps its own look");
            Assert.True(iso.IsFaded("ent_b")); Assert.False(iso.IsFaded("ent_a"));
            Assert.AreEqual(0, _backend.Previews); Assert.AreEqual(0, _backend.Commits);
            Assert.AreEqual(CommitBarPhase.Empty, _workspace.CommitBar.Phase, "isolation is not a CAD draft");
            Assert.AreEqual("r", (await _backend.GetDocumentStateAsync(CancellationToken.None)).Revision, "revision unchanged");
            Assert.False(Enabled(AssemblyWorkspace.IdIsolate)); Assert.True(Enabled(AssemblyWorkspace.IdRelease));

            Do(AssemblyWorkspace.IdRelease);
            Assert.False(iso.Active); Assert.True(Renderers("ent_b").All(r => r.enabled)); Assert.AreEqual(0, iso.FadedBodies);
            iso.Snap();
            Assert.AreEqual(home, _view.Find("ent_a").transform.localPosition);
            Assert.AreEqual(before, CenterOf("ent_a"));
            Assert.AreEqual(0, _view.Find("ent_b").GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == "IsolationGhost"));
        }

        [Test] public async Task XReleasesTheIsolatedComponentBeforeAnythingElse()
        {
            UseInput(); await Select(); Do(AssemblyWorkspace.IdIsolate); _workspace.Isolation.Snap();
            _workspace.BeginMove();   // Sposta starts a draft: the component goes back first
            Assert.False(_workspace.Isolation.Active, "moving draws the gizmo at the real place");
            Do(AssemblyWorkspace.IdIsolate); Assert.True(_workspace.Isolation.Active);
            _workspace.Back(); Assert.False(_workspace.Isolation.Active, "X releases");
            Assert.AreEqual(CommitBarPhase.Draft, _workspace.CommitBar.Phase, "the draft is still there");
            _workspace.Back(); Assert.AreEqual(CommitBarPhase.Empty, _workspace.CommitBar.Phase, "a second X drops the command");
        }

        [Test] public async Task IsolationEndsWithTheWorkspaceTheSelectionAndTheScene()
        {
            _view.transform.position = new Vector3(0, 0, 2);
            await Select(); var home = _view.Find("ent_a").transform.localPosition;
            Do(AssemblyWorkspace.IdIsolate); _workspace.Isolation.Snap();
            await Select("ent_b"); Assert.False(_workspace.Isolation.Active, "another selection releases");
            _workspace.Isolation.Snap(); Assert.AreEqual(home, _view.Find("ent_a").transform.localPosition);

            await Select(); Do(AssemblyWorkspace.IdIsolate); _workspace.Isolation.Snap();
            _workspace.Close();
            Assert.False(_workspace.Isolation.Active); Assert.False(_workspace.Isolation.Tweening, "closing restores at once");
            Assert.AreEqual(home, _view.Find("ent_a").transform.localPosition);
            Assert.True(Renderers("ent_b").All(r => r.enabled));

            Start(); await Select(); Do(AssemblyWorkspace.IdIsolate);
            _view.Show(_scene); _workspace.SetScene(_scene);
            Assert.False(_workspace.Isolation.Active, "a rebuilt scene starts clean");
        }

        [Test] public async Task OpenInProgettazioneAndLamieraWorkFromTheIsolatedComponent()
        {
            await Select();
            Assert.False(Enabled(AssemblyWorkspace.IdOpenDesign)); StringAssert.Contains("Isola prima", Act(AssemblyWorkspace.IdOpenDesign).DisabledReason);
            Assert.False(Enabled(AssemblyWorkspace.IdOpenLamiera));
            Do(AssemblyWorkspace.IdIsolate);
            int design = 0, lamiera = 0; _workspace.DesignRequested += () => design++; _workspace.LamieraRequested += () => lamiera++;
            Do(AssemblyWorkspace.IdOpenDesign);
            Assert.AreEqual(1, design); Assert.AreEqual(0, lamiera); Assert.AreEqual("doc_bolt", _backend.LastActivated);
            Do(AssemblyWorkspace.IdOpenLamiera);
            Assert.AreEqual(1, lamiera); Assert.AreEqual(2, _backend.Activations);
            Assert.AreEqual(0, _backend.Commits); Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task RingApriIsolatesAPartAndOffersTheOpenActions()
        {
            await Select();
            Do(AssemblyWorkspace.IdOpen);
            Assert.True(_workspace.Isolation.Active);
            StringAssert.Contains("Apri in Progettazione", AllHud());
            Assert.True(Enabled(AssemblyWorkspace.IdOpenDesign)); Assert.True(Enabled(AssemblyWorkspace.IdOpenLamiera));
        }

        [Test] public async Task VoiceResolvesIsolateOnTheCatalogLabelsAndRunsTheSameAction()
        {
            var target = WorkspaceVoiceTarget.ForWorkspaces(null, null, _workspace, null); target.InSession = true;
            Assert.False(target.TryResolveAction("isola", out var none) && none.Enabled, "no component selected: disabled");
            await Select();
            Assert.True(target.TryResolveAction("isola", out var action)); Assert.True(action.Enabled);
            Assert.True(target.Invoke(action.Id));
            Assert.True(_workspace.Isolation.Active);
            Assert.AreEqual(0, _backend.Commits);
        }

        // ---------------------------------------------------------------- shell, workbench, no panel

        [Test] public void PaletteShowsTheAssemblyTabsAndPickerTabsAreReachableWithTheStick()
        {
            var shell = AttachShell(out _, out _);
            Assert.AreEqual("componenti", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("vincoli", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("vista", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("spazi", shell.Palette.CurrentTab);
            shell.Palette.SelectTab(+1); Assert.AreEqual("componenti", shell.Palette.CurrentTab);
            _backend.ExtraOccurrences = 8; Start();
            Do(AssemblyWorkspace.IdComponents);
            Assert.AreEqual(AssemblyWorkspace.PickTabPrefix + "0", shell.Palette.CurrentTab); Assert.True(shell.Palette.InTabGroup);
            Assert.AreEqual(8, shell.Palette.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            shell.Palette.SelectTab(+1); Assert.AreEqual(AssemblyWorkspace.PickTabPrefix + "1", shell.Palette.CurrentTab);
            Assert.AreEqual(2, shell.Palette.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            _workspace.Back();
            Assert.False(shell.Palette.InTabGroup); Assert.AreEqual("componenti", shell.Palette.CurrentTab, "X returns to the tab it came from");
        }

        [Test] public void OpeningRaisesTheAssemblyAndClosingReleasesIt()
        {
            AttachShell(out var bench, out _);
            bench.Snap();
            Assert.True(bench.Raised);
            var expected = WorkbenchLayout.Assembly(bench.Frame, ExtentOf());
            Assert.AreEqual((float)expected.Position.Z, _view.transform.position.z, 1e-3f);
            Assert.AreEqual(-(float)WorkbenchLayout.AssemblyDrop, _view.transform.position.y, 1e-3f);
            Assert.AreEqual((float)expected.Scale, _view.transform.localScale.x, 1e-4f);
            int closed = 0; _workspace.Closed += () => closed++;
            _workspace.Close(); _workspace.Close();
            Assert.AreEqual(1, closed, "Closed only fires for an open workspace");
            Assert.False(bench.Raised);
            _view.transform.position = Vector3.one; bench.Snap();
            Assert.AreEqual(Vector3.one, _view.transform.position, "released: nothing moves the model any more");
        }

        private double ExtentOf()
        {
            var size = ScenePlacement.LocalBounds(_view.transform).size;
            return Math.Max(size.x, Math.Max(size.y, size.z));
        }

        [Test] public void ViewActionsFitAndRecenterTheRaisedPose()
        {
            AttachShell(out var bench, out _);
            Assert.True(Enabled(AssemblyWorkspace.IdFit)); Assert.True(Enabled(AssemblyWorkspace.IdRecenter));
            _eye.transform.position = new Vector3(0, 1.6f, 0); _eye.transform.rotation = Quaternion.Euler(0, 90, 0);
            Do(AssemblyWorkspace.IdRecenter);
            Assert.AreEqual(90, bench.Frame.YawDegrees, 1e-3); Assert.True(bench.Raised);
            Do(AssemblyWorkspace.IdFit);
            _workspace.Close();
            Assert.False(Enabled(AssemblyWorkspace.IdFit), "closed: no view actions");
        }

        [Test] public async Task CommitBarSitsOnTheWorkPlaneAndFollowsThePhase()
        {
            var shell = AttachShell(out var bench, out _);
            var expected = WorkbenchLayout.CommitBarPosition(bench.Frame);
            Assert.AreEqual((float)expected.Z, shell.CommitBar.transform.position.z, 1e-4f);
            Assert.AreEqual((float)expected.Y, shell.CommitBar.transform.position.y, 1e-4f);
            Assert.False(shell.CommitBar.Canvas.gameObject.activeSelf);
            await Select(); _workspace.BeginMove();
            Assert.True(shell.CommitBar.Canvas.gameObject.activeSelf);
            Do(CommitIds.Cancel); Assert.False(shell.CommitBar.Canvas.gameObject.activeSelf);
        }

        [Test] public async Task NoUnityUiPanelNorOvrInputIsLeftInTheWorkspace()
        {
            Assert.IsNull(_workspace.GetComponentInChildren<HomePanel>(true));
            Assert.IsNull(typeof(AssemblyWorkspace).GetField("_panel", Flags));
            Assert.IsNull(typeof(AssemblyWorkspace).GetMethod("Render", Flags));
            Assert.IsNull(typeof(AssemblyWorkspace).GetMethod("Page", Flags));
            Assert.IsNull(typeof(AssemblyWorkspace).GetMethod("ProcessControllerFrame", Flags));
            Assert.IsNull(_workspace.VoicePanel);
            foreach (var file in new[] { "AssemblyWorkspace.cs", "AssemblyActions.cs" })
                StringAssert.DoesNotContain("OVRInput", File.ReadAllText(Path.Combine(Application.dataPath, "XrSo/Xr/" + file)), file);
            await Task.CompletedTask;
        }
    }
}
