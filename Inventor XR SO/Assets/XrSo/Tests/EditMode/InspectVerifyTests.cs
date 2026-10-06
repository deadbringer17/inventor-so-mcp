using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Verify;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    /// <summary>M7 in Ispeziona: Visibilita and Verifica tabs, results list, focus and the two-selection distance flow (synthetic input, FakeAddIn-style backend).</summary>
    public class InspectVerifyTests
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _root;
        private InspectWorkspace _workspace;
        private CadSceneView _view;
        private ControllerRay _ray;
        private EnvironmentModeController _env;
        private ActionCatalog _catalog;
        private UiShell _shell;
        private XrInput _xr;
        private XrInputFrame _frame;
        private float _clock;
        private bool _other;
        private readonly List<string> _hud = new List<string>();
        private readonly List<GameObject> _roots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _hud.Clear(); _other = false;
            _root = new GameObject("Test rig");
            var eye = Child("Eye").AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 1.6f, 0);
            var left = Child("Left"); var right = Child("Right");
            _ray = right.AddComponent<ControllerRay>(); _ray.Configure(right.transform, right.AddComponent<LineRenderer>());
            _view = Child("Model").AddComponent<CadSceneView>();
            var visuals = _view.gameObject.AddComponent<SelectionVisuals>(); visuals.Configure(_view, null, null);
            _env = Child("Environment").AddComponent<EnvironmentModeController>(); _env.Configure(eye, null);
            _workspace = Child("Workspace").AddComponent<InspectWorkspace>();
            _workspace.SuspendProbe = () => _other;
            _workspace.HudMessage += _hud.Add;
            _workspace.Initialize(_view, visuals, _ray, eye.transform, _env);
            _catalog = new ActionCatalog(TestDocs.Create());
            _shell = UiShell.Create(left.transform, eye.transform, _catalog);
            _roots.Add(_shell.gameObject); _roots.Add(_shell.CommitBar.Canvas.gameObject); _roots.Add(_shell.Hud.Canvas.gameObject);
            _xr = Child("XrInput").AddComponent<XrInput>(); _xr.Source = new SyntheticInputSource();
            Assert.True(_xr.Synthetic, "the runner log must call this input synthetic");
            _frame = new XrInputFrame { PenTracked = true, PaletteTracked = true };
            _workspace.Attach(_shell, _xr);
            _catalog.SetActive(_workspace);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            _workspace.SetVisible(true);
            _xr.Poll(_frame, _clock += 0.016f);
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }

        [TearDown]
        public void TearDown()
        {
            foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r);
            _roots.Clear();
            Object.DestroyImmediate(_root);
        }

        private XrAction Act(string id) => _workspace.Actions.Single(a => a.Id == id);
        private bool Enabled(string id) => _workspace.Actions.Any(a => a.Id == id && a.Enabled);
        private void Do(string id) { var a = Act(id); Assert.True(a.Enabled, id + ": " + a.DisabledReason); Assert.True(a.TryInvoke(), id); }
        private string AllHud() => string.Join("\n", _hud);

        private sealed class VerifyBackend : IInspectionBackend, IVerifyBackend
        {
            public TaskCompletionSource<InterferenceReport> Interference = new TaskCompletionSource<InterferenceReport>();
            public IReadOnlyList<string> LastScope;
            public string LastA, LastB;
            public TaskCompletionSource<DistanceReport> DistanceHold;
            public DistanceReport Distance = DistanceReport.FromJson(new JObject { ["revision"] = "r", ["distance_mm"] = 30.0,
                ["point_a"] = new JArray(0, 0, 0), ["point_b"] = new JArray(30, 0, 0) });
            public HealthReport Health = HealthReport.FromJson(new JObject { ["revision"] = "r", ["healthy"] = false,
                ["failing_constraints"] = new JArray(new JObject { ["name"] = "M7_Sick", ["health"] = "kInconsistentHealth", ["a_occurrence_id"] = "ent_occ_1" }),
                ["bom"] = new JObject { ["valid"] = true } });

            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) => Task.FromResult(InspectionInfo.FromJson(new JObject()));
            public Task<FaceFeatureInfo> GetFaceFeatureAsync(DocumentState state, string faceId, CancellationToken ct) => throw new NotImplementedException();
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenDocument>>(Array.Empty<OpenDocument>());
            public Task ActivateOpenAsync(string documentId, CancellationToken ct) => Task.CompletedTask;
            public Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> ids, CancellationToken ct) { LastScope = ids; return Interference.Task; }
            public Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string a, string b, CancellationToken ct) { LastA = a; LastB = b; return DistanceHold != null ? DistanceHold.Task : Task.FromResult(Distance); }
            public Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct) => Task.FromResult(Health);
        }

        private static InterferenceReport OnePair() => InterferenceReport.FromJson(new JObject
        {
            ["revision"] = "r", ["analyzed"] = 2, ["count"] = 1, ["total_volume_mm3"] = 2000.0,
            ["pairs"] = new JArray(new JObject { ["a_occurrence_id"] = "ent_occ_1", ["b_occurrence_id"] = "ent_occ_2", ["a_name"] = "Bolt:1", ["b_name"] = "Bolt:2",
                ["volume_mm3"] = 2000.0, ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(0, 0, 0), ["max_mm"] = new JArray(5, 5, 5) }) }),
        });

        private VerifyBackend Online()
        {
            var backend = new VerifyBackend();
            _workspace.Bind(backend, null);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            _workspace.SetOnline(true);
            return backend;
        }

        private void Select(string occurrenceId) =>
            typeof(InspectWorkspace).GetField("_selected", Flags).SetValue(_workspace,
                ((BrowserContext)typeof(InspectWorkspace).GetField("_context", Flags).GetValue(_workspace)).Find(occurrenceId));

        private T Field<T>(string name) => (T)typeof(InspectWorkspace).GetField(name, Flags).GetValue(_workspace);

        /// <summary>The async void continuation of the workspace may need a few turns of the loop in EditMode.</summary>
        private static async Task Until(Func<bool> condition)
        {
            for (int i = 0; i < 10 && !condition(); i++) await Task.Yield();
        }

        [Test]
        public void TabsAddVisibilityAndVerifyAfterViewWithAtMostEightActions()
        {
            CollectionAssert.AreEqual(new[] { "ispeziona", "misura", "sezione", "visibilita", "verifica", "esplora", "_ispeziona_esci" }, _workspace.Tabs.Select(t => t.Id).ToArray());   // M9: Vista is the shared ViewActions tab
            Assert.That(_catalog.Palette("vista").Count, Is.InRange(1, 8), "Scala and Ambiente share the Vista tab");
            CollectionAssert.AreEqual(new[] { "Ispeziona ▸", "Misura", "Sezione", "Visibilità", "Verifica", "Esplora", "Documento" }, _catalog.Tabs.Select(t => t.Label).ToArray());
            foreach (var tab in _workspace.Tabs) Assert.That(_catalog.Palette(tab.Id).Count, Is.InRange(1, 8), tab.Id);
            Assert.True(_workspace.Actions.Where(a => a.Id.StartsWith("inspect.verify.") || a.Id.StartsWith("inspect.visibility.")).All(a => !a.VoiceInvokes));
        }

        [Test]
        public void VisibilityWorksOnTheSelectionWithoutBackendCalls()
        {
            var backend = Online(); Select("ent_occ_1"); _workspace.SetOnline(false);
            Do(InspectWorkspace.IdXRay);
            var visibility = Field<ComponentVisibility>("_visibility");
            Assert.AreEqual(OccurrenceVisibility.Ghost, visibility.Get("ent_occ_1"));
            Do(InspectWorkspace.IdHide);
            Assert.AreEqual(OccurrenceVisibility.Hidden, visibility.Get("ent_occ_1"));
            Do(InspectWorkspace.IdIsolate);
            Assert.AreEqual(OccurrenceVisibility.Ghost, visibility.Get("ent_occ_2"));
            Do(InspectWorkspace.IdShowAll);
            Assert.False(visibility.AnyChanged);
            Assert.IsNull(backend.LastScope, "no Inventor call");
        }

        [Test]
        public void InventorVerificationsAreDisabledWithAReasonOfflineOrOnAPart()
        {
            Online(); _workspace.SetOnline(false);
            foreach (var id in new[] { InspectWorkspace.IdInterference, InspectWorkspace.IdHealth, InspectWorkspace.IdDistance })
            {
                Assert.False(Enabled(id), id);
                Assert.False(string.IsNullOrEmpty(Act(id).DisabledReason), id);
            }
        }

        [Test]
        public void InventorVerificationsOnAPartDocumentSayAnAssemblyIsNeeded()
        {
            Online();
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_part"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[],""root"":{""name"":""Part.ipt"",""definition_kind"":""part"",""children"":[]}}"));
            _workspace.SetScene(new LoadedScene(graph, new Dictionary<string, GlbModel>(), new Dictionary<string, string>(), new List<string>()));
            foreach (var id in new[] { InspectWorkspace.IdInterference, InspectWorkspace.IdHealth, InspectWorkspace.IdDistance })
            {
                Assert.False(Enabled(id), id);
                Assert.AreEqual("Serve un assieme.", Act(id).DisabledReason, id);
            }
        }

        [Test]
        public async Task InterferenceRunsOnceListsResultsAndFocusesARow()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            Assert.False(Enabled(InspectWorkspace.IdHealth), "one Inventor verification at a time");
            Assert.True(Enabled(InspectWorkspace.IdIgnore));
            backend.Interference.SetResult(OnePair());
            await Until(() => Field<IReadOnlyList<VerifyFinding>>("_findings").Count > 0);
            var findings = Field<IReadOnlyList<VerifyFinding>>("_findings");
            Assert.AreEqual(1, findings.Count);
            Assert.That(AllHud(), Does.Contain("1 interferenze su 2 occorrenze"));
            Do(InspectWorkspace.IdResults);
            var row = _workspace.Actions.First(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix) && a.Label.Contains("Bolt:1 ↔ Bolt:2"));
            Assert.True(row.TryInvoke());
            Assert.AreEqual(1, Field<VerifyOverlay>("_overlay").BoxCount);
            Assert.AreEqual(OccurrenceVisibility.Normal, Field<ComponentVisibility>("_visibility").Get("ent_occ_1"));
            _workspace.Back();
            Assert.AreEqual(0, Field<VerifyOverlay>("_overlay").BoxCount, "Back leaves the focus and restores the visibility");
            Assert.False(Field<ComponentVisibility>("_visibility").AnyChanged);
        }

        /// <summary>One pair where only Bolt:1 is a known occurrence, so Bolt:2 is not involved.</summary>
        private static InterferenceReport PairWithOnlyBoltOne() => InterferenceReport.FromJson(new JObject
        {
            ["revision"] = "r", ["analyzed"] = 2, ["count"] = 1, ["total_volume_mm3"] = 2000.0,
            ["pairs"] = new JArray(new JObject { ["a_occurrence_id"] = "ent_occ_1", ["b_occurrence_id"] = "ent_occ_other", ["a_name"] = "Bolt:1", ["b_name"] = "Other:1",
                ["volume_mm3"] = 2000.0, ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(0, 0, 0), ["max_mm"] = new JArray(5, 5, 5) }) }),
        });

        [Test]
        public async Task AnInterferenceResultAppliesTheGlobalXRayPulsingViewAndBackRestores()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            backend.Interference.SetResult(PairWithOnlyBoltOne());
            var overlay = Field<VerifyOverlay>("_overlay"); var visibility = Field<ComponentVisibility>("_visibility");
            await Until(() => overlay.BoxCount > 0);
            Assert.AreEqual(OccurrenceVisibility.Normal, visibility.Get("ent_occ_1"), "involved part stays solid");
            Assert.AreEqual(OccurrenceVisibility.Ghost, visibility.Get("ent_occ_2"), "uninvolved part goes X-Ray");
            Assert.Greater(overlay.TintedBodies, 0, "involved part is tinted red");
            Assert.AreEqual(1, overlay.BoxCount);
            Assert.True(overlay.Pulsing);
            Assert.That(_hud.Last(), Does.Contain("Vista X-Ray con le interferenze in rosso. Indietro per tornare alla vista."));
            _workspace.Back();
            Assert.False(visibility.AnyChanged, "Back restores the visibility");
            Assert.AreEqual(0, overlay.BoxCount);
            Assert.AreEqual(0, overlay.TintedBodies);
            Assert.False(overlay.Pulsing);
        }

        [Test]
        public async Task ZeroInterferencesApplyNoView()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            backend.Interference.SetResult(InterferenceReport.FromJson(new JObject { ["revision"] = "r", ["analyzed"] = 2, ["count"] = 0, ["total_volume_mm3"] = 0.0, ["pairs"] = new JArray() }));
            await Until(() => Field<VerifySession>("_verifySession").Gate.CanStart);
            var overlay = Field<VerifyOverlay>("_overlay");
            Assert.False(Field<ComponentVisibility>("_visibility").AnyChanged);
            Assert.AreEqual(0, overlay.BoxCount); Assert.AreEqual(0, overlay.TintedBodies); Assert.False(overlay.Pulsing);
            Assert.That(_hud.Last(), Does.Not.Contain("Vista X-Ray"));
        }

        [Test]
        public async Task HealthDoesNotApplyTheGlobalInterferenceView()
        {
            Online();
            Do(InspectWorkspace.IdHealth);
            await Until(() => Field<IReadOnlyList<VerifyFinding>>("_findings").Count > 0);
            Assert.Greater(Field<IReadOnlyList<VerifyFinding>>("_findings").Count, 0);
            var overlay = Field<VerifyOverlay>("_overlay");
            Assert.False(Field<ComponentVisibility>("_visibility").AnyChanged);
            Assert.AreEqual(0, overlay.TintedBodies); Assert.AreEqual(0, overlay.BoxCount); Assert.False(overlay.Pulsing);
        }

        [Test]
        public void ThePulseStopsAfterFiveSecondsAndLeavesFixedValues()
        {
            Online();
            var overlay = Field<VerifyOverlay>("_overlay");
            overlay.ShowBoxes(new[] { new VerifyBox(new double[] { 0, 0, 0 }, new double[] { 5, 5, 5 }) });
            var material = overlay.TintMaterial;
            Assert.AreEqual(0f, material.GetFloat("_PulseDepth"), "no pulse before StartPulse");
            overlay.StartPulse(100f);
            Assert.True(overlay.Pulsing);
            Assert.AreEqual(VerifyOverlay.PulseDepth, material.GetFloat("_PulseDepth"), 1e-4);
            Assert.AreEqual(0f, material.GetFloat("_PulseWave"), 1e-4, "cycle starts at full alpha");
            overlay.TickPulse(102.4f);
            Assert.True(overlay.Pulsing, "still pulsing at 2.4 s");
            overlay.TickPulse(100f + 0.5f / VerifyOverlay.PulseHz);
            Assert.AreEqual(1f, material.GetFloat("_PulseWave"), 1e-3, "mid-cycle is the deepest dip, tint and boxes share the wave");
            overlay.TickPulse(100f + VerifyOverlay.PulseSeconds);
            Assert.False(overlay.Pulsing);
            Assert.AreEqual(0f, material.GetFloat("_PulseDepth"));
            Assert.AreEqual(0f, material.GetFloat("_PulseWave"));
            overlay.Clear();
        }

        [Test]
        public void TheGhostMaterialKeepsThePulseOff()
        {
            var ghost = GhostBodies.CreateMaterial("ghost test", ComponentVisibility.GhostColor);
            try
            {
                Assert.AreEqual(0f, ghost.GetFloat("_PulseDepth"));
                Assert.AreEqual(0f, ghost.GetFloat("_PulseWave"));
            }
            finally { Object.DestroyImmediate(ghost); }
        }

        [Test]
        public async Task ScopeSelectionSendsTheSelectedOccurrence()
        {
            var backend = Online(); Select("ent_occ_1");
            Do(InspectWorkspace.IdScope);
            Do(InspectWorkspace.IdInterference);
            CollectionAssert.AreEqual(new[] { "ent_occ_1" }, backend.LastScope);
            backend.Interference.SetResult(OnePair());
            await Until(() => Field<IReadOnlyList<VerifyFinding>>("_findings").Count > 0);
        }

        [Test]
        public async Task ANewRevisionMarksTheResultsStale()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            backend.Interference.SetResult(OnePair());
            await Until(() => Field<IReadOnlyList<VerifyFinding>>("_findings").Count > 0);
            var state = Field<DocumentState>("_documentState");
            _workspace.SetDocumentState(new DocumentState(state.DocumentId, "r2", state.VisualRevision));
            Assert.True(Field<bool>("_findingsStale"));
            Do(InspectWorkspace.IdResults);
            Assert.True(_workspace.Actions.Any(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix) && a.Label.StartsWith("[obsoleto]")));
        }

        [Test]
        public async Task ASceneOfANewRevisionMarksTheResultsStaleBeforeTheDocumentStateArrives()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            backend.Interference.SetResult(OnePair());
            await Until(() => Field<IReadOnlyList<VerifyFinding>>("_findings").Count > 0);
            // SessionController: SceneLoaded (SetScene) comes first, DocumentStateChanged after.
            var scene = CadSceneViewTests.BoltScene("r2"); _view.Show(scene); _workspace.SetScene(scene);
            Assert.True(Field<bool>("_findingsStale"));
            Assert.AreEqual("Modello cambiato: rilancia la verifica.", _hud.Last());
            _workspace.SetDocumentState(scene.Graph.State);
            Assert.AreEqual("Modello cambiato: rilancia la verifica.", _hud.Last(), "the generic notice does not overwrite it");
            Do(InspectWorkspace.IdResults);
            Assert.True(_workspace.Actions.Any(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix) && a.Label.StartsWith("[obsoleto]")));
        }

        [Test]
        public async Task ANewRevisionClearsAPendingDistanceTogetherWithTheSelection()
        {
            var backend = Online();
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Assert.NotNull(Field<SceneNode>("_distanceA"));
            var scene = CadSceneViewTests.BoltScene("r2"); _view.Show(scene); _workspace.SetScene(scene);
            Assert.Null(Field<SceneNode>("_distanceA"));
            await Task.Yield();
        }

        [Test]
        public async Task AnAnswerComputedBeforeTheRevisionMovedIsShownStaleNotFresh()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            var state = Field<DocumentState>("_documentState");
            _workspace.SetDocumentState(new DocumentState(state.DocumentId, "r2", state.VisualRevision));   // while the request runs
            backend.Interference.SetResult(OnePair());   // computed on "r"
            await Until(() => Field<IReadOnlyList<VerifyFinding>>("_findings").Count > 0);
            Assert.True(Field<bool>("_findingsStale"));
            Assert.AreEqual(VerifyStatus.Stale, Field<VerifySession>("_verifySession").Interference.Status);
            Assert.AreEqual("Modello cambiato: rilancia la verifica.", _hud.Last());
        }

        [Test]
        public async Task ADistanceAnsweredOnAnOlderRevisionIsNotDrawn()
        {
            var backend = Online();
            backend.DistanceHold = new TaskCompletionSource<DistanceReport>();
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Select("ent_occ_2"); Do(InspectWorkspace.IdDistance);
            var state = Field<DocumentState>("_documentState");
            _workspace.SetDocumentState(new DocumentState(state.DocumentId, "r2", state.VisualRevision));
            backend.DistanceHold.SetResult(backend.Distance);
            await Until(() => Field<VerifySession>("_verifySession").Gate.CanStart);
            Assert.False(Field<VerifyOverlay>("_overlay").HasDistance);
            Assert.That(AllHud(), Does.Contain("Modello cambiato"));
        }

        [Test]
        public async Task IgnoreDropsTheAnswer()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            Do(InspectWorkspace.IdIgnore);
            backend.Interference.SetResult(OnePair());
            await Until(() => Enabled(InspectWorkspace.IdHealth));
            Assert.True(Enabled(InspectWorkspace.IdHealth), "the gate is free once the ignored answer arrived");
            Assert.AreEqual(0, Field<IReadOnlyList<VerifyFinding>>("_findings").Count);
        }

        [Test]
        public async Task SuspendingIspezionaDuringARunKeepsTheAnswerButDrawsAndAnnouncesNothing()
        {
            var backend = Online();
            Do(InspectWorkspace.IdInterference);
            int hudBefore = _hud.Count;
            _other = true;
            backend.Interference.SetResult(OnePair());
            await Until(() => Field<VerifySession>("_verifySession").Gate.CanStart);
            Assert.True(Field<VerifySession>("_verifySession").Gate.CanStart);
            Assert.AreEqual(0, Field<VerifyOverlay>("_overlay").BoxCount);
            Assert.AreEqual(1, Field<IReadOnlyList<VerifyFinding>>("_findings").Count, "M9: a suspension keeps the result for when the tools resume");
            Assert.That(string.Join(" | ", _hud.Skip(hudBefore)), Does.Not.Contain("interferenze"));
        }

        [Test]
        public async Task BackDuringTheDistanceRunDropsTheAnswer()
        {
            var backend = Online();
            backend.DistanceHold = new TaskCompletionSource<DistanceReport>();
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Select("ent_occ_2"); Do(InspectWorkspace.IdDistance);
            _workspace.Back();
            backend.DistanceHold.SetResult(backend.Distance);
            await Until(() => Field<VerifySession>("_verifySession").Gate.CanStart);
            Assert.False(Field<VerifyOverlay>("_overlay").HasDistance);
        }

        [Test]
        public async Task DistanceTakesTwoSelectionsAndDrawsTheInventorLine()
        {
            var backend = Online();
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Select("ent_occ_2"); Do(InspectWorkspace.IdDistance);
            await Until(() => Field<VerifyOverlay>("_overlay").HasDistance);
            Assert.AreEqual("ent_occ_1", backend.LastA); Assert.AreEqual("ent_occ_2", backend.LastB);
            var overlay = Field<VerifyOverlay>("_overlay");
            Assert.True(overlay.HasDistance);
            Assert.AreEqual("30 mm", overlay.DistanceLabel);
            Assert.That(AllHud(), Does.Contain("Distanza minima (Inventor): 30 mm"));
        }

        [Test]
        public async Task WithoutInventorPointsTheLineIsIndicative()
        {
            var backend = Online();
            backend.Distance = DistanceReport.FromJson(new JObject { ["revision"] = "r", ["distance_mm"] = 30.0 });
            Select("ent_occ_1"); Do(InspectWorkspace.IdDistance);
            Select("ent_occ_2"); Do(InspectWorkspace.IdDistance);
            await Until(() => Field<VerifyOverlay>("_overlay").HasDistance);
            Assert.AreEqual("30 mm (linea indicativa)", Field<VerifyOverlay>("_overlay").DistanceLabel);
        }

        [Test]
        public void TheLocalMeasureIsCalledPointToPoint()
        {
            Online();
            Do(InspectWorkspace.IdMeasure);
            Assert.That(AllHud(), Does.Contain("Punto-punto (locale)"));
            Assert.That(AllHud(), Does.Not.Contain("approssimata"));
        }
    }
}
