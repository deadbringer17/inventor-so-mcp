using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

namespace InventorXrSo.Tests
{
    /// <summary>
    /// Ispeziona on the M6 shell: no panel, wrist menu, breadcrumb or compact card. Every command goes through the declared
    /// actions by stable id (palette, ring and voice share them); context and properties land on the HUD; grip is view only.
    /// </summary>
    public class InspectWorkspaceTests
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
            _workspace.OtherWorkspaceActive = () => _other;
            _workspace.HudMessage += _hud.Add;
            _workspace.Initialize(_view, visuals, _ray, eye.transform, _env);
            _catalog = new ActionCatalog(new SpacesActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => true, () => true, () => true, () => true));
            _shell = UiShell.Create(left.transform, eye.transform, _catalog);
            _roots.Add(_shell.gameObject); _roots.Add(_shell.CommitBar.Canvas.gameObject); _roots.Add(_shell.Hud.Canvas.gameObject);
            _xr = Child("XrInput").AddComponent<XrInput>(); _xr.Source = new SyntheticInputSource();
            Assert.True(_xr.Synthetic, "the runner log must call this input synthetic");
            _frame = new XrInputFrame { PenTracked = true, PaletteTracked = true };
            _workspace.Attach(_shell, _xr);
            _catalog.SetActive(_workspace);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            _workspace.SetVisible(true);
            Poll();
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }

        [TearDown]
        public void TearDown()
        {
            foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r);
            _roots.Clear();
            Object.DestroyImmediate(_root);
        }

        private void Poll() => _xr.Poll(_frame, _clock += 0.016f);
        private XrAction Act(string id) => _workspace.Actions.Single(a => a.Id == id);
        private bool Enabled(string id) => _workspace.Actions.Any(a => a.Id == id && a.Enabled);
        private void Do(string id) { var a = Act(id); Assert.True(a.Enabled, id + ": " + a.DisabledReason); Assert.True(a.TryInvoke(), id); }
        private string AllHud() => string.Join("\n", _hud);
        private MeasurementView Measure => _view.GetComponentInChildren<MeasurementView>();
        private SectionPlane Section => _view.GetComponentInChildren<SectionPlane>();
        private void Pick(Vector3 point) => typeof(InspectWorkspace).GetMethod("OnPointPicked", Flags).Invoke(_workspace, new object[] { point });
        private void Update() => typeof(InspectWorkspace).GetMethod("Update", Flags).Invoke(_workspace, null);

        private void Commit(double value)
        {
            var entry = _workspace.ActiveEntry; Assert.NotNull(entry, "a numeric entry is waiting for the keypad"); Assert.True(entry.Editing);
            Assert.True(entry.CommitValue(value, out var reason), reason);
        }

        private sealed class Backend : IInspectionBackend
        {
            public int Documents = 3;
            public readonly List<string> Activated = new List<string>();
            public InspectionInfo Info = InspectionInfo.FromJson(new JObject { ["material"] = "Acciaio", ["mass_kg"] = 1.5, ["volume_mm3"] = 120000 });
            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) => Task.FromResult(Info);
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenDocument>>(
                Enumerable.Range(1, Documents).Select(i => new OpenDocument("doc" + i, "Documento " + i, "kPartDocumentObject")).ToArray());
            public Task ActivateOpenAsync(string documentId, CancellationToken ct) { Activated.Add(documentId); return Task.CompletedTask; }
        }

        private sealed class DeferredInspection : IInspectionBackend
        {
            public readonly TaskCompletionSource<InspectionInfo> Result = new TaskCompletionSource<InspectionInfo>();
            public DocumentState LastState;
            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) { LastState = state; return Result.Task; }
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenDocument>>(Array.Empty<OpenDocument>());
            public Task ActivateOpenAsync(string documentId, CancellationToken ct) => Task.CompletedTask;
        }

        private Backend GoOnline(Backend backend = null)
        {
            backend = backend ?? new Backend();
            _workspace.Bind(backend, null); _workspace.SetScene(CadSceneViewTests.BoltScene()); _workspace.SetOnline(true);
            return backend;
        }

        // ---------------------------------------------------------------- catalog contract

        [Test]
        public void TabsAreMeasureSectionViewVisibilityVerifyWithAtMostEightActionsEachAndUniqueIds()
        {
            CollectionAssert.AreEqual(new[] { "misura", "sezione", "vista", "visibilita", "verifica" }, _workspace.Tabs.Select(t => t.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "Misura", "Sezione", "Vista", "Visibilità", "Verifica", "Spazi" }, _catalog.Tabs.Select(t => t.Label).ToArray());
            foreach (var tab in _workspace.Tabs)
            {
                var list = _catalog.Palette(tab.Id);
                Assert.That(list.Count, Is.InRange(1, 8), tab.Id);
            }
            var ids = _workspace.Actions.Select(a => a.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.True(ids.All(id => id.StartsWith("inspect.", StringComparison.Ordinal)), "stable ids inspect.*");
            Assert.IsNull(_workspace.CommitBar, "Ispeziona never changes the CAD: no commit bar");
        }

        [Test]
        public void InspectIsTheDefaultWorkspaceAndStopsWhileAnAuthoringWorkspaceIsOpen()
        {
            Assert.AreSame(_workspace, _catalog.Active, "the palette is never empty");
            Assert.True(_workspace.Active); Assert.True(Enabled(InspectWorkspace.IdMeasure));
            Do(InspectWorkspace.IdMeasure); Do(InspectWorkspace.IdSection);
            Assert.True(_workspace.Measuring); Assert.True(Section.Active);
            _other = true; _workspace.OthersChanged();
            Assert.False(_workspace.Active);
            Assert.False(_workspace.Measuring); Assert.False(Section.Active, "local tools stop while another workspace is open");
            Assert.False(Enabled(InspectWorkspace.IdMeasure));
            Assert.False(string.IsNullOrEmpty(Act(InspectWorkspace.IdMeasure).DisabledReason));
            _other = false; _workspace.OthersChanged();
            Assert.True(Enabled(InspectWorkspace.IdMeasure));
        }

        [Test]
        public void LocalToolsStayAvailableOfflineAndBackendOnesAreDisabledWithAReason()
        {
            GoOnline();
            foreach (var id in new[] { InspectWorkspace.IdBrowse, InspectWorkspace.IdProperties, InspectWorkspace.IdDocuments }) Assert.True(Enabled(id), id);
            _workspace.SetOnline(false);
            foreach (var id in new[] { InspectWorkspace.IdMeasure, InspectWorkspace.IdSection, InspectWorkspace.IdScale, InspectWorkspace.IdEnvironment,
                InspectWorkspace.IdSectionOffset, InspectWorkspace.IdSectionAngle, InspectWorkspace.IdSectionReset }) Assert.True(Enabled(id), id + " works offline");
            foreach (var id in new[] { InspectWorkspace.IdBrowse, InspectWorkspace.IdProperties, InspectWorkspace.IdDocuments })
            {
                Assert.False(Enabled(id), id); Assert.False(string.IsNullOrEmpty(Act(id).DisabledReason), id);
                Assert.False(Act(id).TryInvoke());
            }
            Do(InspectWorkspace.IdMeasure); Assert.True(_workspace.Measuring);
        }

        [Test]
        public void RingOnAComponentOffersPropertiesContextAndMeasureWithinTheLimit()
        {
            var ring = _catalog.Context(UiSelectionKind.Component);
            CollectionAssert.AreEqual(new[] { InspectWorkspace.IdProperties, InspectWorkspace.IdEnter, InspectWorkspace.IdMeasure }, ring.Select(a => a.Id).ToArray());
            Assert.LessOrEqual(ring.Count, ActionCatalog.MaxContext);
            Assert.IsEmpty(_catalog.Context(UiSelectionKind.Edge));
        }

        [Test]
        public void NothingInInspectReadsOVRInputDirectly()
        {
            foreach (var file in new[] { "InspectWorkspace.cs", "InspectActions.cs", "AssemblyWorkspace.cs", "AssemblyActions.cs" })
            {
                var text = File.ReadAllText(Path.Combine(Application.dataPath, "XrSo/Xr/" + file));
                StringAssert.DoesNotContain("OVRInput", text, file + ": controllers come only from XrInput");
            }
        }

        // ---------------------------------------------------------------- measure and section

        [Test]
        public void MeasureFlowRunsThroughTheActionsAndPinsOnTheModel()
        {
            Assert.False(Enabled(InspectWorkspace.IdMeasurePin)); Assert.False(Enabled(InspectWorkspace.IdMeasureCancel));
            Do(InspectWorkspace.IdMeasure);
            Assert.True(_workspace.Measuring); Assert.True(Enabled(InspectWorkspace.IdMeasureCancel));
            Pick(Vector3.zero); Assert.True(_workspace.Measuring);
            Pick(Vector3.right); Assert.False(_workspace.Measuring);
            Assert.NotNull(Measure.DistanceMm); StringAssert.Contains("Punto-punto (locale)", AllHud());
            Do(InspectWorkspace.IdMeasurePin);
            Assert.AreEqual(1, Measure.PinnedCount); Assert.True(Enabled(InspectWorkspace.IdMeasureClear));
            Do(InspectWorkspace.IdMeasure); Do(InspectWorkspace.IdMeasureCancel);
            Assert.False(_workspace.Measuring); Assert.AreEqual(1, Measure.PinnedCount);
            Do(InspectWorkspace.IdMeasureClear); Assert.AreEqual(0, Measure.PinnedCount);
        }

        [Test]
        public void SectionToggleAndNumericFieldsUseTheKeypadEntry()
        {
            Do(InspectWorkspace.IdSection);
            Assert.True(Section.Active); Assert.True(Act(InspectWorkspace.IdSection).IsOn);
            Do(InspectWorkspace.IdSectionOffset);
            Assert.True(_shell.Palette.KeypadVisible); Assert.True(_workspace.ActiveEntry.Editing);
            Assert.AreEqual(QuantityUnit.Millimeters, _workspace.ActiveEntry.Unit);
            Commit(25);
            Assert.AreEqual(25f, Section.OffsetMm, 0.01f); Assert.IsNull(_workspace.ActiveEntry); Assert.False(_shell.Palette.KeypadVisible);
            StringAssert.Contains("25", Act(InspectWorkspace.IdSectionOffset).Label);
            Do(InspectWorkspace.IdSectionAngle); Assert.AreEqual(QuantityUnit.Degrees, _workspace.ActiveEntry.Unit);
            Commit(30); Assert.AreEqual(30f, Section.AngleDegrees, 0.01f);
            Do(InspectWorkspace.IdSectionReset); Assert.AreEqual(0f, Section.OffsetMm, 0.01f); Assert.True(Section.Active);
            Do(InspectWorkspace.IdSection); Assert.False(Section.Active);
        }

        [Test]
        public void XClosesTheKeypadThenTheListThenTheMeasurementOnly()
        {
            Do(InspectWorkspace.IdSectionOffset); Assert.NotNull(_workspace.ActiveEntry);
            Assert.False(Enabled(InspectWorkspace.IdMeasure), "no measuring while the keypad is open");
            _workspace.Back(); Assert.IsNull(_workspace.ActiveEntry); Assert.False(_shell.Palette.KeypadVisible);
            Assert.AreEqual(0f, Section.OffsetMm, "closing the keypad applies nothing");
            Do(InspectWorkspace.IdScale); Assert.True(_shell.Palette.InTabGroup);
            _workspace.Back(); Assert.False(_shell.Palette.InTabGroup);
            Assert.True(_workspace.Actions.All(a => !a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal)));
            Do(InspectWorkspace.IdMeasure); _workspace.Back(); Assert.False(_workspace.Measuring);
        }

        [Test]
        public void SceneChangeClearsMeasurementsAndSectionButLocalToolsRemainOffline()
        {
            Do(InspectWorkspace.IdMeasure);
            Measure.Pick(Vector3.zero); Measure.Pick(Vector3.right); Assert.IsTrue(Measure.Pin());
            Do(InspectWorkspace.IdSection);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            Assert.AreEqual(0, Measure.PinnedCount); Assert.IsFalse(Section.Active);
            _workspace.SetOnline(false); Do(InspectWorkspace.IdMeasure);
            Assert.IsTrue(_workspace.Measuring);
        }

        // ---------------------------------------------------------------- scale and environment

        [Test]
        public void ScaleIsAPickerTabAndTheRoomExtentComesFromTheKeypad()
        {
            Do(InspectWorkspace.IdScale);
            Assert.True(_shell.Palette.InTabGroup);
            CollectionAssert.IsSubsetOf(new[] { InspectWorkspace.IdScaleOne, InspectWorkspace.IdScaleFit, InspectWorkspace.IdScaleTable,
                InspectWorkspace.IdScaleRoom, InspectWorkspace.IdScaleRecenter }, _workspace.Actions.Select(a => a.Id).ToArray());
            Do(InspectWorkspace.IdScaleTable);
            var extent = Mathf.Max(ScenePlacement.LocalBounds(_view.transform).size.x, ScenePlacement.LocalBounds(_view.transform).size.y,
                ScenePlacement.LocalBounds(_view.transform).size.z);
            Assert.LessOrEqual(extent * _view.transform.lossyScale.x, 0.6f + 1e-3f);
            Assert.False(_shell.Palette.InTabGroup, "choosing an entry closes the list");
            Do(InspectWorkspace.IdScale); Do(InspectWorkspace.IdScaleRoom);
            Assert.AreEqual(QuantityUnit.Meters, _workspace.ActiveEntry.Unit);
            Commit(0.2);
            Assert.AreEqual(0.2f, (float)typeof(InspectWorkspace).GetField("_roomExtent", Flags).GetValue(_workspace), 1e-4f);
            Do(InspectWorkspace.IdScale); Do(InspectWorkspace.IdScaleFit);
            Assert.LessOrEqual(extent * _view.transform.lossyScale.x, 0.2f + 1e-3f);
            Do(InspectWorkspace.IdScale); Do(InspectWorkspace.IdScaleOne);
            Assert.AreEqual(1f, _view.transform.lossyScale.x, 1e-4f);
        }

        [Test]
        public void EnvironmentActionSwitchesBetweenMixedRealityAndStudio()
        {
            Assert.AreEqual("Studio virtuale", Act(InspectWorkspace.IdEnvironment).Label);
            Do(InspectWorkspace.IdEnvironment);
            Assert.AreEqual(EnvironmentMode.StudioVr, _env.Mode);
            Assert.AreEqual("Realtà mista", Act(InspectWorkspace.IdEnvironment).Label);
            Do(InspectWorkspace.IdEnvironment);
            Assert.AreEqual(EnvironmentMode.MixedReality, _env.Mode);
        }

        // ---------------------------------------------------------------- backend reads and the HUD

        [Test]
        public async Task PropertiesGoToTheHudWithoutInventingValues()
        {
            GoOnline();
            Do(InspectWorkspace.IdProperties); await Task.Yield();
            var hud = AllHud();
            StringAssert.Contains("Materiale: Acciaio", hud); StringAssert.Contains("Volume: 120000", hud);
            StringAssert.Contains("Area: —", hud);
        }

        [Test]
        public async Task LatePropertiesFromAnOldSceneAreDiscarded()
        {
            var backend = new DeferredInspection();
            _workspace.Bind(backend, null); _workspace.SetScene(CadSceneViewTests.BoltScene()); _workspace.SetOnline(true);
            Do(InspectWorkspace.IdProperties);
            _workspace.SetScene(CadSceneViewTests.BoltScene());
            backend.Result.SetResult(InspectionInfo.FromJson(new JObject { ["material"] = "STALE STEEL", ["mass_kg"] = 900 }));
            await Task.Yield();
            Assert.IsFalse(AllHud().Contains("STALE STEEL"));
        }

        [Test]
        public void NonVisualRevisionUsesCurrentTokenAndInvalidatesPinnedMeasurements()
        {
            var backend = new DeferredInspection();
            var scene = CadSceneViewTests.BoltScene();
            _workspace.Bind(backend, null); _workspace.SetScene(scene); _workspace.SetOnline(true);
            Do(InspectWorkspace.IdMeasure); Measure.Pick(Vector3.zero); Measure.Pick(Vector3.right); Measure.Pin();
            _workspace.SetDocumentState(new DocumentState(scene.Graph.DocumentId, "new-property-revision", scene.Graph.VisualRevision));
            Do(InspectWorkspace.IdProperties);
            Assert.AreEqual("new-property-revision", backend.LastState.Revision);
            Assert.AreEqual(0, Measure.PinnedCount);
            backend.Result.SetCanceled();
        }

        [Test]
        public void BrowserListsTheChildrenAsPickerEntriesAndBackendEntriesFollowOnline()
        {
            GoOnline();
            Do(InspectWorkspace.IdBrowse);
            Assert.True(_shell.Palette.InTabGroup);
            var picks = _workspace.Actions.Where(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(new[] { "Bolt:1", "Bolt:2" }, picks.Select(a => a.Label).ToArray());
            Assert.True(picks.All(a => a.Enabled));
            _workspace.SetOnline(false);
            Assert.False(_workspace.Actions.Any(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal)), "the list closes when the link drops");
        }

        [Test]
        public void ContextEntryNeedsASelectionAndBackWorksFromTheRoot()
        {
            GoOnline();
            Assert.False(Enabled(InspectWorkspace.IdEnter)); Assert.False(Enabled(InspectWorkspace.IdBack));
            Assert.False(string.IsNullOrEmpty(Act(InspectWorkspace.IdEnter).DisabledReason));
        }

        [Test]
        public void LongDocumentListsBecomeSeveralPickerTabsOfAtMostEightEntries()
        {
            GoOnline(new Backend { Documents = 11 });
            Do(InspectWorkspace.IdDocuments);
            Assert.True(_shell.Palette.InTabGroup);
            var pickTabs = _workspace.Tabs.Where(t => t.Id.StartsWith(InspectWorkspace.PickTabPrefix, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(2, pickTabs.Count);
            Assert.True(pickTabs.All(t => t.Hidden));
            Assert.AreEqual(8, _catalog.Palette(pickTabs[0].Id).Count); Assert.AreEqual(3, _catalog.Palette(pickTabs[1].Id).Count);
            CollectionAssert.AreEqual(Enumerable.Range(1, 11).Select(i => "Documento " + i).ToArray(),
                _workspace.Actions.Where(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal)).Select(a => a.Label).ToArray());
        }

        [Test]
        public void ChoosingADocumentActivatesItWithoutTouchingTheCad()
        {
            var backend = GoOnline(new Backend { Documents = 2 });
            Do(InspectWorkspace.IdDocuments);
            var pick = _workspace.Actions.First(a => a.Label == "Documento 2");
            Assert.True(pick.TryInvoke());
            CollectionAssert.AreEqual(new[] { "doc2" }, backend.Activated);
            Assert.False(_shell.Palette.InTabGroup);
        }

        // ---------------------------------------------------------------- gestures (synthetic XrInput)

        [Test]
        public void GripMovesTheModelWithTheHandAndNeverTheCad()
        {
            Physics.SyncTransforms();
            var body = _view.GetComponentsInChildren<CadBody>().First();
            var center = body.GetComponent<Collider>().bounds.center;
            var hand = _ray.Origin;
            hand.SetPositionAndRotation(center + Vector3.back * 0.5f, Quaternion.LookRotation(Vector3.forward));
            Physics.SyncTransforms();
            Assert.True(CadRaycaster.TryPick(new Ray(hand.position, hand.forward), 20, out _, out _, out _));
            var before = _view.transform.position;
            _frame.PenGrip = true; Poll();
            hand.position += Vector3.right * 0.2f; Update();
            Assert.AreEqual(before.x + 0.2f, _view.transform.position.x, 1e-3f);
            _frame.PenGrip = false; Poll();
            hand.position += Vector3.right * 0.2f; Update();
            Assert.AreEqual(before.x + 0.2f, _view.transform.position.x, 1e-3f, "released grip no longer drags the model");
        }

        [Test]
        public void TrackingLossAndASecondWorkspaceEndTheGrip()
        {
            Physics.SyncTransforms();
            var body = _view.GetComponentsInChildren<CadBody>().First();
            var center = body.GetComponent<Collider>().bounds.center;
            var hand = _ray.Origin;
            hand.SetPositionAndRotation(center + Vector3.back * 0.5f, Quaternion.LookRotation(Vector3.forward));
            Physics.SyncTransforms();
            _frame.PenGrip = true; Poll();
            _frame.PenTracked = false; Poll();
            var before = _view.transform.position;
            hand.position += Vector3.right * 0.3f; Update();
            Assert.AreEqual(before, _view.transform.position, "tracking lost closes the grab");
            _frame.PenTracked = true; _frame.PenGrip = false; Poll();
        }

        // ---------------------------------------------------------------- voice surface

        [Test]
        public void VoiceMeasureUsesTheSameEnablementAsItsAction()
        {
            Assert.True(_workspace.IsEnabled(CommandIds.Measure));
            Assert.True(_workspace.Invoke(CommandIds.Measure)); Assert.True(_workspace.Measuring);
            _workspace.SetVisible(false);
            Assert.False(_workspace.IsEnabled(CommandIds.Measure)); Assert.False(_workspace.Invoke(CommandIds.Measure));
            Assert.False(_workspace.IsEnabled(CommandIds.Isolate)); Assert.False(_workspace.Invoke(CommandIds.Isolate));
        }

        [Test]
        public void LeavingTheSessionDropsMeasurementKeypadAndList()
        {
            GoOnline();
            Do(InspectWorkspace.IdMeasure); Do(InspectWorkspace.IdScale);
            _workspace.SetVisible(false);
            Assert.False(_workspace.Measuring); Assert.False(_shell.Palette.InTabGroup);
            Assert.False(Enabled(InspectWorkspace.IdMeasure));
            _workspace.SetVisible(true); Do(InspectWorkspace.IdSectionOffset);
            _workspace.SetVisible(false); Assert.IsNull(_workspace.ActiveEntry); Assert.False(_shell.Palette.KeypadVisible);
        }
    }
}
