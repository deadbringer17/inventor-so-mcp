using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Capture;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Capture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Xr
{
    public sealed partial class RealityCaptureWorkspace
    {
        private Func<CaptureReviewClient> _reviewFactory;
        private CaptureReviewClient _reviewClient;
        private CancellationTokenSource _reviewCancel;
        private ScanReview _scanReview;
        private ScanSceneView _scanView;
        private ControllerRay _reviewRay;
        private string _reviewSession;
        private int _reviewEpoch;
        private readonly List<ScanPrimitive> _measurePrimitives=new List<ScanPrimitive>();
        private static string PrimitiveLabel(string kind)=>kind=="point"?"Punto":kind=="axis"?"Asse":kind=="plane"?"Piano":kind=="circle"?"Cerchio":"Cilindro";
        private static string SolveLabel(string state)=>state=="queued"?"in coda":state=="running"?"in corso":state=="succeeded"?"pronta":state=="cancelled"?"annullata":"non riuscita";
#if XR_SO_ACCEPTANCE || UNITY_EDITOR
        internal ScanSceneView OpenSyntheticReviewFixture()
        {
            _scanReview=ScanReviewFixture.Create();
            if(_scanView==null) {var go=new GameObject("RC3 SYNTHETIC scan");go.transform.SetParent(transform,false);_scanView=go.AddComponent<ScanSceneView>();_scanView.SelectionChanged+=ShowReview;}
            _scanView.gameObject.SetActive(true);_scanView.Load(_scanReview,_head.position+_head.forward*.9f-_head.right*.38f,Quaternion.LookRotation(_head.forward));
            Panel.transform.SetPositionAndRotation(_head.position+_head.forward+_head.right*.35f,Quaternion.LookRotation(_head.forward));
            ShowReview();return _scanView;
        }
#endif
        public void BindReview(Func<CaptureReviewClient> factory,ControllerRay ray) {_reviewFactory=factory;_reviewRay=ray;}
        private void CloseReview()
        {
            ClearCadRegistration();
            _reviewEpoch++;_reviewCancel?.Cancel();_reviewCancel?.Dispose();_reviewCancel=null;
            _scanView?.Clear();_scanReview=null;_reviewClient=null;_measurePrimitives.Clear();
        }
        private void OnApplicationPause(bool paused)
        {
            if(paused&&Active&&(_scanReview!=null||_reviewCancel!=null)) ShowOverview();
        }
        private async Task OpenReviewsAsync(string id)
        {
            CloseReview();_reviewClient=_reviewFactory?.Invoke();
            if(_reviewClient==null) {ShowInfo("Revisioni PC","Associa un PC dalle impostazioni prima di aprire una ricostruzione.");return;}
            _reviewSession=id;_reviewCancel=new CancellationTokenSource();
            int epoch=_reviewEpoch;_showingCapture=false;
            try {await ListReviewsAsync();}
            catch(OperationCanceledException) { }
            catch(Exception) {if(ReviewCurrent(epoch)) ShowReviewError();}
        }
        private bool ReviewCurrent(int epoch)=>Active&&_reviewCancel!=null&&!_reviewCancel.IsCancellationRequested&&epoch==_reviewEpoch;
        private async Task ListReviewsAsync()
        {
            int epoch=_reviewEpoch;var result=await _reviewClient.ListAsync(_reviewSession,_reviewCancel.Token);
            if(!ReviewCurrent(epoch)) return;
            if(_scanView!=null) _scanView.gameObject.SetActive(false);
            var actions=new List<(string,Action)>();
            foreach(var solution in result["solutions"] as JArray??new JArray())
            {
                int revision=solution.Value<int>("Revision");string state=solution.Value<string>("State");
                actions.Add(($"r{revision} · {SolveLabel(state)}",()=>ReviewAction(()=>OpenRevisionAsync(revision))));
            }
            actions.Add(("Nuovo solve",()=>ReviewAction(StartSolveAsync)));
            actions.Add(("Aggiorna",()=>ReviewAction(ListReviewsAsync)));
            actions.Add((UiText.RealityCapture,ShowOverview));
            Panel.ShowMessage("Revisioni PC",_reviewSession+"\nRaw e risultati precedenti immutabili. Ogni solve usa una copia dei vincoli correnti. Massimo quattro revisioni per sessione.");
            Panel.SetActions(actions.ToArray());
        }
        private async Task StartSolveAsync()
        {
            int epoch=_reviewEpoch;var constraints=await Control("list_constraints");if(!ReviewCurrent(epoch)) return;
            var result=await Control("start_solve",new JObject {["expected_version"]=constraints.Value<int>("version")});
            if(ReviewCurrent(epoch)) await OpenRevisionAsync(result["solution"].Value<int>("Revision"));
        }
        private async Task OpenRevisionAsync(int revision)
        {
            int epoch=_reviewEpoch;var solution=await _reviewClient.SolutionAsync(_reviewSession,revision,_reviewCancel.Token);
            if(!ReviewCurrent(epoch)) return;
            string state=solution["solution"]?.Value<string>("State");
            if(state!="succeeded")
            {
                if(_scanView!=null) _scanView.gameObject.SetActive(false);
                Panel.ShowMessage($"Solve r{revision}",SolveLabel(state)+"\n"+solution["solution"]?.Value<string>("ErrorCode")+"\nIl PC esegue la ricostruzione locale; i raw restano conservati.");
                Panel.SetActions(("Aggiorna",()=>ReviewAction(()=>OpenRevisionAsync(revision))),
                    ("Annulla solve",()=>ReviewAction(async()=>{await Control("cancel_solve",new JObject {["revision"]=revision});if(ReviewCurrent(epoch)) await ListReviewsAsync();})),
                    ("Revisioni",()=>ReviewAction(ListReviewsAsync)),(UiText.RealityCapture,ShowOverview));return;
            }
            var review=await _reviewClient.OpenAsync(_reviewSession,revision,_reviewCancel.Token);if(!ReviewCurrent(epoch)) return;
            ClearCadRegistration();_scanReview=review;_measurePrimitives.Clear();
            if(_scanView==null)
            {
                var go=new GameObject("Reality Capture · scan only");go.transform.SetParent(transform,false);_scanView=go.AddComponent<ScanSceneView>();
                _scanView.SelectionChanged+=ShowReview;
            }
            var forward=Vector3.ProjectOnPlane(_head.forward,Vector3.up).normalized;if(forward.sqrMagnitude<.001f) forward=Vector3.forward;
            var right=Vector3.Cross(Vector3.up,forward);
            _scanView.gameObject.SetActive(true);_scanView.Load(review,_head.position+forward*.9f-right*.38f,Quaternion.LookRotation(forward));
            Panel.transform.SetPositionAndRotation(_head.position+forward+right*.35f,Quaternion.LookRotation(forward));
            ShowReview();
        }
        private void ShowReview()
        {
            if(!Active||_scanReview==null) return;
            HideCadOverlay();
            _scanView.gameObject.SetActive(true);
            var metric=_scanReview.Quality["metric"];
            string measure="Seleziona i punti col Trigger. Giallo: selezionati; arancio: osservati in sole due immagini. Il colore non certifica copertura o accuratezza.";
            var selected=_scanReview.Points.Where(p=>_scanView.Selected.Contains(p.Id)).ToArray();
            if(selected.Length==2) measure=$"Distanza ricostruita: {ScanGeometry.Distance(selected[0].Position,selected[1].Position):0.###} mm. Incertezza non stimata.";
            if(selected.Length>0&&selected.Length<=3) measure+="\n"+string.Join("\n",selected.Select(p=>p.Id));
            Panel.ShowMessage($"Scansione r{_scanReview.Revision} · {metric?.Value<string>("AccuracyStatus")??"UNSCALED"}",
                $"{_scanReview.Points.Length} punti · {_scanView.Selected.Count} selezionati. Vista ridotta, quote in mm nello spazio scansione.\n"+measure);
            Panel.SetActions(("Selezione / fitting",ShowFitting),("Primitive / misure",()=>ShowPrimitives(0)),
                ("Vincoli noti",()=>ReviewAction(()=>ShowConstraintsAsync(0))),
                ("Qualità e zone deboli",ShowQuality),("Esporta",ShowExports),("Riferimento Inventor / overlay",()=>ReviewAction(OpenCadAsync)),
                ("Revisioni / solve",()=>ReviewAction(ListReviewsAsync)),(UiText.RealityCapture,ShowOverview));
        }
        private void ShowFitting()
        {
            Panel.ShowMessage("Selezione e fitting",$"{_scanView.Selected.Count} punti selezionati. Scegli solo la regione della primitiva. I fitting sono stime; outlier, archi corti o regioni miste possono rendere il supporto insufficiente.\nRMS del fitting ≠ incertezza della misura.");
            var actions=new List<(string,Action)> {("Azzera",()=>_scanView.ClearSelection()),("Tutti i punti",()=>_scanView.SelectAll()),
                ("Regione 100 mm",()=>_scanView.SelectRegion(100)),("Punti a due viste",()=>_scanView.SelectWeak())};
            foreach(string kind in new[]{"point","axis","plane","circle","cylinder"})
            {string k=kind;actions.Add((PrimitiveLabel(kind),()=>ReviewAction(()=>FitAsync(k))));}
            actions.Add(("Vista scansione",ShowReview));Panel.SetActions(actions.ToArray());
        }
        private async Task FitAsync(string kind)
        {
            if(_scanView.Selected.Count==0) {ShowFitting();return;}
            int epoch=_reviewEpoch;var result=await Control("fit_primitive",new JObject { ["revision"]=_scanReview.Revision,["expected_version"]=_scanReview.Annotations.Value<int>("version"),
                ["primitive_id"]="p_"+Guid.NewGuid().ToString("N"),["kind"]=kind,["point_ids_json"]=JsonConvert.SerializeObject(_scanView.Selected) });
            if(!ReviewCurrent(epoch)) return;_scanReview.ApplyAnnotations(result);_scanView.Redraw();ShowPrimitives(0);
        }
        private void ShowPrimitives(int index)
        {
            var primitives=_scanReview.Annotations["primitives"]?.ToObject<ScanPrimitive[]>()??Array.Empty<ScanPrimitive>();
            if(primitives.Length==0) {Panel.ShowMessage("Primitive","Nessuna primitiva. Seleziona una regione e scegli un fitting.");Panel.SetActions(("Selezione / fitting",ShowFitting),("Vista scansione",ShowReview));return;}
            index=Math.Max(0,Math.Min(index,primitives.Length-1));var p=primitives[index];
            string body=$"{index+1}/{primitives.Length} · {PrimitiveLabel(p.Kind)} · {p.Id}\nStima da {p.SupportIds.Length} punti · RMS {p.FitRms:0.###} mm. Incertezza non stimata.\n";
            if(p.ConstraintIds.Length>0) body+="Vincoli della revisione: "+string.Join(", ",p.ConstraintIds)+"\n";
            if(p.Kind=="circle"||p.Kind=="cylinder") body+=$"Raggio {p.Radius:0.###} mm · diametro {2*p.Radius:0.###} mm.\n";
            if(p.Kind=="cylinder"||p.Kind=="axis") body+=$"Estensione assiale osservata {p.Length:0.###} mm.\n";
            if(p.Kind=="plane"&&_scanView.Selected.Count==1) body+=$"Distanza punto-piano {ScanGeometry.PointPlane(_scanReview.Points.First(s=>s.Id==_scanView.Selected[0]).Position,p):0.###} mm.\n";
            if(_measurePrimitives.Count==2)
            {
                body+=$"Angolo acuto riferimenti: {ScanGeometry.AngleDegrees(_measurePrimitives[0],_measurePrimitives[1]):0.###}°.\n";
                try {body+=$"Separazione all’origine (normali entro 1°): {ScanGeometry.ParallelPlanes(_measurePrimitives[0],_measurePrimitives[1]):0.###} mm.\n";}catch(ArgumentException) { }
            }
            Panel.ShowMessage("Primitive e misure",body);
            var actions=new List<(string,Action)> {("Riferimento misura",()=>{if(p.Kind!="point") {if(_measurePrimitives.Count==2) _measurePrimitives.Clear();if(!_measurePrimitives.Any(s=>s.Id==p.Id)) _measurePrimitives.Add(p);}ShowPrimitives(index);}),
                ("Evidenzia supporto",()=>_scanView.SelectIds(p.SupportIds)),
                ("Elimina annotazione",()=>ReviewAction(async()=>{int epoch=_reviewEpoch;var r=await Control("remove_primitive",new JObject {["revision"]=_scanReview.Revision,["primitive_id"]=p.Id,["expected_version"]=_scanReview.Annotations.Value<int>("version")});if(ReviewCurrent(epoch)) {_scanReview.ApplyAnnotations(r);_measurePrimitives.Clear();_scanView.Redraw();ShowPrimitives(0);}}))};
            if(index>0) actions.Add(("Precedente",()=>ShowPrimitives(index-1)));if(index+1<primitives.Length) actions.Add(("Successiva",()=>ShowPrimitives(index+1)));
            actions.Add(("Vista scansione",ShowReview));Panel.SetActions(actions.ToArray());
        }
        private void ShowQuality()
        {
            var q=_scanReview.Quality;var metric=q["metric"];
            string text=$"{q.Value<int>("frames_used")}/{q.Value<int>("frames_total")} immagini · {q.Value<int>("triangles")} triangoli sparsi.\n"+
                $"RMS calibrazione: {metric?["CalibrationRmsMm"]??JValue.CreateNull()} mm. RMS/P95 validazione: {metric?["ValidationRmsMm"]??JValue.CreateNull()} / {metric?["ValidationP95Mm"]??JValue.CreateNull()} mm.\n"+
                $"Punti osservati in due immagini: {_scanReview.Points.Count(p=>p.Observations<=2)}. Copertura geometrica e incertezza non stimate.\n"+
                "Quote note usate nel solve: "+string.Join("; ",(_scanReview.Constraints as JArray??new JArray()).Take(8).Select(c=>$"{c["Id"]}: {c["ValueMm"]} mm ({c["Role"]})"))+"\n"+
                "VALIDATED richiede controlli fisici indipendenti. Le zone lucide, prive di texture e occluse richiedono il collaudo reale.";
            Panel.ShowMessage("Qualità ricostruzione",text);
            Panel.SetActions(("Residui vincoli",()=>ShowResiduals(0)),("Evidenzia punti a due viste",()=>_scanView.SelectWeak()),
                (_scanView.SurfaceVisible?"Solo punti":"Mostra superficie",()=>{_scanView.SurfaceVisible=!_scanView.SurfaceVisible;_scanView.Redraw();ShowQuality();}),
                ("Ruota 45°",()=>_scanView.transform.Rotate(Vector3.up,45,Space.World)),("Vista scansione",ShowReview));
        }
        private void ShowResiduals(int index)
        {
            var list=_scanReview.Quality["metric"]?["Constraints"] as JArray??new JArray();
            if(list.Count==0) {Panel.ShowMessage("Residui vincoli","Nessun vincolo nella revisione caricata.");Panel.SetActions(("Qualità",ShowQuality),("Vista scansione",ShowReview));return;}
            index=Math.Max(0,Math.Min(index,list.Count-1));var c=list[index];
            var frozen=(_scanReview.Constraints as JArray??new JArray()).FirstOrDefault(p=>p.Value<string>("Id")==c.Value<string>("Id"));
            string measured=c["MeasuredMm"]?.Type==JTokenType.Null?"non disponibile":c.Value<string>("MeasuredMm")+" mm";
            Panel.ShowMessage("Residui · r"+_scanReview.Revision,$"{index+1}/{list.Count} · {c["Id"]} · {c["Role"]} · {c["Status"]}\n"+
                $"Quota nota: {c["KnownMm"]} mm ± {c["UncertaintyMm"]} mm.\nMisura ricostruita: {measured}.\n"+
                $"Residuo: {c["ResidualMm"]} mm; errore assoluto: {c["AbsoluteErrorMm"]} mm; relativo: {c["RelativeError"]}.\nQuesta incertezza appartiene alla quota esterna. L’incertezza della ricostruzione non è stimata.");
            var actions=new List<(string,Action)>();
            if(frozen!=null) actions.Add(("Evidenzia riferimenti",()=>_scanView.SelectIds((frozen["PointIds"] as JArray??new JArray()).Values<string>())));
            if(index>0) actions.Add(("Precedente",()=>ShowResiduals(index-1)));if(index+1<list.Count) actions.Add(("Successivo",()=>ShowResiduals(index+1)));
            actions.Add(("Qualità",ShowQuality));actions.Add(("Vista scansione",ShowReview));Panel.SetActions(actions.ToArray());
        }
        private async Task ShowConstraintsAsync(int index)
        {
            int epoch=_reviewEpoch;var current=await Control("list_constraints");if(!ReviewCurrent(epoch)) return;
            var list=current["constraints"] as JArray??new JArray();int version=current.Value<int>("version");
            index=Math.Max(0,Math.Min(index,list.Count-1));var c=list.Count>0?(JObject)list[index]:null;
            Panel.ShowMessage("Quote note · vincoli correnti",c==null?"Nessun vincolo. Seleziona due punti per una distanza o tre punti sul bordo per un diametro. Ogni quota richiede la propria incertezza.":
                $"{index+1}/{list.Count} · {c["Id"]}\nQuota nota: {c["ValueMm"]} mm ± {c["UncertaintyMm"]} mm. {c["Type"]} · {c["Role"]} · attivo {c["Enabled"]}\nRiferimenti: {string.Join(", ",(c["PointIds"] as JArray??new JArray()).Values<string>())}\nLe modifiche valgono per il prossimo solve; r{_scanReview.Revision} conserva i propri vincoli.");
            var actions=new List<(string,Action)> {("Nuova calibrazione",()=>PromptConstraint(null,version,false)),("Nuova quota di controllo",()=>PromptConstraint(null,version,true))};
            if(c!=null)
            {
                actions.Add(("Evidenzia riferimenti",()=>_scanView.SelectIds((c["PointIds"] as JArray??new JArray()).Values<string>())));
                actions.Add(("Modifica quota",()=>PromptConstraint(c,version,c.Value<string>("Role")=="validation")));
                actions.Add((c.Value<bool>("Enabled")?"Disattiva":"Attiva",()=>ReviewAction(async()=>{c["Enabled"]=!c.Value<bool>("Enabled");await SaveConstraint(c,version);if(ReviewCurrent(epoch)) await ShowConstraintsAsync(index);} )));
                actions.Add(("Rimuovi",()=>ReviewAction(async()=>{await Control("remove_constraint",new JObject {["constraint_id"]=c["Id"],["expected_version"]=version});if(ReviewCurrent(epoch)) await ShowConstraintsAsync(0);} )));
            }
            if(index>0) actions.Add(("Precedente",()=>ReviewAction(()=>ShowConstraintsAsync(index-1))));if(index+1<list.Count) actions.Add(("Successiva",()=>ReviewAction(()=>ShowConstraintsAsync(index+1))));
            actions.Add(("Nuovo solve",()=>ReviewAction(StartSolveAsync)));actions.Add(("Vista scansione",ShowReview));Panel.SetActions(actions.ToArray());
        }
        private void PromptConstraint(JObject existing,int version,bool validation)
        {
            string[] selected=_scanView.Selected.ToArray();
            if(existing==null&&selected.Length!=2&&selected.Length!=3) {Panel.ShowMessage("Quota nota","Seleziona due punti per distanza, tre per diametro.");Panel.SetActions(("Vista scansione",ShowReview));return;}
            Panel.PromptText("Quota nota (mm)","Inserisci una misura esterna, non il valore ricostruito.",existing?.Value<string>("ValueMm")??"",value=>
            {
                if(!Positive(value,out double mm)) {ShowReview();return;}
                Panel.PromptText("Incertezza quota (mm)","Incertezza della misura esterna (almeno 0,001 mm).",existing?.Value<string>("UncertaintyMm")??"",uncertainty=>
                {
                    if(!Positive(uncertainty,out double u)||u<.001||u>mm) {ShowReview();return;}
                    var constraint=existing==null?new JObject {["Id"]="d_"+Guid.NewGuid().ToString("N"),["Type"]=selected.Length==2?"distance":"diameter",["PointIds"]=JArray.FromObject(selected),
                        ["Role"]=validation?"validation":"calibration",["Enabled"]=true,["Independent"]=false,["Source"]="manual",["MeasurementId"]="m_"+Guid.NewGuid().ToString("N")}: (JObject)existing.DeepClone();
                    constraint["ValueMm"]=mm;constraint["UncertaintyMm"]=u;
                    ReviewAction(async()=>{int epoch=_reviewEpoch;await SaveConstraint(constraint,version);if(ReviewCurrent(epoch)) await ShowConstraintsAsync(0);});
                },ShowReview);
            },ShowReview);
        }
        private static bool Positive(string value,out double number)=>double.TryParse(value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&ScanGeometry.Finite(number)&&number>0&&number<=1e6;
        private Task<JObject> SaveConstraint(JObject c,int version)=>Control("add_constraint",new JObject {["constraint_json"]=c.ToString(Formatting.None),["expected_version"]=version});
        private void ShowExports()
        {
            Panel.ShowMessage("Esporta scansione","GLB in metri; PLY e OBJ in mm. Ogni esportazione comprende units.json e la revisione. File salvati sul visore nella cartella RealityCapture/exports; geometria stimata, senza certificazione dimensionale.");
            Panel.SetActions(("GLB · metri",()=>ReviewAction(()=>ExportReviewAsync("glb"))),("PLY · mm",()=>ReviewAction(()=>ExportReviewAsync("ply"))),
                ("OBJ · mm",()=>ReviewAction(()=>ExportReviewAsync("obj"))),("Vista scansione",ShowReview));
        }
        private async Task ExportReviewAsync(string format)
        {
            int epoch=_reviewEpoch;var review=_scanReview;var bytes=await _reviewClient.ExportAsync(review.SessionId,review.Revision,format,_reviewCancel.Token);
            if(!ReviewCurrent(epoch)) return;
            string parent=Path.Combine(Application.persistentDataPath,"RealityCapture","exports");
            string root=await Task.Run(()=>ScanExportWriter.WriteNew(parent,review,format,bytes));
            if(ReviewCurrent(epoch)) {Panel.ShowMessage("Esportazione completata","Scansione r"+review.Revision+" · "+format.ToUpperInvariant()+" con unità e qualità.\n"+root);Panel.SetActions(("Vista scansione",ShowReview));}
        }
        private Task<JObject> Control(string operation,JObject args=null)
        {args=args??new JObject();args["session_id"]=_reviewSession;return _reviewClient.ControlAsync(operation,args,_reviewCancel.Token);}
        private bool _reviewBusy;
        private async void ReviewAction(Func<Task> action)
        {
            if(_reviewBusy||_reviewCancel==null) return;int epoch=_reviewEpoch;_reviewBusy=true;
            try {await action();}
            catch(OperationCanceledException) { }
            catch(Exception) {if(ReviewCurrent(epoch)) ShowReviewError();}
            finally {_reviewBusy=false;}
        }
        private void ShowReviewError()
        {
            if(_referenceCommitUnconfirmed)
            {
                Panel.ShowMessage("Applicazione non confermata","La risposta del PC non conferma l’esito. Il comando potrebbe essere stato eseguito: controlla la parte attiva in Inventor prima di ripetere l’importazione. Nessun nuovo tentativo o salvataggio automatico è stato effettuato. La scansione resta conservata.");
                Panel.SetActions(("Vista scansione",ShowReview),(UiText.RealityCapture,ShowOverview));return;
            }
            Panel.ShowMessage("Revisione non completata","Verifica il collegamento al PC, la versione dei vincoli e il supporto selezionato. La scansione caricata resta disponibile.");
            Panel.SetActions(("Riprova elenco",()=>ReviewAction(ListReviewsAsync)),("Vista scansione",()=>{if(_scanReview!=null) ShowReview();else ShowOverview();}),(UiText.RealityCapture,ShowOverview));
        }
        /// <summary>Same guarded path for physical input and explicitly labelled synthetic acceptance frames.</summary>
        public bool ProcessReviewRay(Ray ray,bool tracked,bool trigger,bool grip,bool overUi)
        {
            if(!Active||_scanReview==null||_scanView==null||!_scanView.gameObject.activeInHierarchy||_reviewBusy||!tracked||!trigger||grip||overUi) return false;
            return _scanView.Pick(ray);
        }
        private void LateUpdate()
        {
            CheckCadOverlayValidity();
            if(!Active||_scanReview==null||_scanView==null||!_scanView.gameObject.activeInHierarchy||_reviewBusy||_reviewRay?.Origin==null) return;
            var controller=_reviewRay.Controller;
            bool overUi=EventSystem.current!=null&&EventSystem.current.currentInputModule is ControllerUiInputModule ui&&ui.CurrentHit.isValid;
            ProcessReviewRay(new Ray(_reviewRay.Origin.position,_reviewRay.Origin.forward),OVRInput.IsControllerConnected(controller)&&OVRInput.GetControllerOrientationTracked(controller),
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger,controller),OVRInput.Get(OVRInput.Button.PrimaryHandTrigger,controller),overUi);
        }
    }
}
