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
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public sealed class AssemblyWorkspaceTests
    {
        private GameObject _root;
        private Material _material;
        private AssemblyWorkspace _workspace;
        private CadSceneView _view;
        private Backend _backend;
        private LoadedScene _scene;
        private sealed class Backend : IAssemblyWorkspaceBackend
        {
            public DocumentState State = new DocumentState("doc_bolt", "r", "v");
            public GlbModel Model;
            public int Previews, Commits;
            public JArray LastOperations;
            public bool Grounded, DofComplete = true;
            public TaskCompletionSource<DocumentState> Mutation;
            public TaskCompletionSource<DesignPreview> Preview;
            public AssemblyContext Context(DocumentState state, string selected)
            {
                JObject Occ(string id) => new JObject { ["occurrence_id"] = id, ["name"] = id, ["editable"] = true, ["grounded"] = Grounded,
                    ["suppressed"] = false, ["adaptive"] = false, ["dof_translation"] = 1, ["dof_rotation"] = 1, ["dof_complete"] = DofComplete,
                    ["translation_axes"] = new JArray { new JArray(1, 0, 0) }, ["rotation_axes"] = new JArray { new JArray(0, 0, 1) }, ["rotation_center_mm"] = new JArray(10, 20, 30) };
                var occurrences = selected == null ? new JArray(Occ("ent_a"), Occ("ent_b")) : new JArray(Occ(selected));
                var refs = selected == null ? new JArray() : new JArray(new JObject { ["id"] = selected + "_face", ["occurrence_id"] = selected,
                    ["name"] = "Faccia " + selected, ["kind"] = "face", ["geometry"] = "kPlaneSurface", ["point_mm"] = new JArray(10, 20, 30), ["axis"] = new JArray(0, 0, 1) });
                return AssemblyContext.Parse(new JObject { ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["kind"] = "assembly",
                    ["truncated"] = false, ["occurrences"] = occurrences, ["references"] = refs }, state);
            }
            public Task<AssemblyContext> GetAssemblyContextAsync(DocumentState state, string selected, CancellationToken ct) => Task.FromResult(Context(state, selected));
            public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
            { Previews++; LastOperations = (JArray)operations.DeepClone(); return Preview?.Task ?? Task.FromResult(new DesignPreview("p" + Previews, state.DocumentId, state.Revision, DateTimeOffset.UtcNow.AddMinutes(1), Model, isAssembly: true)); }
            public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct) { Commits++; State = new DocumentState(State.DocumentId, "r2", "v2"); return Mutation?.Task ?? Task.FromResult(State); }
            public Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) => Task.FromResult(State);
            public Task<DesignHistory> GetHistoryAsync(DocumentState state, CancellationToken ct) => Task.FromResult(new DesignHistory(state, false, false, null));
            public Task<DocumentState> ApplyHistoryAsync(DesignHistory history, bool redo, CancellationToken ct) => Task.FromResult(State);
        }
        [SetUp] public void Setup()
        {
            _root = new GameObject("Assembly test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            var eye = Child("eye").AddComponent<Camera>();
            var hand = Child("hand"); var ray = hand.AddComponent<ControllerRay>(); var line = hand.AddComponent<LineRenderer>();
            line.sharedMaterial = _material; ray.Configure(hand.transform, line);
            _view = Child("model").AddComponent<CadSceneView>(); _view.BodyMaterial = _material;
            _backend = new Backend { Model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture)) };
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""assembly"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Test"",""definition_kind"":""assembly"",""children"":[
                {""name"":""Bolt"",""occurrence_id"":""ent_a"",""definition_document_id"":""doc_bolt"",""definition_kind"":""part"",""children"":[]}]}}"));
            _scene = new LoadedScene(graph, new Dictionary<string, GlbModel> { ["doc_bolt"] = _backend.Model }, new Dictionary<string, string> { ["doc_bolt"] = "a_test" }, new List<string>());
            _view.Show(_scene); _workspace = Child("workspace").AddComponent<AssemblyWorkspace>(); _workspace.Initialize(_view, ray, eye.transform);
            _workspace.Bind(_backend); _workspace.SetScene(_scene); _workspace.SetOnline(true); _workspace.SetVisible(true); _workspace.Open();
        }
        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        private bool HasButton(string text) => _workspace.GetComponentsInChildren<Button>().Any(b => b.GetComponentInChildren<Text>().text == text);
        private Transform Hand => _root.transform.Find("hand");
        private void Frame(bool tracked, bool grip, bool trigger, bool gripDown = false, bool triggerDown = false, bool ui = false)
        { typeof(AssemblyWorkspace).GetMethod("ProcessControllerFrame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_workspace, new object[] { tracked, grip, trigger, gripDown, triggerDown, ui }); }
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
        [TearDown] public void TearDown() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); if (_material != null) UnityEngine.Object.DestroyImmediate(_material); }
        [Test] public async Task ApplyRequiresRenderedPreviewAndRefreshesAfterCommit()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove();
            Assert.False(HasButton("Applica"));
            await _workspace.PreviewAsync(); Assert.True(HasButton("Applica"));
            Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);
            await _workspace.ApplyAsync(); Assert.AreEqual(1, _backend.Commits);
            Assert.False(HasButton("Applica")); Assert.False(_workspace.RequiresCadReview);
        }
        [Test] public async Task RebuildingSameRevisionSceneRevokesPreviewUntilRenderedAgain()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); await _workspace.PreviewAsync();
            Assert.True(HasButton("Applica")); Assert.True(_view.GetComponent<DesignPreviewView>().IsShowing);

            _view.Show(_scene); _workspace.SetScene(_scene);

            Assert.False(HasButton("Applica"));
            await _workspace.ApplyAsync(); Assert.AreEqual(0, _backend.Commits);
            await _workspace.PreviewAsync(); Assert.True(HasButton("Applica"));
            await _workspace.ApplyAsync(); Assert.AreEqual(1, _backend.Commits);
        }

        [Test] public async Task ClearingRenderedPreviewRevokesApplyProof()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); await _workspace.PreviewAsync();
            var preview = _view.GetComponents<DesignPreviewView>().First(p => p.IsShowing);
            preview.Clear();

            await _workspace.ApplyAsync();

            Assert.AreEqual(0, _backend.Commits); Assert.False(HasButton("Applica"));
        }
        [Test] public async Task SecondReferenceAutomaticallyPreviewsSelectedConstraint()
        {
            _workspace.ChooseConstraint("mate");
            _workspace.ChooseReference(_backend.Context(_backend.State, "ent_a").References[0]); Assert.AreEqual(0, _backend.Previews);
            _workspace.ChooseReference(_backend.Context(_backend.State, "ent_b").References[0]);
            Assert.AreEqual(1, _backend.Previews); Assert.True(HasButton("Applica")); await Task.CompletedTask;
        }
        [Test] public void SelectingReferencesFirstOffersCompatibleConstraintsWithoutAutomaticPreview()
        {
            _workspace.ChooseReference(_backend.Context(_backend.State, "ent_a").References[0]);
            _workspace.ChooseReference(_backend.Context(_backend.State, "ent_b").References[0]);
            Assert.AreEqual(0, _backend.Previews);
            Assert.True(HasButton("Accoppia")); Assert.True(HasButton("Allinea")); Assert.True(HasButton("Angolo"));
            Assert.False(HasButton("Inserisci")); Assert.False(HasButton("Applica"));
            _workspace.ChooseConstraint("flush");
            Assert.AreEqual(1, _backend.Previews); Assert.True(HasButton("Applica"));
        }
        [Test] public async Task SelectingReferencesAfterCadMoveDoesNotPreviewStaleMove()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove();
            await _workspace.PreviewAsync(); Assert.True(HasButton("Applica"));
            int previewsBefore = _backend.Previews;

            _workspace.ChooseReference(_backend.Context(_backend.State, "ent_a").References[0]);
            _workspace.ChooseReference(_backend.Context(_backend.State, "ent_b").References[0]);

            Assert.AreEqual(previewsBefore, _backend.Previews);
            Assert.False(HasButton("Applica"));
            Assert.True(HasButton("Accoppia"));
            Assert.True(HasButton("Allinea"));
            Assert.True(HasButton("Angolo"));
        }
        [Test] public async Task DisconnectInvalidatesPreviewAndLateResults()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove();
            _backend.Preview = new TaskCompletionSource<DesignPreview>(); var request = _workspace.PreviewAsync();
            _workspace.SetOnline(false);
            _backend.Preview.SetResult(new DesignPreview("late", "doc_bolt", "r", DateTimeOffset.UtcNow.AddMinutes(1), _backend.Model, isAssembly: true));
            await request; Assert.False(HasButton("Applica")); Assert.False(_view.GetComponent<DesignPreviewView>().IsShowing);
        }
        [Test] public async Task PendingAndUnknownMutationSurviveCloseAndRebind()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); await _workspace.PreviewAsync();
            _backend.Mutation = new TaskCompletionSource<DocumentState>(); var apply = _workspace.ApplyAsync();
            Assert.True(_workspace.RequiresCadReview); _workspace.Close(); _workspace.Bind(null); Assert.True(_workspace.RequiresCadReview);
            _backend.Mutation.SetException(new System.IO.IOException("connection lost")); await apply;
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
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove();
            var gizmo = _view.GetComponentsInChildren<LineRenderer>().First(l => l.name == "DOF traslazione 1");
            Assert.AreEqual(CadCoordinates.ToLocal(new CadPoint(10, 20, 30)), gizmo.GetPosition(0));
            _view.transform.localScale = Vector3.one * 0.1f;
            Assert.AreEqual(0, gizmo.GetComponents<Collider>().Length);
            Assert.That(Vector3.Distance(gizmo.transform.TransformPoint(gizmo.GetPosition(0)), gizmo.transform.TransformPoint(gizmo.GetPosition(1))), Is.EqualTo(0.01f).Within(0.00001f));
        }

        [Test] public async Task PlainGripMovesViewWithoutPreview()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); var before = _view.transform.position;
            Hand.SetPositionAndRotation(new Vector3(0, 0, -1), Quaternion.identity); Frame(true, true, false, gripDown: true);
            Hand.position += Vector3.right * 0.25f; Frame(true, true, false);
            Assert.That(Vector3.Distance(_view.transform.position, before), Is.EqualTo(0.25f).Within(0.001f)); Assert.AreEqual(0, _backend.Previews);
        }

        [TestCase(1f)] [TestCase(0.1f)] public async Task CombinedGestureMovesTenMillimetresAndReleasePreviewsOnce(float scale)
        {
            await _workspace.SelectOccurrenceAsync("ent_a");
            _view.transform.SetPositionAndRotation(new Vector3(0.3f, -0.2f, 0.4f), Quaternion.Euler(15f, 25f, -10f));
            _workspace.BeginMove(); AimAtHandle(scale);
            Frame(true, true, true, triggerDown: true); Hand.position += _view.transform.TransformVector(new Vector3(-0.01f, 0, 0));
            Frame(true, true, true); Frame(true, false, false);
            Assert.AreEqual(1, _backend.Previews);
            var op = (JObject)_backend.LastOperations[0]; Assert.AreEqual("assembly_move", (string)op["command"]);
            var delta = (JArray)op["arguments"]["translation_mm"];
            Assert.That((double)delta[0], Is.EqualTo(10).Within(0.05));
            Assert.That((double)delta[1], Is.EqualTo(0).Within(0.05)); Assert.That((double)delta[2], Is.EqualTo(0).Within(0.05));
        }
        [TestCase(1f)] [TestCase(0.1f)] public async Task SelectedBodyStartsIntentionalMoveAtBothScales(float scale)
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); AimAtSelectedBody(scale);
            Frame(true, true, true, triggerDown: true);
            Hand.position += _view.transform.TransformVector(new Vector3(-0.01f, 0, 0));
            Frame(true, true, true); Frame(true, false, false);
            Assert.AreEqual(1, _backend.Previews);
            var delta = (JArray)((JObject)_backend.LastOperations[0])["arguments"]["translation_mm"];
            Assert.That((double)delta[0], Is.EqualTo(10).Within(0.05));
        }
        [Test] public async Task BodyGestureBeforeCadMoveCannotStartPreview()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); AimAtSelectedBody();
            Frame(true, true, true, triggerDown: true); Hand.position += Vector3.left * 0.01f;
            Frame(true, true, true); Frame(true, false, false);
            Assert.AreEqual(0, _backend.Previews);
        }

        [TestCase(1f)] [TestCase(0.1f)] public async Task CombinedGestureRotatesThirtyCadDegrees(float scale)
        {
            await _workspace.SelectOccurrenceAsync("ent_a");
            _view.transform.SetPositionAndRotation(new Vector3(-0.25f, 0.18f, 0.3f), Quaternion.Euler(-12f, 18f, 9f));
            _view.transform.localScale = Vector3.one * scale;
            _workspace.BeginMove();
            typeof(AssemblyWorkspace).GetField("_rotating", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_workspace, true);
            typeof(AssemblyWorkspace).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_workspace, null);
            AimAtRotationHandle(scale);
            Frame(true, true, true, triggerDown: true);
            var axis = _view.transform.TransformDirection(CadCoordinates.ToLocal(new CadPoint(0, 0, 1))).normalized;
            Hand.rotation = Quaternion.AngleAxis(-30f, axis) * Hand.rotation;
            Frame(true, true, true); Frame(true, false, false);
            Assert.AreEqual(1, _backend.Previews);
            var op = (JObject)_backend.LastOperations[0]; var args = (JObject)op["arguments"];
            Assert.That((double)args["rotation_degrees"], Is.EqualTo(30).Within(0.1));
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

        [Test] public async Task TrackingLossCancelsArmedGestureWithoutPreview()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); AimAtHandle();
            Frame(true, true, true, triggerDown: true); Frame(false, false, false); Frame(true, false, false);
            Assert.AreEqual(0, _backend.Previews);
        }

        [Test] public async Task UiHitBlocksCombinedGesture()
        {
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); AimAtHandle();
            Frame(true, true, true, triggerDown: true, ui: true); Hand.position += Vector3.left * 0.1f;
            Frame(true, true, true, ui: true); Frame(true, false, false); Assert.AreEqual(0, _backend.Previews);
        }

        [TestCase(true, true)] [TestCase(false, false)] public async Task GroundedOrIncompleteDofCannotBegin(bool grounded, bool complete)
        {
            _backend.Grounded = grounded; _backend.DofComplete = complete;
            await _workspace.SelectOccurrenceAsync("ent_a"); _workspace.BeginMove(); AimAtHandle();
            Frame(true, true, true, triggerDown: true); Frame(true, false, false);
            Assert.AreEqual(0, _backend.Previews); Assert.False(HasButton("Applica"));
        }
    }
}
