using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Capture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace InventorXrSo.Tests
{
    public sealed class RealityCaptureCadTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject _root;
        private RealityCaptureWorkspace _workspace;
        private PairedServer _server;
        private FakeCad _cad;
        private ScanReview _review;
        [SetUp] public void Setup()
        {
            _root=new GameObject("RC4 SYNTHETIC test");var head=new GameObject("head");head.transform.SetParent(_root.transform,false);head.AddComponent<Camera>();
            _workspace=_root.AddComponent<RealityCaptureWorkspace>();_workspace.Initialize(head.transform,()=>_workspace.Close());_workspace.Open();
            _review=ScanReviewFixture.Create();_server=new PairedServer("paired.test",443,new string('a',64),"test","token");_cad=new FakeCad(_review);
            var client=new CaptureReviewClient(_cad,_server);Set("_scanReview",_review);Set("_reviewClient",client);Set("_reviewSession",_review.SessionId);Set("_reviewCancel",new CancellationTokenSource());
            var scan=new GameObject("scan");scan.transform.SetParent(_root.transform,false);var view=scan.AddComponent<ScanSceneView>();view.Load(_review,new Vector3(0,1,1),Quaternion.identity);Set("_scanView",view);
            _workspace.BindCad(()=>new CaptureCadContext(_server,_cad.State,null));
        }
        [TearDown] public void Cleanup()=>Object.DestroyImmediate(_root);
        private void Set(string field,object value)=>typeof(RealityCaptureWorkspace).GetField(field,Hidden).SetValue(_workspace,value);
        private Task Run(string method)=>(Task)typeof(RealityCaptureWorkspace).GetMethod(method,Hidden).Invoke(_workspace,null);
        [Test] public async Task ReferenceIsPreviewedThenAppliedOnceAndSavedOnlyOnExplicitAction()
        {
            await Run("NewReferenceAsync");Assert.IsTrue(_workspace.Panel.VoiceActions.Any(a=>a.label=="Applica riferimento"));
            Assert.That(_cad.Tools,Does.Contain("inventor_plan_change"));Assert.That(_cad.Tools,Does.Not.Contain("inventor_commit_plan"));
            Assert.IsTrue(_workspace.Panel.InvokeVoiceAction("Applica riferimento"));await Task.Yield();
            Assert.AreEqual(1,_cad.Tools.Count(t=>t=="inventor_commit_plan"));Assert.That(_cad.Tools,Does.Not.Contain("inventor_save_document_safe"));
            Assert.IsTrue(_workspace.Panel.InvokeVoiceAction("Salva riferimento"));await Task.Yield();Assert.That(_cad.Tools,Does.Contain("inventor_save_document_safe"));
            Assert.IsFalse(_workspace.Panel.InvokeVoiceAction("Applica riferimento"));
        }
        [Test] public async Task UncertainCommitIsConsumedAndNeverSavedOrRetried()
        {
            _cad.LoseCommitResponse=true;await Run("NewReferenceAsync");_workspace.Panel.InvokeVoiceAction("Applica riferimento");await Task.Yield();
            Assert.AreEqual(1,_cad.Tools.Count(t=>t=="inventor_commit_plan"));Assert.IsFalse(_workspace.Panel.InvokeVoiceAction("Applica riferimento"));
            Assert.That(_cad.Tools,Does.Not.Contain("inventor_save_document_safe"));Assert.IsNull(typeof(RealityCaptureWorkspace).GetField("_referencePlan",Hidden).GetValue(_workspace));
            StringAssert.Contains("potrebbe essere stato eseguito",string.Join("\n",_workspace.Panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Select(t=>t.text)));
        }
        [Test] public async Task MissingNativeProofNeverOffersApply()
        {
            _cad.BadPreview=true;Assert.ThrowsAsync<FormatException>(async()=>await Run("NewReferenceAsync"));
            Assert.IsFalse(_workspace.Panel.VoiceActions.Any(a=>a.label=="Applica riferimento"));Assert.That(_cad.Tools,Does.Not.Contain("inventor_commit_plan"));await Task.CompletedTask;
        }
        [Test] public async Task ChangingDocumentAfterPreviewRefusesCommitBeforeSendingIt()
        {
            await Run("NewReferenceAsync");_cad.State=new DocumentState("foreign","r0","v0");_workspace.Panel.InvokeVoiceAction("Applica riferimento");await Task.Yield();
            Assert.That(_cad.Tools,Does.Not.Contain("inventor_commit_plan"));
        }
        [Test] public void ComparisonCopiesCadWithoutCollidersAndDestroysItsOwnedGeometry()
        {
            var graph=SceneGraph.FromJson(new JObject {["document_id"]="cad",["revision"]="r0",["visual_revision"]="v0",["kind"]="part",["definition_document_ids"]=new JArray("cad"),["root"]=new JObject {["definition_document_id"]="cad",["definition_kind"]="part"}});
            var scene=new LoadedScene(graph,new Dictionary<string,GlbModel>{{"cad",GlbModel.Parse(_review.Glb)}},new Dictionary<string,string>{{"cad","a_"+new string('a',64)}},Array.Empty<string>());
            var go=new GameObject("read-only CAD copy");go.transform.SetParent(_root.transform,false);var overlay=go.AddComponent<CaptureCadOverlayView>();
            overlay.Show(scene,Matrix4x4.TRS(new Vector3(1,2,3),Quaternion.Euler(0,20,0),Vector3.one));Assert.AreEqual("cad",overlay.DocumentId);
            Assert.Greater(go.GetComponentsInChildren<MeshFilter>().Length,0);Assert.AreEqual(0,go.GetComponentsInChildren<Collider>().Length);Assert.AreEqual(0,go.GetComponentsInChildren<CadBody>().Length);
            var meshes=go.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();overlay.Clear();Assert.IsFalse(go.activeSelf);Assert.IsTrue(meshes.All(m=>m==null));
        }
        [Test] public void TrackingLossOrRecenterInvalidatesEvenARegistrationNotYetDisplayed()
        {
            Set("_trackingFromScan",ScanRegistration.Identity);Set("_registrationEpoch",3);
            Assert.IsTrue(_workspace.ValidatePhysicalTracking(true,3));Assert.IsFalse(_workspace.ValidatePhysicalTracking(false,3));
            Assert.IsNull(typeof(RealityCaptureWorkspace).GetField("_trackingFromScan",Hidden).GetValue(_workspace));
            Set("_trackingFromScan",ScanRegistration.Identity);Assert.IsFalse(_workspace.ValidatePhysicalTracking(true,4));
            Assert.IsNull(typeof(RealityCaptureWorkspace).GetField("_trackingFromScan",Hidden).GetValue(_workspace));Assert.IsEmpty(_cad.Tools);
        }
        [Test] public void RepeatedCadOccurrencesShareGeometryAndInvalidSnapshotsRemoveTheOldCopy()
        {
            var child=new JObject {["definition_document_id"]="cad",["definition_kind"]="part"};
            var graph=SceneGraph.FromJson(new JObject {["document_id"]="assembly",["revision"]="r0",["visual_revision"]="v0",["kind"]="assembly",["root"]=new JObject {["definition_kind"]="assembly",["children"]=new JArray(child.DeepClone(),child.DeepClone())}});
            var scene=new LoadedScene(graph,new Dictionary<string,GlbModel>{{"cad",GlbModel.Parse(_review.Glb)}},new Dictionary<string,string>{{"cad","a_"+new string('a',64)}},Array.Empty<string>());
            var go=new GameObject("Repeated geometry");go.transform.SetParent(_root.transform,false);var overlay=go.AddComponent<CaptureCadOverlayView>();overlay.Show(scene,Matrix4x4.identity);
            var filters=go.GetComponentsInChildren<MeshFilter>();Assert.AreEqual(2,filters.Length);Assert.AreSame(filters[0].sharedMesh,filters[1].sharedMesh);
            Assert.Throws<ArgumentException>(()=>overlay.Show(new LoadedScene(graph,scene.Models,scene.AssetIds,new[]{"missing"}),Matrix4x4.identity));Assert.IsFalse(go.activeSelf);
        }
        [Test] public async Task SwitchingPairedPcRefusesCadWritesWithoutSendingTheOriginalToken()
        {
            _server=new PairedServer("other.test",443,new string('b',64),"other","other-token");
            Assert.ThrowsAsync<InvalidOperationException>(async()=>await Run("NewReferenceAsync"));Assert.IsEmpty(_cad.Tools);await Task.CompletedTask;
        }
        [Test] public void OfflineCadContextClearsAVisibleComparison()
        {
            var graph=SceneGraph.FromJson(new JObject {["document_id"]="cad",["revision"]="r0",["visual_revision"]="v0",["kind"]="part",["root"]=new JObject {["definition_document_id"]="cad",["definition_kind"]="part"}});
            var scene=new LoadedScene(graph,new Dictionary<string,GlbModel>{{"cad",GlbModel.Parse(_review.Glb)}},new Dictionary<string,string>{{"cad","a_"+new string('a',64)}},Array.Empty<string>());
            var go=new GameObject("Disconnect copy");go.transform.SetParent(_root.transform,false);var overlay=go.AddComponent<CaptureCadOverlayView>();overlay.Show(scene,Matrix4x4.identity);Set("_cadOverlay",overlay);
            _workspace.BindCad(()=>null);typeof(RealityCaptureWorkspace).GetMethod("CheckCadOverlayValidity",Hidden).Invoke(_workspace,null);
            Assert.IsFalse(go.activeSelf);Assert.AreEqual(0,go.GetComponentsInChildren<MeshFilter>(true).Length);
        }
        private sealed class FakeCad:IHttpTransport
        {
            private readonly ScanReview _scan;
            public DocumentState State=new DocumentState("cad","r0","v0");
            public readonly List<string> Tools=new List<string>();
            public bool LoseCommitResponse,BadPreview;
            public FakeCad(ScanReview scan)=>_scan=scan;
            public Task<TransportResponse> SendAsync(TransportRequest request,CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();var rpc=JObject.Parse(System.Text.Encoding.UTF8.GetString(request.Body));string method=rpc.Value<string>("method");JObject result;
                if(method=="tools/call")
                {
                    string tool=rpc["params"].Value<string>("name");Tools.Add(tool);var args=(JObject)rpc["params"]["arguments"];JObject data;
                    switch(tool)
                    {
                        case "inventor_new_document_safe":State=new DocumentState("reference","r0","v0");data=new JObject {["workspace_managed"]=true,["document_id"]=State.DocumentId,["revision"]=State.Revision,["path"]="managed/Reality.ipt"};break;
                        case "inventor_get_visual_revision":data=new JObject {["document_id"]=State.DocumentId,["revision"]=State.Revision,["visual_revision"]=State.VisualRevision};break;
                        case "inventor_reality_capture_align_to_cad":
                            var registration=JObject.Parse(args.Value<string>("registration_json"));data=new JObject {["ok"]=true,["alignment"]=new JObject {["version"]=1,["review_version"]=0,["solve_revision"]=1,["scan_id"]=_scan.SessionId,["document_id"]=State.DocumentId,["cad_revision"]=State.Revision,["method"]="scan_basis",["scan_metric_to_inventor_mm"]=JArray.FromObject(ScanRegistration.ScanToCadBasis)}};break;
                        case "inventor_reality_capture_create_reference":data=new JObject {["ok"]=true,["operations"]=new JArray(new JObject {["command"]="create_reality_reference",["arguments"]=new JObject {["reference"]=new JObject {["points"]=JArray.FromObject(_scan.Points)}}})};break;
                        case "inventor_plan_change":data=new JObject {["plan_id"]="p1",["document_id"]=State.DocumentId,["document_revision"]=State.Revision,["operations"]=args["operations"].DeepClone(),["expires_utc"]=DateTimeOffset.UtcNow.AddMinutes(15),
                            ["preview"]=new JObject {["document_id"]=State.DocumentId,["revision"]=State.Revision,["status"]="preview_rolled_back",["validated"]=BadPreview?new JArray():new JArray("rebuild","feature_health")}};break;
                        case "inventor_commit_plan":State=new DocumentState("reference","r1","v1");if(LoseCommitResponse) throw new System.IO.IOException("Synthetic response loss after native commit.");data=new JObject {["plan_id"]="p1",["status"]="committed",["document_id"]=State.DocumentId,["revision"]=State.Revision};break;
                        case "inventor_save_document_safe":data=new JObject {["status"]="saved",["path"]="managed/Reality.ipt"};break;
                        default:throw new InvalidOperationException("Unexpected fake tool: "+tool);
                    }
                    result=new JObject {["content"]=new JArray(new JObject {["type"]="text",["text"]=data.ToString(Formatting.None)})};
                }
                else result=new JObject();
                var response=new JObject {["jsonrpc"]="2.0",["id"]=rpc["id"]?.DeepClone(),["result"]=result};
                return Task.FromResult(new TransportResponse(200,new Dictionary<string,string>{{"Content-Type","application/json"}},System.Text.Encoding.UTF8.GetBytes(response.ToString(Formatting.None))));
            }
            public Task<int> StreamLinesAsync(TransportRequest request,Action<string> onLine,CancellationToken ct)=>throw new NotSupportedException();
        }
    }
}
