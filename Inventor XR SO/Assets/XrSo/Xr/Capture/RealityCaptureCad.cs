using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    public sealed class CaptureCadContext
    {
        public PairedServer Server {get;}
        public DocumentState Document {get;}
        public LoadedScene Scene {get;}
        public CaptureCadContext(PairedServer server,DocumentState document,LoadedScene scene) {Server=server;Document=document;Scene=scene;}
    }
    public sealed partial class RealityCaptureWorkspace
    {
        private Func<CaptureCadContext> _cadContext;
        private JObject _cadAlignment,_referencePlan;
        private bool _referenceCommitUnconfirmed;
        private DocumentState _referenceDestination;
        private DateTimeOffset _referenceExpires;
        private Capture.CaptureCadOverlayView _cadOverlay;
        private bool _physicalOverlay;
        private double[] _trackingFromScan;
        private int _registrationEpoch;
        private string[] _physicalIds;
        private readonly List<double[]> _physicalPoints=new List<double[]>();
        public void BindCad(Func<CaptureCadContext> context)=>_cadContext=context;
        private CaptureCadContext CadContext()
        {var context=_cadContext?.Invoke();if(context==null||_reviewClient==null||!_reviewClient.Matches(context.Server)) throw new InvalidOperationException("The paired CAD host changed or is unavailable.");return context;}
        private void HideCadOverlay() {_physicalOverlay=false;_cadOverlay?.Clear();}
        private void ClearCadRegistration()
        {HideCadOverlay();_cadAlignment=null;_referencePlan=null;_referenceCommitUnconfirmed=false;_referenceDestination=null;_trackingFromScan=null;_physicalIds=null;_physicalPoints.Clear();}
        private Task<JObject> CadTool(string name,JObject args=null)
        {CadContext();return _reviewClient.CadToolAsync(name,args??new JObject(),_reviewCancel.Token);}
        private async Task<DocumentState> CurrentCadAsync()
        {var json=await CadTool("inventor_get_visual_revision");var state=DocumentState.FromJson(json);if(string.IsNullOrWhiteSpace(state.DocumentId)||string.IsNullOrWhiteSpace(state.Revision)) throw new FormatException("CAD identity missing.");return state;}
        private async Task OpenCadAsync()
        {
            CadContext();int epoch=_reviewEpoch;var result=await Control("get_alignment",new JObject {["revision"]=_scanReview.Revision});
            if(!ReviewCurrent(epoch)) return;_cadAlignment=result["alignment"] as JObject;ShowCadPage();
        }
        private void ShowCadPage()
        {
            if(!Active||_scanReview==null) return;
            string alignment=_cadAlignment==null?"Nessun allineamento CAD salvato.":$"Allineamento v{_cadAlignment["version"]} · {_cadAlignment["method"]}\nCAD {_cadAlignment["document_id"]} · revisione {_cadAlignment["cad_revision"]}.\nResidui mm: {string.Join(", ",(_cadAlignment["residuals_mm"] as JArray??new JArray()).Values<double>().Select(n=>n.ToString("0.###",CultureInfo.CurrentCulture)))}. Incertezza non stimata.";
            if(_measurePrimitives.Count>0) alignment+="\nRiferimenti: "+string.Join("; ",_measurePrimitives.Select(p=>PrimitiveLabel(p.Kind)+" "+p.Id+" · direzione ["+string.Join(", ",p.Direction.Select(n=>n.ToString("0.###",CultureInfo.CurrentCulture)))+"]"));
            Panel.ShowMessage("Riferimento Inventor / overlay",alignment+"\nTre punti: selezionali nell’ordine e inserisci le coordinate CAD in mm. Piano/asse: scegli i due riferimenti di misura e un punto origine. L’asse deve avere una componente nel piano.\nL’overlay reale richiede tre nuove posizioni fisiche nella sessione corrente; nessuna vecchia posa viene riutilizzata.");
            Panel.SetActions(("Allinea tre punti",()=>ReviewAction(AlignThreeAsync)),("Allinea piano / asse",()=>ReviewAction(AlignPlaneAxisAsync)),
                ("Confronto CAD ridotto",()=>ReviewAction(CompareCadAsync)),("Registra punti fisici",BeginPhysicalRegistration),
                (_physicalOverlay?"Ferma overlay":"Avvia overlay 1:1",()=>ReviewAction(StartPhysicalOverlayAsync)),
                ("Nuova parte riferimento",()=>ReviewAction(NewReferenceAsync)),("Vista scansione",ShowReview));
        }
        private JObject RegistrationRequest(DocumentState state,string method,string[] references)=>new JObject {["document_id"]=state.DocumentId,["cad_revision"]=state.Revision,["review_version"]=_scanReview.Annotations.Value<int>("version"),
            ["expected_alignment_version"]=_cadAlignment?.Value<int>("version")??0,["method"]=method,["reference_ids"]=JArray.FromObject(references)};
        private async Task AlignThreeAsync()
        {
            if(_scanView.Selected.Count!=3) {CadHint("Seleziona esattamente tre punti non allineati, nell’ordine delle corrispondenze CAD.");return;}
            int epoch=_reviewEpoch;var state=await CurrentCadAsync();if(!ReviewCurrent(epoch)) return;
            var ids=_scanView.Selected.ToArray();var targets=new List<double[]>();
            void Next()
            {
                if(targets.Count==3) {var request=RegistrationRequest(state,"three_points",ids);request["cad_points_mm"]=JArray.FromObject(targets);request["tolerance_mm"]=5;
                    ReviewAction(()=>SaveAlignmentAsync(request,state));return;}
                PromptVector("CAD mm per "+ids[targets.Count],new double[3],value=>{targets.Add(value);Next();});
            }
            Next();
        }
        private async Task AlignPlaneAxisAsync()
        {
            var plane=_measurePrimitives.FirstOrDefault(p=>p.Kind=="plane");var axis=_measurePrimitives.FirstOrDefault(p=>p.Kind=="axis"||p.Kind=="cylinder");
            if(plane==null||axis==null||_scanView.Selected.Count!=1) {CadHint("Scegli un piano e un asse/cilindro con Riferimento misura; seleziona un solo punto per l’origine corrispondente.");return;}
            int epoch=_reviewEpoch;var state=await CurrentCadAsync();if(!ReviewCurrent(epoch)) return;
            var request=RegistrationRequest(state,"plane_axis",new[]{plane.Id,axis.Id,_scanView.Selected[0]});
            PromptVector("Origine CAD (mm)",new double[3],origin=>PromptVector("Normale piano CAD",new[]{0d,0,1},normal=>PromptVector("Direzione asse CAD",new[]{1d,0,0},direction=>
            {request["cad_origin_mm"]=JArray.FromObject(origin);request["cad_normal"]=JArray.FromObject(normal);request["cad_axis"]=JArray.FromObject(direction);ReviewAction(()=>SaveAlignmentAsync(request,state));})));
        }
        private async Task SaveAlignmentAsync(JObject request,DocumentState expected)
        {
            int epoch=_reviewEpoch;var current=await CurrentCadAsync();if(!ReviewCurrent(epoch)) return;SameDocument(current,expected);
            var result=await Control("align_to_cad",new JObject {["revision"]=_scanReview.Revision,["registration_json"]=request.ToString(Formatting.None)});
            if(!ReviewCurrent(epoch)) return;HideCadOverlay();_trackingFromScan=null;_cadAlignment=(JObject)result["alignment"];ShowCadPage();
        }
        private static void SameDocument(DocumentState actual,DocumentState expected)
        {if(actual==null||expected==null||actual.DocumentId!=expected.DocumentId||actual.Revision!=expected.Revision) throw new InvalidOperationException("CAD document or revision changed.");}
        private double[] CadMatrix(DocumentState current)
        {
            if(_cadAlignment==null||_cadAlignment.Value<string>("scan_id")!=_scanReview.SessionId||_cadAlignment.Value<int>("solve_revision")!=_scanReview.Revision||
                _cadAlignment.Value<int>("review_version")!=_scanReview.Annotations.Value<int>("version")) throw new InvalidOperationException("Refresh the scan registration.");
            SameDocument(current,new DocumentState(_cadAlignment.Value<string>("document_id"),_cadAlignment.Value<string>("cad_revision"),null));
            var matrix=_cadAlignment["scan_metric_to_inventor_mm"].ToObject<double[]>();ScanRegistration.Validate(matrix,-1);return matrix;
        }
        private void CadHint(string hint) {Panel.ShowMessage("Riferimento Inventor",hint);Panel.SetActions(("Riferimento / overlay",ShowCadPage),("Vista scansione",ShowReview));}
        private void PromptVector(string title,double[] initial,Action<double[]> accept)
        {
            var values=new double[3];int epoch=_reviewEpoch;
            void Component(int i)
            {
                Panel.PromptText(title+" · "+new[]{"X","Y","Z"}[i],"Coordinate finite. Nessun cambio automatico di scala.",initial[i].ToString(CultureInfo.InvariantCulture),text=>
                {
                    if(!ReviewCurrent(epoch)) return;
                    if(!double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!ScanGeometry.Finite(value)||Math.Abs(value)>1e6) {CadHint("Coordinata non valida.");return;}
                    values[i]=value;if(i<2) Component(i+1);else accept(values);
                },ShowCadPage);
            }
            Component(0);
        }
        private static Matrix4x4 UnityMatrix(double[] rowMajor)
        {var m=new Matrix4x4();for(int r=0;r<4;r++) for(int c=0;c<4;c++) m[r,c]=(float)rowMajor[r*4+c];return m;}
        private Capture.CaptureCadOverlayView OverlayView()
        {if(_cadOverlay==null) {var go=new GameObject("Reality Capture · CAD overlay");go.transform.SetParent(transform,false);_cadOverlay=go.AddComponent<Capture.CaptureCadOverlayView>();}return _cadOverlay;}
        private async Task CompareCadAsync()
        {
            HideCadOverlay();_scanView.gameObject.SetActive(true);
            int epoch=_reviewEpoch;var current=await CurrentCadAsync();if(!ReviewCurrent(epoch)) return;
            var context=CadContext();SameDocument(context.Scene?.Graph.State,current);var matrix=CadMatrix(current);
            var pose=_scanView.ScanMetresToWorld*UnityMatrix(ScanRegistration.Multiply(ScanRegistration.InMetres(ScanRegistration.Inverse(matrix)),ScanRegistration.ScanToCadBasis));
            _physicalOverlay=false;OverlayView().Show(context.Scene,pose);ShowCadPage();
        }
        private void BeginPhysicalRegistration()
        {
            HideCadOverlay();_trackingFromScan=null;_physicalPoints.Clear();
            if(_scanView.Selected.Count!=3) {CadHint("Seleziona tre punti non allineati da riconoscere fisicamente nell’ordine.");return;}
            _physicalIds=_scanView.Selected.ToArray();_registrationEpoch=OVRPlugin.GetLocalTrackingSpaceRecenterCount();ShowPhysicalStep();
        }
        private void ShowPhysicalStep()
        {
            Panel.ShowMessage("Registrazione fisica · "+(_physicalPoints.Count+1)+"/3",_physicalIds[_physicalPoints.Count]+"\nPorta l’origine del controller dominante sul riferimento fisico corrispondente, poi premi Acquisisci. L’origine del controller non è una punta calibrata. Accuratezza e offset richiedono collaudo indipendente.");
            Panel.SetActions(("Acquisisci posizione",CapturePhysicalPoint),("Annulla",()=>{_physicalIds=null;_physicalPoints.Clear();ShowCadPage();}));
        }
        private void CapturePhysicalPoint()
        {
            if(!Capture.QuestCameraSource.TrackingValid()||_reviewRay?.Origin==null||!OVRInput.GetControllerPositionTracked(_reviewRay.Controller)||_registrationEpoch!=OVRPlugin.GetLocalTrackingSpaceRecenterCount())
            {InvalidatePhysicalRegistration();CadHint("Tracking o origine cambiati. Ripeti i tre riferimenti.");return;}
            Vector3 world=_reviewRay.Origin.position;var point=_tracking==null?world:_tracking.InverseTransformPoint(world);
            _physicalPoints.Add(new[]{(double)point.x*1000,point.y*1000d,point.z*1000d});
            if(_physicalPoints.Count<3) {ShowPhysicalStep();return;}
            try {_trackingFromScan=ScanRegistration.ThreePoints(_physicalIds.Select(id=>_scanReview.Points.First(p=>p.Id==id).Position).ToArray(),_physicalPoints.ToArray(),false,5);_physicalIds=null;ShowCadPage();}
            catch(ArgumentException) {InvalidatePhysicalRegistration();CadHint("Riferimenti degeneri o distanze incompatibili (oltre 5 mm). Nessuna scala è stata adattata.");}
        }
        private void InvalidatePhysicalRegistration() {HideCadOverlay();_trackingFromScan=null;_physicalIds=null;_physicalPoints.Clear();}
        public bool ValidatePhysicalTracking(bool tracked,int epoch)
        {
            if(_trackingFromScan==null&&_physicalIds==null) return true;
            if(tracked&&epoch==_registrationEpoch) return true;
            InvalidatePhysicalRegistration();return false;
        }
        private async Task StartPhysicalOverlayAsync()
        {
            if(_physicalOverlay) {HideCadOverlay();_scanView.gameObject.SetActive(true);ShowCadPage();return;}
            if(_trackingFromScan==null||!Capture.QuestCameraSource.TrackingValid()||_registrationEpoch!=OVRPlugin.GetLocalTrackingSpaceRecenterCount()) {CadHint("Prima registra tre nuovi riferimenti fisici nella sessione corrente.");return;}
            HideCadOverlay();_scanView.gameObject.SetActive(true);
            int epoch=_reviewEpoch;var current=await CurrentCadAsync();if(!ReviewCurrent(epoch)) return;
            if(_trackingFromScan==null||!ValidatePhysicalTracking(Capture.QuestCameraSource.TrackingValid(),OVRPlugin.GetLocalTrackingSpaceRecenterCount())) {CadHint("Tracking o origine cambiati durante la verifica del CAD. Ripeti i riferimenti.");return;}
            var context=CadContext();SameDocument(context.Scene?.Graph.State,current);var matrix=CadMatrix(current);
            var trackingWorld=_tracking==null?Matrix4x4.identity:_tracking.localToWorldMatrix;
            if((trackingWorld.lossyScale-Vector3.one).sqrMagnitude>1e-6f||Mathf.Abs(trackingWorld.determinant-1)>1e-5f) throw new InvalidOperationException("Tracking scale must be 1:1 with no reflection.");
            var pose=trackingWorld*UnityMatrix(ScanRegistration.Multiply(ScanRegistration.Multiply(ScanRegistration.InMetres(_trackingFromScan),ScanRegistration.InMetres(ScanRegistration.Inverse(matrix))),ScanRegistration.ScanToCadBasis));
            OverlayView().Show(context.Scene,pose);_physicalOverlay=true;_scanView.gameObject.SetActive(false);ShowCadPage();
        }
        private void CheckCadOverlayValidity()
        {
            if(!Active) return;
            if(_trackingFromScan!=null||_physicalIds!=null)
            {bool visible=_physicalOverlay||_physicalIds!=null;if(!ValidatePhysicalTracking(Capture.QuestCameraSource.TrackingValid(),OVRPlugin.GetLocalTrackingSpaceRecenterCount())) {if(_scanView!=null) _scanView.gameObject.SetActive(true);if(visible&&!_reviewBusy) CadHint("Tracking perso o origine cambiata. Ripeti i tre riferimenti fisici.");}}
            if(_cadOverlay==null||!_cadOverlay.gameObject.activeSelf) return;
            var context=_cadContext?.Invoke();
            bool invalid=context==null||_reviewClient==null||!_reviewClient.Matches(context.Server)||context.Document==null||context.Document.DocumentId!=_cadOverlay.DocumentId||context.Document.Revision!=_cadOverlay.Revision||context.Document.VisualRevision!=_cadOverlay.VisualRevision;
            if(_physicalOverlay) invalid|=!Capture.QuestCameraSource.TrackingValid()||_registrationEpoch!=OVRPlugin.GetLocalTrackingSpaceRecenterCount();
            if(invalid) {InvalidatePhysicalRegistration();if(_scanView!=null) _scanView.gameObject.SetActive(true);if(!_reviewBusy) CadHint("Overlay rimosso: tracking, PC o revisione CAD sono cambiati. Verifica e registra nuovamente i riferimenti.");}
        }
        private async Task NewReferenceAsync()
        {
            if(_referenceCommitUnconfirmed) throw new InvalidOperationException("Inspect the unconfirmed native commit before creating another reference.");
            HideCadOverlay();int epoch=_reviewEpoch;CadContext();
            if(_cadAlignment!=null) {CadMatrix(await CurrentCadAsync());if(!ReviewCurrent(epoch)) return;}
            var created=await CadTool("inventor_new_document_safe",new JObject {["name"]="Reality_"+_scanReview.SessionId.Substring(0,8)+"_"+Guid.NewGuid().ToString("N").Substring(0,8),["kind"]="part"});
            if(!ReviewCurrent(epoch)) return;
            if(created.Value<bool?>("workspace_managed")!=true) throw new FormatException("Managed reference document missing.");
            _referenceDestination=new DocumentState(created.Value<string>("document_id"),created.Value<string>("revision"),null);
            if(_cadAlignment==null)
            {
                var request=RegistrationRequest(_referenceDestination,"scan_basis",Array.Empty<string>());
                var aligned=await Control("align_to_cad",new JObject {["revision"]=_scanReview.Revision,["registration_json"]=request.ToString(Formatting.None)});
                if(!ReviewCurrent(epoch)) return;_cadAlignment=(JObject)aligned["alignment"];
            }
            var prepared=await Control("create_reference",new JObject {["revision"]=_scanReview.Revision,["alignment_version"]=_cadAlignment.Value<int>("version")});
            if(!ReviewCurrent(epoch)) return;SameDocument(await CurrentCadAsync(),_referenceDestination);
            var plan=await CadTool("inventor_plan_change",new JObject {["document_id"]=_referenceDestination.DocumentId,["expected_revision"]=_referenceDestination.Revision,["operations"]=prepared["operations"],
                ["validate"]=new JArray("rebuild","feature_health"),["intent"]="Reality Capture reference; estimated geometry; no B-Rep",["include_preview_mesh"]=false});
            if(!ReviewCurrent(epoch)) return;
            if(plan["preview"]?.Value<string>("status")!="preview_rolled_back"||plan.Value<string>("document_id")!=_referenceDestination.DocumentId||plan.Value<string>("document_revision")!=_referenceDestination.Revision||
                plan["preview"]?.Value<string>("document_id")!=_referenceDestination.DocumentId||plan["preview"]?.Value<string>("revision")!=_referenceDestination.Revision||
                !(plan["preview"]?["validated"] is JArray validated)||!validated.Values<string>().Contains("rebuild")||!validated.Values<string>().Contains("feature_health")||
                string.IsNullOrWhiteSpace(plan.Value<string>("plan_id"))||!JToken.DeepEquals(plan["operations"],prepared["operations"])||!DateTimeOffset.TryParse(plan.Value<string>("expires_utc"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _referenceExpires)) throw new FormatException("Native preview not confirmed.");
            _referencePlan=plan;
            Panel.ShowMessage("Anteprima riferimento",$"Parte vuota creata nel workspace: {created["path"]}\nAnteprima nativa eseguita e annullata; ricompilazione del modello e salute delle feature verificate. {_scanReview.Points.Length} punti, {_scanReview.Annotations["primitives"].Count()} primitive.\nApplica crea geometria di riferimento stimata e quote note distinte. Nessuna B-Rep. Annulla lascia la parte vuota già creata; nessun dato della scansione viene perso.");
            Panel.SetActions(("Applica riferimento",()=>ReviewAction(CommitReferenceAsync)),("Annulla anteprima",()=>{_referencePlan=null;ShowCadPage();}));
        }
        private async Task CommitReferenceAsync()
        {
            if(_referencePlan==null||DateTimeOffset.UtcNow>=_referenceExpires) throw new InvalidOperationException("Preview expired.");
            int epoch=_reviewEpoch;SameDocument(await CurrentCadAsync(),_referenceDestination);if(!ReviewCurrent(epoch)) return;
            var plan=_referencePlan;_referencePlan=null; // An uncertain commit must never be retried automatically.
            _referenceCommitUnconfirmed=true;
            JObject result;
            try {result=await CadTool("inventor_commit_plan",new JObject {["plan_id"]=plan["plan_id"],["document_id"]=_referenceDestination.DocumentId,["expected_revision"]=_referenceDestination.Revision});}
            catch(InventorXrSo.Core.Mcp.McpToolException error) when(new[]{"ROLLED_BACK","STALE_REVISION","DOCUMENT_CHANGED","PLAN_NOT_FOUND","PLAN_MISMATCH","READ_ONLY","EXPERIMENTAL_DISABLED","ATOMIC_REQUIRED","WRONG_DOCUMENT_TYPE","NO_TARGET","NO_DOCUMENT"}.Contains(error.Code))
            {_referenceCommitUnconfirmed=false;throw;}
            if(!ReviewCurrent(epoch)) return;
            if(result.Value<string>("status")!="committed"||result.Value<string>("plan_id")!=plan.Value<string>("plan_id")||result.Value<string>("document_id")!=_referenceDestination.DocumentId||string.IsNullOrWhiteSpace(result.Value<string>("revision"))) throw new FormatException("Commit unconfirmed; inspect the CAD document.");
            _referenceCommitUnconfirmed=false;
            _referenceDestination=new DocumentState(result.Value<string>("document_id"),result.Value<string>("revision"),null);
            Panel.ShowMessage("Riferimento applicato","Transazione nativa confermata. Punti, work geometry, parametri e provenienza sono nel documento. Salva nel workspace per conservarli sul disco. Le misure stimate rimangono tali.");
            Panel.SetActions(("Salva riferimento",()=>ReviewAction(async()=>{int e=_reviewEpoch;var saved=await CadTool("inventor_save_document_safe",new JObject {["document_id"]=_referenceDestination.DocumentId,["expected_revision"]=_referenceDestination.Revision});if(ReviewCurrent(e)) CadHint("Salvataggio: "+saved["status"]+"\n"+saved["path"]);})),("Riferimento / overlay",ShowCadPage),("Vista scansione",ShowReview));
        }
    }
}
