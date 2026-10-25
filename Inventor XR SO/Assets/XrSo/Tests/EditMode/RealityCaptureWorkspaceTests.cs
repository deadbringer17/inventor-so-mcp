using System;
using System.Linq;
using System.Reflection;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public class RealityCaptureWorkspaceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private AppController _app;
        private HomePanel _home;
        private RealityCaptureWorkspace _capture;
        private CadSceneView _view;
        private ControllerRay _ray;
        private EnvironmentModeController _environment;
        private UiShell _shell;
        private Material _material;

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Reality Capture integration fixture");
            var rig = Child("Rig");
            var head = Child("Eye", rig.transform).AddComponent<Camera>();
            Child("LeftHandAnchor", rig.transform);
            var right = Child("RightHandAnchor", rig.transform);
            _ray = right.AddComponent<ControllerRay>();
            _ray.Configure(right.transform, right.AddComponent<LineRenderer>());
            _view = Child("CadScene").AddComponent<CadSceneView>();
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _view.BodyMaterial = _material;
            var visuals = _view.gameObject.AddComponent<SelectionVisuals>();
            visuals.Configure(_view, null, null);
            _environment = _root.AddComponent<EnvironmentModeController>();
            _environment.Configure(head, null);
            _app = _root.AddComponent<AppController>();
            _app.Configure(_view, visuals, _ray, _environment, head.transform, null);
            // Initialize the real UI without reading credentials or starting a network session.
            Call("InitializeUi");
            _home = Read<HomePanel>("_home");
            _capture = _app.GetComponent<RealityCaptureWorkspace>();
            _shell = Read<UiShell>("_shell");
            Call("ShowPairing", UiText.NotPaired, UiText.NotPairedBody);
        }

        private GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? _root.transform, false);
            return go;
        }

        [TearDown]
        public void Cleanup()
        {
            if (_app != null) Call("ShowHome");
            // The existing shell and app own detached canvases. Release them before their runtime OnDestroy methods.
            if (_shell != null)
            {
                Object.DestroyImmediate(_shell.Palette.gameObject);
                Object.DestroyImmediate(_shell.CommitBar.gameObject);
                Object.DestroyImmediate(_shell.Hud.gameObject);
                Object.DestroyImmediate(_shell.gameObject);
            }
            foreach (var name in new[] { "_home", "_legend", "_selectionLabel" })
            {
                var item = Read<Component>(name);
                if (item != null) Object.DestroyImmediate(item.gameObject);
            }
            if (_root != null) Object.DestroyImmediate(_root);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        [Test]
        public void UnpairedHomeEntersIndependentCaptureWithPassthroughAndNoCadTools()
        {
            _environment.Set(EnvironmentMode.StudioVr);
            Assert.IsTrue(_home.InvokeVoiceAction(UiText.RealityCapture));
            Assert.IsTrue(_capture.Active);
            Assert.IsFalse(_home.gameObject.activeSelf);
            Assert.AreEqual(EnvironmentMode.MixedReality, _environment.Mode);
            AssertCadHidden();
        }
        [Test]
        public void ApplicationPauseClearsReviewWithoutStartingTheCameraOrReopeningCad()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            var review=InventorXrSo.Xr.Capture.ScanReviewFixture.Create();var scan=Child("Pause scan").AddComponent<InventorXrSo.Xr.Capture.ScanSceneView>();scan.Load(review,Vector3.zero,Quaternion.identity);
            typeof(RealityCaptureWorkspace).GetField("_scanReview",Private).SetValue(_capture,review);typeof(RealityCaptureWorkspace).GetField("_scanView",Private).SetValue(_capture,scan);
            typeof(RealityCaptureWorkspace).GetMethod("OnApplicationPause",Private).Invoke(_capture,new object[]{true});
            Assert.IsFalse(scan.gameObject.activeSelf);Assert.IsTrue(_capture.Panel.VoiceActions.Any(a=>a.label==UiText.CaptureNewObject));AssertCadHidden();
        }

        [Test]
        public void CadCatalogChangesAndConnectionFailuresDoNotReopenCadOrReplaceCapture()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            Read<ActionCatalog>("_catalog").NotifyChanged();
            Read<ContextWorkspaceSwitcher>("_contextSwitcher").OnDocument(new DocInfo("doc", "Part", "part", false));
            Call("OnStatusChanged", SessionStatus.Offline);
            Call("ShowPairing", UiText.TokenRevoked, UiText.NotPairedBody);
            Assert.IsTrue(_capture.Active);
            Assert.IsFalse(_home.gameObject.activeSelf);
            AssertCadHidden();
        }

        [Test]
        public void ReturnHomeWorksWithoutPcAndAllowsRepeatedEntry()
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(_home.InvokeVoiceAction(UiText.RealityCapture));
                Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.ReturnHome));
                Assert.IsFalse(_capture.Active);
                Assert.IsTrue(_home.gameObject.activeSelf);
                AssertCadHidden();
            }
            Assert.AreEqual(1, _root.GetComponentsInChildren<RealityCaptureWorkspace>(true).Length);
        }

        [Test]
        public void CaptureInformationHasOwnNavigationAndMakesNoGeometryClaims()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.CaptureReferences));
            StringAssert.Contains("dimensioni realmente misurate", PanelText());
            Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.RealityCapture));
            Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.CaptureObjects));
            StringAssert.Contains("modulo di acquisizione", PanelText());
            Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.RealityCapture));
            StringAssert.Contains("Nessuna immagine viene registrata", PanelText());
            AssertCadHidden();
        }

        [Test]
        public void ObjectAndAreaSetupRequireExplicitVolumeConfirmationBeforeCapture()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            foreach (string label in new[] { UiText.CaptureNewObject, UiText.CaptureNewArea })
            {
                Assert.IsTrue(_capture.Panel.InvokeVoiceAction(label));
                StringAssert.Contains("zona scelta dall’operatore", PanelText());
                Assert.IsFalse(_capture.Panel.InvokeVoiceAction(UiText.CaptureStart));
                Assert.IsNotNull(_root.GetComponentInChildren<LineRenderer>());
                AssertCadHidden();
                Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.Cancel));
            }
        }

        [Test]
        public void ReenterCadRestoresItsShellAndInputThenCaptureHidesThemAgain()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            _capture.Panel.InvokeVoiceAction(UiText.ReturnHome);
            Call("EnterSession", EnvironmentMode.StudioVr);
            Assert.IsFalse(_capture.Active);
            Assert.IsTrue(_shell.Palette.gameObject.activeSelf);
            Assert.IsTrue(_app.GetComponent<XrInput>().enabled);
            Assert.IsTrue(_view.gameObject.activeSelf);
            Assert.IsTrue(_ray.CanPick);
            Call("LeaveSession");
            _home.InvokeVoiceAction(UiText.RealityCapture);
            Assert.IsTrue(_capture.Active);
            AssertCadHidden();
        }

        [Test]
        public void CaptureButtonsStayInsideThePanelAndUseTheEyeCamera()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            var canvas = (RectTransform)_capture.Panel.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            foreach (var button in _capture.Panel.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var p = canvas.InverseTransformPoint(corner);
                    Assert.That(p.x, Is.InRange(canvas.rect.xMin - 1, canvas.rect.xMax + 1), button.name);
                    Assert.That(p.y, Is.InRange(canvas.rect.yMin - 1, canvas.rect.yMax + 1), button.name);
                }
            }
            Assert.IsNotNull(_capture.Panel.Canvas.worldCamera);
            Assert.IsNotNull(_capture.Panel.GetComponent<OVRRaycaster>());
        }

        private void AssertCadHidden()
        {
            Assert.IsFalse(_view.gameObject.activeSelf);
            Assert.IsFalse(_shell.Palette.gameObject.activeSelf);
            Assert.IsFalse(_shell.CommitBar.gameObject.activeSelf);
            Assert.IsFalse(_shell.Hud.gameObject.activeSelf);
            Assert.IsFalse(_app.GetComponent<XrInput>().enabled);
            Assert.IsFalse(_ray.CanPick);
            Assert.IsFalse(_app.GetComponent<InspectWorkspace>().Active);
            Assert.IsFalse(_app.GetComponent<DesignWorkspace>().Active);
            Assert.IsFalse(_app.GetComponent<AssemblyWorkspace>().Active);
            Assert.IsFalse(_app.GetComponent<LamieraWorkspace>().Active);
        }

        private string PanelText() => string.Join("\n", _capture.Panel.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text));
        [Test]
        public void ReviewPickingUsesTheProductionRoutingGuardsWithoutCadClickThrough()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);var review=InventorXrSo.Xr.Capture.ScanReviewFixture.Create();
            var scan=Child("Routing scan").AddComponent<InventorXrSo.Xr.Capture.ScanSceneView>();scan.Load(review,Vector3.zero,Quaternion.identity);
            typeof(RealityCaptureWorkspace).GetField("_scanReview",Private).SetValue(_capture,review);typeof(RealityCaptureWorkspace).GetField("_scanView",Private).SetValue(_capture,scan);
            var target=scan.transform.TransformPoint(scan.DisplayPoint(review.Points[0].Position));var ray=new Ray(target-Vector3.forward,Vector3.forward);
            Assert.IsFalse(_capture.ProcessReviewRay(ray,false,true,false,false));Assert.IsFalse(_capture.ProcessReviewRay(ray,true,false,false,false));
            Assert.IsFalse(_capture.ProcessReviewRay(ray,true,true,true,false));Assert.IsFalse(_capture.ProcessReviewRay(ray,true,true,false,true));Assert.IsEmpty(scan.Selected);
            Assert.IsTrue(_capture.ProcessReviewRay(ray,true,true,false,false));Assert.AreEqual(new[]{"p0"},scan.Selected);AssertCadHidden();
            _capture.Close();Assert.IsFalse(_capture.ProcessReviewRay(ray,true,true,false,false));
        }
        [Test]
        public void ReviewMeasurementsUseScanMillimetresAndHomeClearsOnlyScanVisuals()
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            var review=InventorXrSo.Xr.Capture.ScanReviewFixture.Create();
            var scan=Child("Independent scan").AddComponent<InventorXrSo.Xr.Capture.ScanSceneView>();
            scan.Load(review,Vector3.zero,Quaternion.identity);scan.SelectIds(new[]{"p0","p1"});
            typeof(RealityCaptureWorkspace).GetField("_scanReview",Private).SetValue(_capture,review);
            typeof(RealityCaptureWorkspace).GetField("_scanView",Private).SetValue(_capture,scan);
            typeof(RealityCaptureWorkspace).GetMethod("ShowReview",Private).Invoke(_capture,null);
            StringAssert.Contains("1000",PanelText());StringAssert.Contains("Incertezza non stimata",PanelText());StringAssert.Contains("UNSCALED",PanelText());
            Assert.IsTrue(_capture.Panel.InvokeVoiceAction("Qualità e zone deboli"));StringAssert.Contains("Copertura geometrica e incertezza non stimate",PanelText());
            AssertCadHidden();
            typeof(RealityCaptureWorkspace).GetMethod("ShowReview",Private).Invoke(_capture,null);
            Assert.IsTrue(_capture.Panel.InvokeVoiceAction(UiText.RealityCapture));Assert.IsFalse(scan.gameObject.activeSelf);Assert.IsNull(typeof(RealityCaptureWorkspace).GetField("_scanReview",Private).GetValue(_capture));
            AssertCadHidden();
        }
        [TestCase(false)]
        [TestCase(true)]
        public async System.Threading.Tasks.Task LateReviewResponseCannotReopenTheWorkspaceAfterHome(bool failure)
        {
            _home.InvokeVoiceAction(UiText.RealityCapture);
            var transport=new DelayedReviewTransport();
            _capture.BindReview(()=>new InventorXrSo.Core.Capture.CaptureReviewClient(transport,new InventorXrSo.Core.Pairing.PairedServer("paired.test",443,new string('a',64),"test","token")),_ray);
            var pending=(System.Threading.Tasks.Task)typeof(RealityCaptureWorkspace).GetMethod("OpenReviewsAsync",Private).Invoke(_capture,new object[]{"11111111111111111111111111111111"});
            _capture.Close();Assert.IsTrue(transport.Token.IsCancellationRequested);
            _capture.Open();
            if(failure) transport.Response.SetException(new System.IO.IOException("Synthetic late failure"));
            else transport.Response.SetResult(new InventorXrSo.Core.Net.TransportResponse(200,null,System.Text.Encoding.UTF8.GetBytes("{\"ok\":true,\"solutions\":[]}")));
            await pending;
            Assert.IsTrue(_capture.Active);Assert.IsTrue(_capture.Panel.VoiceActions.Any(a=>a.label==UiText.CaptureNewObject));
            Assert.IsFalse(_capture.Panel.VoiceActions.Any(a=>a.label=="Nuovo solve"));AssertCadHidden();
        }
        private sealed class DelayedReviewTransport:InventorXrSo.Core.Net.IHttpTransport
        {
            public System.Threading.CancellationToken Token;
            public readonly System.Threading.Tasks.TaskCompletionSource<InventorXrSo.Core.Net.TransportResponse> Response=new System.Threading.Tasks.TaskCompletionSource<InventorXrSo.Core.Net.TransportResponse>();
            public System.Threading.Tasks.Task<InventorXrSo.Core.Net.TransportResponse> SendAsync(InventorXrSo.Core.Net.TransportRequest request,System.Threading.CancellationToken ct) {Token=ct;return Response.Task;}
            public System.Threading.Tasks.Task<int> StreamLinesAsync(InventorXrSo.Core.Net.TransportRequest request,Action<string> onLine,System.Threading.CancellationToken ct)=>throw new NotSupportedException();
        }
        private T Read<T>(string name) => (T)typeof(AppController).GetField(name, Private).GetValue(_app);
        private void Call(string name, params object[] args) => typeof(AppController).GetMethod(name, Private).Invoke(_app, args);
    }
}
