using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    /// <summary>
    /// M9-09 (§4): double Trigger on a face opens «Feature: nome» in Progettazione and Lamiera. Fake backend, no Inventor, no Quest.
    /// Input is synthetic where a gesture is simulated. NOT COVERED here: the geometry handle (distance/flange) for the feature edit.
    /// </summary>
    public class FeatureEditTests
    {
        private static JObject Param(string name, double value, string unit, string expression, bool editable) => new JObject
        { ["name"] = name, ["role"] = "distance", ["value"] = value, ["unit"] = unit, ["expression"] = expression, ["editable"] = editable };

        private static FaceFeatureInfo Feature(bool suppressed = false, bool healthy = true, params JObject[] parameters) => FaceFeatureInfo.FromJson(new JObject
        {
            ["feature"] = new JObject { ["name"] = "Estrusione1", ["type"] = "extrude", ["suppressed"] = suppressed, ["healthy"] = healthy },
            ["parameters"] = new JArray(parameters.Length > 0 ? parameters : new[]
            {
                Param("d3", 20, "mm", "20 mm", true), Param("d4", 40, "mm", "Largo", false), Param("d5", 10, "mm", "d3 * 2", false)
            }),
            ["previous_feature"] = "Schizzo1"
        });

        private static McpToolException ToolError(string code) => new McpToolException("inventor_face_feature", code, code, null);

        private sealed class Backend : IDesignWorkspaceBackend, IFaceFeatureBackend
        {
            public DocumentState State = new DocumentState("doc_bolt", "r", "v");
            public GlbModel Model;
            public FaceFeatureInfo Info;
            public Exception FaceFeatureError, CommitError;
            public int FaceFeatureCalls, Previews, Commits;
            public string LastFaceId;
            public JArray LastOperations;

            public Task<FaceFeatureInfo> GetFaceFeatureAsync(DocumentState state, string faceId, CancellationToken ct)
            {
                FaceFeatureCalls++; LastFaceId = faceId;
                return FaceFeatureError != null ? Task.FromException<FaceFeatureInfo>(FaceFeatureError) : Task.FromResult(Info);
            }

            public Task<DesignContext> GetDesignContextAsync(DocumentState state, CancellationToken ct) => Task.FromResult(
                DesignContext.Parse(new JObject
                {
                    ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "part",
                    ["faces"] = JArray.Parse(@"[{""id"":""ent_face"",""point_mm"":[0,0,10],""normal"":[0,0,1]}]"),
                    ["parameters"] = JArray.Parse(@"[{""name"":""d3"",""value_mm"":20.0,""unit"":""mm""},{""name"":""Largo"",""value_mm"":40.0,""unit"":""mm""}]")
                }, state));

            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => Task.FromResult(State);

            public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
            {
                Previews++; LastOperations = (JArray)operations.DeepClone();
                return Task.FromResult(new DesignPreview("p" + Previews, state.DocumentId, state.Revision, DateTimeOffset.UtcNow.AddMinutes(1), Model));
            }

            public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct)
            {
                Commits++;
                if (CommitError != null) return Task.FromException<DocumentState>(CommitError);
                State = new DocumentState("doc_bolt", "r2", "v2");
                return Task.FromResult(State);
            }
        }

        private GameObject _root;
        private Material _material;
        private DesignWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private readonly List<string> _hud = new List<string>();
        private static readonly System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private XrInput _xr;
        private XrInputFrame _frame;
        private float _clock;
        private double _now = 10;

        [SetUp]
        public void Setup()
        {
            _hud.Clear();
            _root = new GameObject("Feature edit test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            var eye = Child("Eye").AddComponent<Camera>();
            var hand = Child("Hand"); var ray = hand.AddComponent<ControllerRay>();
            var line = hand.AddComponent<LineRenderer>(); line.sharedMaterial = _material; ray.Configure(hand.transform, line);
            _view = Child("Model").AddComponent<CadSceneView>(); _view.BodyMaterial = _material;
            var selection = _view.gameObject.AddComponent<SelectionVisuals>(); selection.Configure(_view, null, null);
            _backend = new Backend { Model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)), Info = Feature() };
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Bolt"",""definition_kind"":""part"",""definition_document_id"":""doc_bolt"",""children"":[]}}"));
            var scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { { "doc_bolt", _backend.Model } },
                new Dictionary<string, string> { { "doc_bolt", "a_test" } }, new List<string>());
            _view.Show(scene);
            _workspace = Child("Workspace").AddComponent<DesignWorkspace>();
            _workspace.HudMessage += _hud.Add;
            _workspace.Initialize(_view, selection, ray, eye.transform); _workspace.Bind(_backend);
            _workspace.SetScene(scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }

        [TearDown]
        public void Cleanup()
        {
            foreach (var name in new[] { "_chip", "_ring" })
            {
                var view = _workspace == null ? null : typeof(DesignWorkspace).GetField(name, Flags).GetValue(_workspace) as Component;
                if (view != null) Object.DestroyImmediate(view.gameObject);
            }
            Object.DestroyImmediate(_root); Object.DestroyImmediate(_material);
        }

        private XrAction Act(string id) => _workspace.Actions.Single(a => a.Id == id);
        private IEnumerable<XrAction> TabActions() => _workspace.Actions.Where(a => a.Tab == _workspace.FeatureTabId).ToArray();
        private string AllHud() => string.Join("\n", _hud);
        private void Commit(double value)
        {
            var entry = _workspace.ActiveEntry; Assert.NotNull(entry, "a numeric entry is waiting for the keypad"); Assert.True(entry.Editing);
            entry.CommitValue(value, out var reason); Assert.AreEqual("", reason);
        }
        private async Task Open(string face = "ent_face") => await _workspace.OpenFeatureForFace(face);
        private void EditChip(string id, double value) { var a = Act(id); Assert.True(a.Enabled, id + ": " + a.DisabledReason); Assert.True(a.TryInvoke(), id); Commit(value); }

        [Test] public async Task OpenFeatureForFace_CallsFaceFeatureOnceAndOpensTabWithOneChipPerParameter()
        {
            await Open();
            Assert.AreEqual(1, _backend.FaceFeatureCalls); Assert.AreEqual("ent_face", _backend.LastFaceId);
            Assert.AreEqual("feature_Estrusione1", _workspace.FeatureTabId);
            Assert.True(_workspace.TabState.FeatureEditOpen); Assert.AreEqual("Estrusione1", _workspace.TabState.FeatureName);
            Assert.True(_workspace.Tabs.Any(t => t.Id == "feature_Estrusione1" && t.Label == "Feature: Estrusione1"));
            var chips = TabActions().Where(a => a.Id.StartsWith("design.feature.p.")).ToArray();
            Assert.AreEqual(3, chips.Length, "one chip per FeatureParameter");
            Assert.True(TabActions().Count() <= ActionCatalog.MaxPalette);
            StringAssert.Contains("Evidenziata la faccia scelta", AllHud(), "highlight is best effort: the HUD says only the picked face");
            Assert.True(TabActions().Any(a => a.Id == "design.feature.prev"), "previous_feature is shown");
            Assert.AreEqual(0, _backend.Commits); Assert.AreEqual(0, _backend.Previews, "opening reads only");
        }

        [Test] public async Task ExpressionDrivenChipIsReadOnlyAndNeverWritesANumberOverTheExpression()
        {
            await Open();
            var chip = Act("design.feature.p.1");
            StringAssert.Contains("Largo", chip.Label); Assert.False(chip.Enabled); StringAssert.Contains("espressione", chip.DisabledReason);
            Assert.False(chip.TryInvoke());
            Assert.False(Act("design.feature.p.2").Enabled);
            // The plain reference gets its own action; a computed expression does not.
            Assert.True(TabActions().Any(a => a.Label == "Modifica Largo")); Assert.False(TabActions().Any(a => a.Label.StartsWith("Modifica d3")));
            EditChip("design.feature.p.0", 25);
            Assert.AreEqual(1, _backend.LastOperations.Count); Assert.AreEqual("d3", (string)_backend.LastOperations[0]["arguments"]["name"]);
            Assert.False(_backend.LastOperations.Any(o => (string)o["arguments"]["name"] == "d4" || (string)o["arguments"]["name"] == "d5"));
        }

        [Test] public async Task EditSourceWritesTheSourceParameterNotTheExpression()
        {
            await Open();
            var source = TabActions().Single(a => a.Label == "Modifica Largo");
            Assert.True(source.Enabled); Assert.True(source.TryInvoke()); Commit(55);
            Assert.AreEqual(1, _backend.LastOperations.Count);
            Assert.AreEqual("Largo", (string)_backend.LastOperations[0]["arguments"]["name"]);
            StringAssert.StartsWith("55", (string)_backend.LastOperations[0]["arguments"]["value"]);
        }

        [TestCase(true, true)][TestCase(false, false)]
        public async Task SuppressedOrUnhealthyFeatureIsReadableButEditIsBlockedWithReason(bool suppressed, bool healthy)
        {
            _backend.Info = Feature(suppressed, healthy);
            await Open();
            Assert.NotNull(_workspace.FeatureEdit);
            foreach (var chip in TabActions().Where(a => a.Id.StartsWith("design.feature.p.")))
            { Assert.False(chip.Enabled, chip.Label); StringAssert.Contains(suppressed ? "soppressa" : "errore", chip.DisabledReason); }
            Assert.False(TabActions().Any(a => a.Label.StartsWith("Modifica ") && a.Label != "Modifica dal desktop"), "no source edit while blocked");
            StringAssert.Contains("Lettura consentita", AllHud());
            Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task ApplySendsOneBatchWithNSetParameterThroughTheCommitBar()
        {
            _backend.Info = Feature(false, true, Param("d1", 5, "mm", "5 mm", true), Param("d2", 8, "mm", "8 mm", true), Param("d9", 3, "mm", "3 mm", true));
            await Open();
            EditChip("design.feature.p.0", 6); EditChip("design.feature.p.1", 9);
            Assert.AreEqual(0, _backend.Commits, "chips only change the draft");
            Assert.AreEqual(2, _backend.LastOperations.Count);
            Assert.True(_backend.LastOperations.All(o => (string)o["command"] == "set_parameter"));
            CollectionAssert.AreEqual(new[] { "d1", "d2" }, _backend.LastOperations.Select(o => (string)o["arguments"]["name"]).ToArray());
            var apply = Act(CommitIds.Apply);
            Assert.True(apply.Enabled, apply.DisabledReason);
            Assert.True(apply.TryInvoke());
            Assert.AreEqual(1, _backend.Commits, "one commit = one atomic batch");
            Assert.Null(_workspace.FeatureEdit, "the document moved on: the feature edit is closed");
        }

        [Test] public async Task StaleRevisionOnApplyInvalidatesTheDraftLikeToday()
        {
            await Open();
            EditChip("design.feature.p.0", 25);
            _backend.CommitError = ToolError("STALE_REVISION");
            Assert.True(Act(CommitIds.Apply).TryInvoke());
            Assert.AreEqual(1, _backend.Commits);
            Assert.Null(_workspace.FeatureEdit); Assert.False(_workspace.TabState.FeatureEditOpen);
            Assert.False(Act(CommitIds.Apply).Enabled, "no stale draft can be applied again");
        }

        [Test] public async Task StaleRevisionOnReadDropsTheOpenAttemptAndReloadsReferences()
        {
            _backend.FaceFeatureError = ToolError("STALE_REVISION");
            await Open();
            Assert.Null(_workspace.FeatureEdit); StringAssert.Contains("cambiato dal desktop", AllHud());
        }

        [Test] public async Task FaceWithoutOwningFeatureSaysSo()
        {
            _backend.FaceFeatureError = ToolError(FaceFeatureInfo.NoOwningFeatureCode);
            await Open();
            Assert.Null(_workspace.FeatureEdit); StringAssert.Contains("non appartiene a una feature", AllHud());
        }

        [Test] public async Task UnsupportedFeatureShowsNameTypeDesktopAndParametriActions()
        {
            _backend.Info = FaceFeatureInfo.FromUnsupported(new JObject { ["feature"] = new JObject { ["name"] = "Loft1", ["type"] = "loft" } });
            await Open();
            Assert.AreEqual("feature_Loft1", _workspace.FeatureTabId);
            Assert.AreEqual(0, TabActions().Count(a => a.Id.StartsWith("design.feature.p.")), "no chips for an unsupported type");
            StringAssert.Contains("Loft1", AllHud()); StringAssert.Contains("loft", AllHud());
            var desktop = Act("design.feature.desktop"); Assert.True(desktop.Enabled);
            Assert.True(desktop.TryInvoke()); StringAssert.Contains("dal desktop", AllHud());
            Assert.True(Act("design.feature.parameters").Enabled, "the Parametri tab stays reachable");
            Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task OpenIsRefusedWhileADraftIsInProgress()
        {
            await Open();
            EditChip("design.feature.p.0", 25);
            int calls = _backend.FaceFeatureCalls;
            await Open();
            Assert.AreEqual(calls, _backend.FaceFeatureCalls); StringAssert.Contains("Chiudi o annulla", AllHud());
        }

        // ---------------------------------------------------------------- gesture (synthetic input)

        private void UseInput()
        {
            _xr = Child("XrInput").AddComponent<XrInput>(); _xr.Source = new SyntheticInputSource();
            Assert.True(_xr.Synthetic, "the runner log must call this input synthetic");
            _workspace.Attach(null, null, null, _xr);
            _frame = new XrInputFrame { PenTracked = true, PaletteTracked = true };
            Poll();
        }
        private void Poll() => _xr.Poll(_frame, _clock += 0.016f);
        private void Press() { _frame.PenTrigger = true; Poll(); _frame.PenTrigger = false; Poll(); }

        private void AimAtBodyAndKnowTheFace()
        {
            var body = _view.GetComponentsInChildren<CadBody>().First();
            Physics.SyncTransforms();
            var center = body.GetComponent<Collider>().bounds.center;
            var hand = _root.transform.Find("Hand");
            hand.SetPositionAndRotation(center + Vector3.back * 0.5f, Quaternion.LookRotation(Vector3.forward));
            Physics.SyncTransforms();
            Assert.True(CadRaycaster.TryPick(new Ray(hand.position, hand.forward), 20, out var hit, out int triangle, out _));
            var range = hit.Primitive.FaceMap.FaceAtTriangle(triangle);
            var state = new DocumentState("doc_bolt", "r", "v");
            typeof(DesignWorkspace).GetField("_context", Flags).SetValue(_workspace, DesignContext.Parse(new JObject
            {
                ["document_id"] = "doc_bolt", ["revision"] = "r", ["kind"] = "part",
                ["faces"] = new JArray(new JObject { ["id"] = "ent_hit", ["body_index"] = hit.Primitive.BodyIndex, ["face_ordinal"] = range.Ordinal,
                    ["point_mm"] = new JArray(0, 0, 10), ["normal"] = new JArray(0, 0, 1) })
            }, state));
        }

        [Test] public void DoubleTriggerOnAFaceOpensTheFeatureAndTheFirstPressOnlySelects()
        {
            _workspace.DoubleTriggerClock = () => _now;
            UseInput(); AimAtBodyAndKnowTheFace();
            Press();
            Assert.AreEqual(0, _backend.FaceFeatureCalls, "the first press selects as today"); Assert.Null(_workspace.FeatureEdit);
            _now += 0.2; Press();
            Assert.AreEqual(1, _backend.FaceFeatureCalls); Assert.AreEqual("ent_hit", _backend.LastFaceId);
            Assert.NotNull(_workspace.FeatureEdit); Assert.AreEqual("feature_Estrusione1", _workspace.FeatureTabId);
            _now += 0.1; Press();
            Assert.AreEqual(1, _backend.FaceFeatureCalls, "the third press is a first press again");
        }

        [Test] public void SlowSecondPressDoesNotOpenTheFeature()
        {
            _workspace.DoubleTriggerClock = () => _now;
            UseInput(); AimAtBodyAndKnowTheFace();
            Press(); _now += 0.6; Press();
            Assert.AreEqual(0, _backend.FaceFeatureCalls); Assert.Null(_workspace.FeatureEdit);
        }
    }

    // ------------------------------------------------------------------- Lamiera

    public class FeatureEditLamieraTests
    {
        private sealed class Backend : IDesignWorkspaceBackend, ISheetMetalBackend, IFaceFeatureBackend
        {
            public DocumentState State = new DocumentState("doc_bolt", "r", "v");
            public GlbModel Model;
            public FaceFeatureInfo Info;
            public int FaceFeatureCalls, Previews, Commits;
            public JArray LastOperations;

            public Task<FaceFeatureInfo> GetFaceFeatureAsync(DocumentState state, string faceId, CancellationToken ct) { FaceFeatureCalls++; return Task.FromResult(Info); }

            public Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct) => Task.FromResult(SheetMetalContext.Parse(JObject.Parse(
                @"{""is_sheet_metal"":true,""rule"":""Default_mm"",""thickness_mm"":2.0,""available_rules"":[{""rule"":""Default_mm"",""active"":true}],
                   ""body_count"":1,""bend_count"":2,""flat_pattern"":{""exists"":false}}"), state, state));
            public Task<FlatPatternMesh> GetFlatPatternMeshAsync(DocumentState state, CancellationToken ct) => throw new NotImplementedException();

            public Task<DesignContext> GetDesignContextAsync(DocumentState state, CancellationToken ct) => Task.FromResult(DesignContext.Parse(new JObject
            {
                ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "part",
                ["faces"] = JArray.Parse(@"[{""id"":""ent_top"",""point_mm"":[0,0,2],""normal"":[0,0,1]}]"),
                ["parameters"] = JArray.Parse(@"[{""name"":""Spessore"",""value_mm"":2.0,""unit"":""mm""}]")
            }, state));

            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => Task.FromResult(State);
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
            {
                Previews++; LastOperations = (JArray)operations.DeepClone();
                return Task.FromResult(new DesignPreview("p" + Previews, state.DocumentId, state.Revision, DateTimeOffset.UtcNow.AddMinutes(1), Model));
            }
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct)
            { Commits++; State = new DocumentState("doc_bolt", "r2", "v2"); return Task.FromResult(State); }
        }

        private GameObject _root;
        private Material _material;
        private LamieraWorkspace _workspace;
        private Backend _backend;
        private readonly List<string> _hud = new List<string>();
        private static readonly System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        [SetUp]
        public void Setup()
        {
            _hud.Clear();
            _root = new GameObject("Lamiera feature edit test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            var eye = Child("Eye").AddComponent<Camera>();
            var hand = Child("Hand"); var ray = hand.AddComponent<ControllerRay>();
            var line = hand.AddComponent<LineRenderer>(); line.sharedMaterial = _material; ray.Configure(hand.transform, line);
            var view = Child("Model").AddComponent<CadSceneView>(); view.BodyMaterial = _material;
            var visuals = view.gameObject.AddComponent<SelectionVisuals>(); visuals.Configure(view, null, null);
            _backend = new Backend
            {
                Model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)),
                Info = FaceFeatureInfo.FromJson(new JObject
                {
                    ["feature"] = new JObject { ["name"] = "Flangia1", ["type"] = "flange", ["suppressed"] = false, ["healthy"] = true },
                    ["parameters"] = new JArray(
                        new JObject { ["name"] = "d7", ["role"] = "distance", ["value"] = 15.0, ["unit"] = "mm", ["expression"] = "15 mm", ["editable"] = true },
                        new JObject { ["name"] = "a2", ["role"] = "angle", ["value"] = 90.0, ["unit"] = "deg", ["expression"] = "Spessore * 45", ["editable"] = false }),
                    ["previous_feature"] = "Base1"
                })
            };
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Bolt"",""definition_kind"":""part"",""definition_document_id"":""doc_bolt"",""children"":[]}}"));
            var scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { { "doc_bolt", _backend.Model } },
                new Dictionary<string, string> { { "doc_bolt", "a_test" } }, new List<string>());
            view.Show(scene);
            _workspace = Child("Workspace").AddComponent<LamieraWorkspace>();
            _workspace.HudMessage += _hud.Add;
            _workspace.Initialize(view, visuals, ray, eye.transform);
            _workspace.Bind(_backend, _backend);
            _workspace.SetScene(scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }

        [TearDown]
        public void Cleanup()
        {
            foreach (var name in new[] { "_heightChip", "_angleChip", "_thicknessChip", "_ring" })
            {
                var view = _workspace == null ? null : typeof(LamieraWorkspace).GetField(name, Flags).GetValue(_workspace) as Component;
                if (view != null) Object.DestroyImmediate(view.gameObject);
            }
            Object.DestroyImmediate(_root); Object.DestroyImmediate(_material);
        }

        private XrAction Act(string id) => _workspace.Actions.Single(a => a.Id == id);
        private IEnumerable<XrAction> TabActions() => _workspace.Actions.Where(a => a.Tab == _workspace.FeatureTabId).ToArray();
        private void Commit(double value)
        {
            var entry = _workspace.ActiveEntry; Assert.NotNull(entry); Assert.True(entry.Editing);
            entry.CommitValue(value, out var reason); Assert.AreEqual("", reason);
        }

        [Test] public async Task OpensTheFeatureTabWithChipsAndReadOnlyExpressions()
        {
            await _workspace.OpenFeatureForFace("ent_top");
            Assert.AreEqual(1, _backend.FaceFeatureCalls);
            Assert.AreEqual("feature_Flangia1", _workspace.FeatureTabId); Assert.True(_workspace.TabState.FeatureEditOpen);
            Assert.True(_workspace.Tabs.Any(t => t.Id == "feature_Flangia1"));
            Assert.AreEqual(2, TabActions().Count(a => a.Id.StartsWith("lamiera.feature.p.")));
            var expression = Act("lamiera.feature.p.1"); Assert.False(expression.Enabled); StringAssert.Contains("espressione", expression.DisabledReason);
            Assert.True(TabActions().Count() <= ActionCatalog.MaxPalette);
        }

        [Test] public async Task ApplySendsOneBatchOfSetParameterThroughTheLamieraCommitBar()
        {
            await _workspace.OpenFeatureForFace("ent_top");
            Assert.True(Act("lamiera.feature.p.0").TryInvoke()); Commit(18);
            Assert.AreEqual(1, _backend.LastOperations.Count);
            Assert.AreEqual("set_parameter", (string)_backend.LastOperations[0]["command"]);
            Assert.AreEqual("d7", (string)_backend.LastOperations[0]["arguments"]["name"]);
            Assert.AreEqual(0, _backend.Commits);
            var apply = Act(CommitIds.Apply); Assert.True(apply.Enabled, apply.DisabledReason); Assert.True(apply.TryInvoke());
            Assert.AreEqual(1, _backend.Commits); Assert.Null(_workspace.FeatureEdit);
        }

        [Test] public async Task SuppressedFeatureBlocksEditInLamiera()
        {
            _backend.Info = FaceFeatureInfo.FromJson(new JObject
            {
                ["feature"] = new JObject { ["name"] = "Flangia1", ["type"] = "flange", ["suppressed"] = true, ["healthy"] = true },
                ["parameters"] = new JArray(new JObject { ["name"] = "d7", ["value"] = 15.0, ["unit"] = "mm", ["expression"] = "15 mm", ["editable"] = true })
            });
            await _workspace.OpenFeatureForFace("ent_top");
            var chip = Act("lamiera.feature.p.0"); Assert.False(chip.Enabled); StringAssert.Contains("soppressa", chip.DisabledReason);
            Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task CancelClosesTheFeatureTab()
        {
            await _workspace.OpenFeatureForFace("ent_top");
            Assert.True(Act("lamiera.feature.p.0").TryInvoke()); Commit(18);
            Assert.True(Act(CommitIds.Cancel).TryInvoke());
            Assert.Null(_workspace.FeatureEdit);
        }
    }

    // ------------------------------------------------------------------- Assieme

    public sealed partial class AssemblyWorkspaceTests
    {
        [Test] public async Task DoubleTriggerOnAFaceInAssemblyOpensTheComponentAndNeverCallsFaceFeature()
        {
            double now = 10; _workspace.DoubleTriggerClock = () => now;
            var entries = WatchEntries();
            UseInput(); await Select(); AimAtSelectedBody();
            PressTrigger(); ReleaseTrigger();
            now += 0.2; PressTrigger(); ReleaseTrigger();
            Assert.AreEqual(1, entries.Count, "the component is opened (M9 §1)");
            Assert.AreEqual(0, _backend.FaceFeatureCalls, "Assieme never reads a face feature");
        }
    }
}
