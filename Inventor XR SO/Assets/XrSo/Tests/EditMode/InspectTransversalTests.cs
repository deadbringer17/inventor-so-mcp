using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    /// <summary>
    /// Rig shared by the M9 transversal Ispeziona tests: the real InspectWorkspace and ViewActions composed into a catalog by
    /// context, with a stand-in for the authoring workspace that decides the context, the commit bar and the suspension.
    /// </summary>
    internal sealed class InspectRig : IDisposable
    {
        public sealed class Stub : IActionProvider, ITabStateSource
        {
            public bool CommitApplyEnabled = true;
            public TabState State = new TabState();
            public XrAction Apply;
            public CommitBarState CommitBar { get; } = new CommitBarState();
            public Stub() { Apply = new XrAction(CommitIds.Apply, "Applica", ActionCatalog.CommitTab, () => CommitApplyEnabled, () => { }); }
            public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("schizzo", "Schizzo"), new XrTab("feature", "Feature"), new XrTab("lamiera", "Lamiera"),
                new XrTab("componenti", "Componenti"), new XrTab("vincoli", "Vincoli"), new XrTab("sviluppo", "Sviluppo") };
            public IEnumerable<XrAction> Actions => new[]
            {
                new XrAction("stub.schizzo", "Crea schizzo", "schizzo", () => true, () => { }),
                new XrAction("stub.feature", "Estrudi", "feature", () => true, () => { }),
                new XrAction("stub.lamiera", "Flangia", "lamiera", () => true, () => { }),
                new XrAction("stub.componenti", "Isola componente", "componenti", () => true, () => { }),
                new XrAction("stub.vincoli", "Vincola", "vincoli", () => true, () => { }),
                new XrAction("stub.sviluppo", "Sviluppo", "sviluppo", () => true, () => { }),
                Apply,
            };
            public IEnumerable<XrAction> ContextActions(InventorXrSo.Core.Ui.SelectionKind selection) => Array.Empty<XrAction>();
            public TabState TabState => State;
        }

        private readonly GameObject _root = new GameObject("Inspect rig");
        private readonly List<GameObject> _roots = new List<GameObject>();
        public InspectWorkspace Inspect;
        public ViewActions View;
        public ActionCatalog Catalog;
        public UiShell Shell;
        public Stub Authoring = new Stub();
        public DocContext? Context;
        public bool Suspended, OwnsView;
        public string External;
        public int Fits;
        public bool Legend = true;

        public InspectRig(bool attachScene = true)
        {
            var eye = Child("Eye").AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 1.6f, 0);
            var left = Child("Left"); var right = Child("Right");
            var ray = right.AddComponent<ControllerRay>(); ray.Configure(right.transform, right.AddComponent<LineRenderer>());
            var model = Child("Model").AddComponent<CadSceneView>();
            var visuals = model.gameObject.AddComponent<SelectionVisuals>(); visuals.Configure(model, null, null);
            var env = Child("Environment").AddComponent<EnvironmentModeController>(); env.Configure(eye, null);
            Inspect = Child("Workspace").AddComponent<InspectWorkspace>();
            Inspect.SuspendProbe = () => Suspended;
            Inspect.AuthoringOwnsView = () => OwnsView;
            Inspect.ExternalSelection = () => External;
            Inspect.Initialize(model, visuals, ray, eye.transform, env);
            Catalog = new ActionCatalog(TestDocs.Create());
            Shell = UiShell.Create(left.transform, eye.transform, Catalog);
            _roots.Add(Shell.gameObject); _roots.Add(Shell.CommitBar.Canvas.gameObject); _roots.Add(Shell.Hud.Canvas.gameObject);
            var input = Child("XrInput").AddComponent<XrInput>(); input.Source = new SyntheticInputSource();
            Inspect.Attach(Shell, input);
            View = new ViewActions(() => true, () => Fits++, () => Legend, on => Legend = on) { Changed = Catalog.NotifyChanged };
            Catalog.ContextProbe = () => Context;
            Catalog.AddShared(Inspect);
            Catalog.AddShared(View);
            Catalog.SetActive(Inspect);
            if (attachScene)
            {
                var scene = CadSceneViewTests.BoltScene(); model.Show(scene); Inspect.SetScene(scene);
            }
            Inspect.SetVisible(true);
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }

        /// <summary>Opens the context like the app controller does: the authoring workspace becomes the active provider.</summary>
        public void Open(DocContext context)
        {
            Context = context;
            Catalog.SetActive(Authoring);
        }

        public XrAction Act(string id) => Catalog.Find(id);
        public bool Enabled(string id) => Catalog.Find(id)?.Enabled == true;
        public void Update() => typeof(InspectWorkspace).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(Inspect, null);

        public void Dispose()
        {
            foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r);
            Object.DestroyImmediate(_root);
        }
    }

    public class InspectTransversalTests
    {
        private InspectRig _rig;

        [SetUp] public void SetUp() => _rig = new InspectRig();
        [TearDown] public void TearDown() => _rig.Dispose();

        [TestCase(DocContext.Part)]
        [TestCase(DocContext.SheetMetal)]
        public void MeasureAndSectionWorkWhileEditingAPartOrSheetMetal(DocContext context)
        {
            _rig.Open(context);
            _rig.OwnsView = true;   // the authoring workspace owns grips, X and Y
            Assert.True(_rig.Enabled(InspectWorkspace.IdMeasure)); Assert.True(_rig.Enabled(InspectWorkspace.IdSection));
            Assert.True(_rig.Act(InspectWorkspace.IdMeasure).TryInvoke()); Assert.True(_rig.Act(InspectWorkspace.IdSection).TryInvoke());
            Assert.True(_rig.Inspect.Measuring);
            CollectionAssert.AreEqual(new[] { "misura", "sezione" }, ContextTabs.InspectGroup(context));
        }

        [Test]
        public void VisibilityAndVerifyBelongToTheAssemblyGroupOnly()
        {
            _rig.Open(DocContext.Assembly);
            CollectionAssert.AreEqual(new[] { "misura", "sezione", "visibilita", "verifica" }, ContextTabs.InspectGroup(DocContext.Assembly));
            _rig.Open(DocContext.Part);
            CollectionAssert.DoesNotContain(ContextTabs.InspectGroup(DocContext.Part), "visibilita");
            Assert.AreEqual(VoiceMatchKind.NotFound, _rig.Catalog.ResolveVoice("salute assieme").Kind, "Verifica does not answer in a part");
        }

        [Test]
        public void SuspendsOnlyDuringAHandleCaptureOrACadReview()
        {
            _rig.Open(DocContext.Part);
            _rig.Act(InspectWorkspace.IdMeasure).TryInvoke();
            Assert.True(_rig.Inspect.Measuring);
            _rig.Suspended = true; _rig.Update();   // a handle is captured / a CAD review is pending
            Assert.False(_rig.Inspect.Active);
            Assert.False(_rig.Inspect.Measuring, "the measurement in progress is dropped");
            Assert.False(_rig.Enabled(InspectWorkspace.IdMeasure)); Assert.False(_rig.Enabled(InspectWorkspace.IdSection));
            Assert.False(string.IsNullOrEmpty(_rig.Act(InspectWorkspace.IdMeasure).DisabledReason));
            _rig.Suspended = false; _rig.Update();   // released / resolved
            Assert.True(_rig.Inspect.Active); Assert.True(_rig.Enabled(InspectWorkspace.IdMeasure)); Assert.True(_rig.Enabled(InspectWorkspace.IdSection));
        }

        [Test]
        public void MeasureAndSectionNeverBlockTheCommitBar()
        {
            _rig.Open(DocContext.Part);
            _rig.OwnsView = true;
            Assert.IsNull(_rig.Inspect.CommitBar, "Ispeziona has no commit bar of its own");
            _rig.Act(InspectWorkspace.IdMeasure).TryInvoke(); _rig.Act(InspectWorkspace.IdSection).TryInvoke();
            Assert.True(_rig.Inspect.Measuring);
            Assert.AreSame(_rig.Authoring.CommitBar, _rig.Catalog.Active.CommitBar, "the commit bar stays the authoring workspace's");
            Assert.True(_rig.Enabled(CommitIds.Apply), "Applica stays enabled while a measurement/section is open");
            Assert.True(_rig.Catalog.TryInvoke(CommitIds.Apply));
        }

        [Test]
        public void TheIspezionaTabOpensTheGroupAndStickLeftRightScrollsIt()
        {
            _rig.Open(DocContext.Assembly);
            Assert.AreEqual(new[] { "componenti", "vincoli", "ispeziona", "vista", ActionCatalog.DocumentTab }, _rig.Catalog.Tabs.Select(t => t.Id).ToArray());
            Assert.True(_rig.Act(InspectWorkspace.IdGroupOpen).TryInvoke());
            var palette = _rig.Shell.Palette;
            Assert.True(palette.InTabGroup);
            Assert.AreEqual("misura", palette.CurrentTab);
            palette.SelectTab(1); Assert.AreEqual("sezione", palette.CurrentTab);
            palette.SelectTab(1); Assert.AreEqual("visibilita", palette.CurrentTab);
            palette.SelectTab(1); Assert.AreEqual("verifica", palette.CurrentTab);
            palette.SelectTab(1); Assert.AreEqual(ActionCatalog.InspectExitTab, palette.CurrentTab, "the exit tab closes the loop");
            palette.SelectTab(-1); Assert.AreEqual("verifica", palette.CurrentTab, "stick left goes back");
        }

        [Test]
        public void XAndTheBackTabLeaveTheGroup()
        {
            _rig.Open(DocContext.Part);
            _rig.Act(InspectWorkspace.IdGroupOpen).TryInvoke();
            Assert.True(_rig.Shell.Palette.InTabGroup);
            _rig.Inspect.Back();
            Assert.False(_rig.Shell.Palette.InTabGroup, "X leaves the group");
            Assert.AreEqual(ContextTabs.Inspect, _rig.Shell.Palette.CurrentTab, "and shows the tab that opened it");
            _rig.Act(InspectWorkspace.IdGroupOpen).TryInvoke();
            _rig.Shell.Palette.ShowTab(ActionCatalog.InspectExitTab);
            Assert.True(_rig.Act(InspectWorkspace.IdGroupExit).TryInvoke());
            Assert.False(_rig.Shell.Palette.InTabGroup);
        }

        [Test]
        public void WhileAnAuthoringWorkspaceOwnsTheViewInspectDoesNotTakeX()
        {
            _rig.Open(DocContext.Part);
            _rig.OwnsView = true;
            _rig.Act(InspectWorkspace.IdGroupOpen).TryInvoke();
            _rig.Inspect.Back();   // the authoring workspace's Back chain leaves the group, not Ispeziona
            Assert.True(_rig.Shell.Palette.InTabGroup);
        }

        [Test]
        public void VisibilityActsOnTheComponentSelectedInAssieme()
        {
            _rig.Open(DocContext.Assembly);
            _rig.External = ""; _rig.Update();
            Assert.False(_rig.Enabled(InspectWorkspace.IdIsolate), "no component selected");
            _rig.External = "ent_occ_1"; _rig.Update();
            Assert.True(_rig.Enabled(InspectWorkspace.IdIsolate), "the Assieme selection feeds Visibilità");
        }

        [Test]
        public void EveryContextShowsOneVistaTabAndNoTabWithoutActions()
        {
            foreach (var context in new[] { DocContext.Assembly, DocContext.Part, DocContext.SheetMetal })
            {
                _rig.Open(context);
                var tabs = _rig.Catalog.Tabs;
                Assert.AreEqual(1, tabs.Count(t => t.Id == ViewActions.TabView), context + ": exactly one Vista tab");
                foreach (var tab in tabs) Assert.Greater(_rig.Catalog.Palette(tab.Id).Count, 0, context + ": " + tab.Id + " has actions");
            }
        }
    }
}
