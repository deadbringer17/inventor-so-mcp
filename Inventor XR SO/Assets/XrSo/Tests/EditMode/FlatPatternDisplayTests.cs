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
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    /// <summary>M5-06 (software part): the flat pattern is a display-only companion of the folded part.</summary>
    public class FlatPatternDisplayTests
    {
        private static readonly DocumentState R1 = new DocumentState("doc_bolt", "r", "v"), R2 = new DocumentState("doc_bolt", "r2", "v2");
        private GameObject _root;
        private Material _material;
        private CadSceneView _view;
        private FlatPatternDisplay _display;
        private FlatPatternView _flat;
        private Backend _backend;

        private sealed class Backend : ISheetMetalBackend
        {
            public GlbModel Model;
            public Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct) => throw new NotSupportedException();
            public Task<FlatPatternMesh> GetFlatPatternMeshAsync(DocumentState state, CancellationToken ct) =>
                Task.FromResult(new FlatPatternMesh(state.DocumentId, state.Revision, "h1", "a_" + new string('0', 64), Model, 120.5, 40, 2, 2.0));
        }

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Flat pattern test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _view = _root.AddComponent<CadSceneView>(); _view.BodyMaterial = _material;
            var model = GlbModel.Parse(File.ReadAllBytes(CoreInUnityTests.BoltFixture));
            var graph = SceneGraph.FromJson(JObject.Parse(@"{""document_id"":""doc_bolt"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",
                ""definition_document_ids"":[""doc_bolt""],""root"":{""name"":""Bolt"",""definition_kind"":""part"",""definition_document_id"":""doc_bolt"",""children"":[]}}"));
            _view.Show(new LoadedScene(graph, new Dictionary<string, GlbModel> { { "doc_bolt", model } },
                new Dictionary<string, string> { { "doc_bolt", "a_test" } }, new List<string>()));
            _backend = new Backend { Model = model };
            _flat = new FlatPatternView(_backend); _flat.SetContext(R1, true);
            var host = new GameObject("Display"); host.transform.SetParent(_root.transform);
            _display = host.AddComponent<FlatPatternDisplay>(); _display.Initialize(_view, null);
            _display.Bind(_flat); _display.SetPresented(true);
        }

        [TearDown] public void Cleanup() { _flat.Dispose(); Object.DestroyImmediate(_root); Object.DestroyImmediate(_material); }

        private void Show() => _flat.ShowAsync().GetAwaiter().GetResult();

        [Test] public void NothingIsDrawnUntilThePatternIsVerified()
        {
            Assert.False(_display.IsShowing); Assert.Null(_view.transform.Find("Flat pattern (view only)")); Assert.False(_display.LabelVisible);
            Show();
            Assert.True(_display.IsShowing); Assert.True(_display.LabelVisible);
        }

        [Test] public void IsShownBesideTheFoldedPartWithNativeMeasuresAndTheViewOnlyLabel()
        {
            var folded = ScenePlacement.LocalBounds(_view.transform);
            Show();
            StringAssert.Contains("Sviluppo — sola vista", _display.LabelText);
            StringAssert.Contains("Lunghezza 120,5 mm", _display.LabelText); StringAssert.Contains("Larghezza 40 mm", _display.LabelText);
            StringAssert.Contains("Spessore 2 mm", _display.LabelText); StringAssert.Contains("Pieghe 2", _display.LabelText);
            Assert.Less(folded.max.x, _display.LocalBounds.min.x, "Beside the folded part along +X, not overlapping it.");
            Assert.That(_display.LocalBounds.center.y, Is.EqualTo(folded.center.y).Within(1e-4));
            Assert.AreEqual(folded, ScenePlacement.LocalBounds(_view.transform), "The pattern does not change the folded part's bounds.");
        }

        private static Bounds WorldBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        [Test] public void OnAWorkPlaneItLiesFlatInFrontOfTheUserFittedToTheSheetArea()
        {
            var frame = WorkbenchFrame.FromHead(new CadPoint(0, 1.6, 0), 0, null);
            _display.SetWorkPlane(frame);
            Show();
            Assert.True(_display.HasWorkPlane); Assert.True(_display.Tweening, "it eases in, it never jumps");
            _display.Snap();
            var bounds = WorldBounds(_display.MeshRoot);
            var floor = WorkbenchLayout.Part(frame, 0.4).Position;
            Assert.AreEqual((float)floor.X, bounds.center.x, 2e-3f); Assert.AreEqual((float)floor.Z, bounds.center.z, 2e-3f);
            Assert.AreEqual((float)floor.Y + 0.002f + bounds.extents.y, bounds.center.y, 2e-3f);
            Assert.LessOrEqual(bounds.size.x, (float)WorkbenchLayout.SheetWidth + 2e-3f);
            Assert.LessOrEqual(bounds.size.z, (float)WorkbenchLayout.SheetDepth + 2e-3f);
            Assert.True(bounds.size.x >= bounds.size.z - 1e-4f, "the longest side runs along the user's right");
        }

        [Test] public void FlatViewOffHidesThePatternAndKeepsTheVerifiedAssetAndFoldedViewOnShowsItAgain()
        {
            _display.SetWorkPlane(WorkbenchFrame.FromHead(new CadPoint(0, 1.6, 0), 0, null));
            Show(); Assert.True(_display.IsShowing); Assert.True(_display.FoldedHidden);
            var asset = _flat.Asset;
            _display.SetFlatView(false);
            Assert.False(_display.IsShowing); Assert.False(_display.FoldedHidden); Assert.False(_display.LabelVisible);
            Assert.AreSame(asset, _flat.Asset); Assert.AreEqual(FlatPatternState.Ready, _flat.State);
            _display.SetFlatView(true); Assert.True(_display.IsShowing); Assert.True(_display.FoldedHidden);
        }

        [Test] public void DetachOffsetIsInPlaneMetresAndAttachReturnsToThePlacedPose()
        {
            _display.SetWorkPlane(WorkbenchFrame.FromHead(new CadPoint(0, 1.6, 0), 0, null));
            Show(); _display.Snap();
            var placed = _display.MeshRoot.position;
            _flat.Detach(); _display.Snap();
            Assert.True(_flat.MoveLocal(0.1, 0, -0.05));
            Assert.That(Vector3.Distance(_display.MeshRoot.position, placed + new Vector3(0.1f, 0, -0.05f)), Is.LessThan(1e-4f));
            var offset = _display.OffsetForRootPosition(placed + new Vector3(0.2f, 0, 0.1f));
            Assert.That(Vector3.Distance(offset, new Vector3(0.2f, 0, 0.1f)), Is.LessThan(1e-4f));
            _flat.Attach(); _display.Snap();
            Assert.That(Vector3.Distance(_display.MeshRoot.position, placed), Is.LessThan(1e-4f));
        }

        [Test] public void ZoomOnThePlaneIsClampedAndResetPlaneReturnsToTheFit()
        {
            _display.SetWorkPlane(WorkbenchFrame.FromHead(new CadPoint(0, 1.6, 0), 0, null));
            Show(); _display.Snap();
            float fitted = WorldBounds(_display.MeshRoot).size.x;
            for (int i = 0; i < 3000; i++) _display.ZoomPlane(1, 0.1f);
            _display.Snap();
            Assert.LessOrEqual(_display.MeshRoot.lossyScale.x, (float)WorkbenchLayout.MaxScale + 1e-4f);
            for (int i = 0; i < 6000; i++) _display.ZoomPlane(-1, 0.1f);
            _display.Snap();
            Assert.GreaterOrEqual(_display.MeshRoot.lossyScale.x, (float)WorkbenchLayout.MinScale - 1e-6f);
            _display.ResetPlane(); _display.Snap();
            Assert.That(WorldBounds(_display.MeshRoot).size.x, Is.EqualTo(fitted).Within(2e-3f));
        }

        [Test] public void IsNeverACadReferenceOrAPhysicsTarget()
        {
            Show();
            var root = _display.MeshRoot;
            Assert.AreEqual(FlatPatternDisplay.NoRaycastLayer, root.gameObject.layer);
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) Assert.AreEqual(FlatPatternDisplay.NoRaycastLayer, t.gameObject.layer);
            Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(0, root.GetComponentsInChildren<CadBody>(true).Length);
            Assert.AreEqual(0, (Physics.DefaultRaycastLayers & (1 << FlatPatternDisplay.NoRaycastLayer)), "CadRaycaster's layer mask never queries this layer.");
            Assert.False(_flat.IsCadSelectable);
        }

        [Test] public void DetachAndGripOffsetMoveOnlyTheLocalView()
        {
            Show();
            var root = _display.MeshRoot; var before = root.localPosition;
            Assert.False(_flat.MoveLocal(1, 0, 0), "An attached pattern cannot be repositioned.");
            _flat.Detach(); Assert.True(_flat.MoveLocal(0.3, -0.1, 0.2));
            Assert.That(Vector3.Distance(root.localPosition, before + new Vector3(0.3f, -0.1f, 0.2f)), Is.LessThan(1e-5));
            StringAssert.Contains("Staccato", _display.LabelText);
            _flat.Attach();
            Assert.That(Vector3.Distance(root.localPosition, before), Is.LessThan(1e-5), "Re-attaching returns beside the part.");
        }

        [Test] public void HitTestFindsTheDisplayForGripAndMissesEmptySpace()
        {
            Show();
            var centre = _view.transform.TransformPoint(_display.LocalBounds.center);
            Assert.True(_display.HitTest(new Ray(centre + new Vector3(0, 0, -1), Vector3.forward)));
            Assert.False(_display.HitTest(new Ray(centre + new Vector3(0, 5, -1), Vector3.forward)));
            _flat.Hide(); Assert.False(_display.HitTest(new Ray(centre + new Vector3(0, 0, -1), Vector3.forward)));
        }

        [Test] public void RevisionChangeHidesTheDisplayUntilReverified()
        {
            Show(); Assert.True(_display.IsShowing);
            _flat.SetContext(R2, true);
            Assert.False(_display.IsShowing); Assert.Null(_view.transform.Find("Flat pattern (view only)")); Assert.False(_display.LabelVisible);
            _flat.ShowAsync().GetAwaiter().GetResult();
            Assert.True(_display.IsShowing); Assert.AreEqual("doc_bolt|r2|h1", _display.ShownKey);
        }

        [Test] public void OfflineKeepsTheLastVerifiedPatternWithAReadOnlyBadge()
        {
            Show(); _flat.SetContext(R1, false);
            Assert.True(_display.IsShowing);
            StringAssert.Contains("revisione r", _display.LabelText); StringAssert.Contains("sola ispezione", _display.LabelText);
        }

        [Test] public void ClosingTheWorkspaceHidesItWithoutDroppingTheState()
        {
            Show(); _display.SetPresented(false);
            Assert.False(_display.IsShowing); Assert.AreEqual(FlatPatternState.Ready, _flat.State);
            _display.SetPresented(true); Assert.True(_display.IsShowing);
        }

        [Test] public void MeshesAndMaterialsAreReleasedWithTheDisplay()
        {
            Show(); var root = _display.MeshRoot.gameObject;
            var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            var material = root.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            // EditMode does not dispatch this runtime MonoBehaviour callback; exercise its cleanup directly.
            typeof(FlatPatternDisplay).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_display, null);
            Object.DestroyImmediate(_display.gameObject);
            Assert.True(root == null, "The mesh root lives under the view and must be released by the display.");
            Assert.True(mesh == null, "The display-created mesh must be released.");
            Assert.True(material == null, "The display-created material must be released.");
        }
    }
}
