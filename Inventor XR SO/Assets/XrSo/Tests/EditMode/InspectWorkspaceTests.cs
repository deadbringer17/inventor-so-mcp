using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class InspectWorkspaceTests
    {
        private GameObject _root;
        private InspectWorkspace _workspace;
        private CadSceneView _view;
        private HomePanel Panel => _workspace.GetComponentInChildren<HomePanel>(true);
        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Test rig");
            var eye = Child("Eye").AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 1.6f, 0);
            var left = Child("Left"); var right = Child("Right");
            var ray = right.AddComponent<ControllerRay>(); ray.Configure(right.transform, right.AddComponent<LineRenderer>());
            _view = Child("Model").AddComponent<CadSceneView>();
            var visuals = _view.gameObject.AddComponent<SelectionVisuals>(); visuals.Configure(_view, null, null);
            var env = Child("Environment").AddComponent<EnvironmentModeController>(); env.Configure(eye, null);
            _workspace = Child("Workspace").AddComponent<InspectWorkspace>();
            _workspace.Initialize(_view, visuals, ray, eye.transform, left.transform, env);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            _workspace.SetVisible(true);
        }
        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(_root); }
        private Button Button(string text) => Panel.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == text);
        private string Body => string.Join("\n", Panel.GetComponentsInChildren<Text>().Select(t => t.text));

        [TestCase("tools")][TestCase("browser")][TestCase("details")][TestCase("measure")][TestCase("scale")][TestCase("section")]
        public void WorkspaceActionsStayInsidePanel(string page)
        {
            _workspace.Open(page);
            var rect = (RectTransform)Panel.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect); Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            foreach (var button in Panel.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = rect.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(rect.rect.xMin - 1, rect.rect.xMax + 1), button.name);
                    Assert.That(point.y, Is.InRange(rect.rect.yMin - 1, rect.rect.yMax + 1), button.name);
                }
            }
        }
        [Test]
        public void PinnedPanelKeepsItsPoseAcrossClosingAndReopening()
        {
            _workspace.Open("browser"); Button("Pin pannello").onClick.Invoke();
            Panel.transform.position = new Vector3(4, 2, 3);
            Button("Chiudi").onClick.Invoke(); _workspace.Open("browser");
            Assert.AreEqual(new Vector3(4, 2, 3), Panel.transform.position);
        }
        [Test]
        public void RefreshClearsMeasurementAndSectionButKeepsLocalToolsAvailableOffline()
        {
            _workspace.Open("tools"); Button("Misura").onClick.Invoke();
            var measure = _view.GetComponentInChildren<MeasurementView>();
            measure.Pick(Vector3.zero); measure.Pick(Vector3.right); Assert.IsTrue(measure.Pin());
            var section = _view.GetComponentInChildren<SectionPlane>(); section.SetActive(true);
            var scene = CadSceneViewTests.BoltScene(); _view.Show(scene); _workspace.SetScene(scene);
            Assert.AreEqual(0, measure.PinnedCount); Assert.IsFalse(section.Active);
            _workspace.SetOnline(false); _workspace.Open("tools"); Button("Misura").onClick.Invoke();
            Assert.IsTrue(_workspace.Measuring);
        }
        [Test]
        public async Task LatePropertiesFromAnOldSceneAreDiscarded()
        {
            var backend = new DeferredInspection();
            _workspace.Bind(backend, null); _workspace.SetScene(CadSceneViewTests.BoltScene()); _workspace.SetOnline(true);
            _workspace.Open("details"); Button("Aggiorna").onClick.Invoke();
            _workspace.SetScene(CadSceneViewTests.BoltScene());
            backend.Result.SetResult(InspectionInfo.FromJson(new JObject { ["material"] = "STALE STEEL", ["mass_kg"] = 900 }));
            await Task.Yield();
            Assert.IsFalse(Body.Contains("STALE STEEL"));
        }
        private sealed class DeferredInspection : IInspectionBackend
        {
            public readonly TaskCompletionSource<InspectionInfo> Result = new TaskCompletionSource<InspectionInfo>();
            public DocumentState LastState;
            public Task<InspectionInfo> InspectAsync(DocumentState state, string occurrenceId, CancellationToken ct) { LastState = state; return Result.Task; }
            public Task<IReadOnlyList<OpenDocument>> ListOpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenDocument>>(Array.Empty<OpenDocument>());
            public Task ActivateOpenAsync(string documentId, CancellationToken ct) => Task.CompletedTask;
        }

        [Test]
        public void NonVisualRevisionUsesCurrentTokenAndInvalidatesPinnedMeasurements()
        {
            var backend = new DeferredInspection();
            var scene = CadSceneViewTests.BoltScene();
            _workspace.Bind(backend, null); _workspace.SetScene(scene); _workspace.SetOnline(true);
            var measure = _view.GetComponentInChildren<MeasurementView>();
            measure.Begin(); measure.Pick(Vector3.zero); measure.Pick(Vector3.right); measure.Pin();
            _workspace.SetDocumentState(new DocumentState(scene.Graph.DocumentId, "new-property-revision", scene.Graph.VisualRevision));
            _workspace.Open("details"); Button("Aggiorna").onClick.Invoke();
            Assert.AreEqual("new-property-revision", backend.LastState.Revision);
            Assert.AreEqual(0, measure.PinnedCount);
            backend.Result.SetCanceled();
        }
    }
}
