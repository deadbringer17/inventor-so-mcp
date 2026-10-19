using System;
using System.Collections.Generic;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        public const string SplineInputTab = SketchCommandCatalog.TabPrefix+"Punti spline";
        private List<CadPoint> _splinePoints;
        private NativeSketchSnapshot _splineSnapshot;
        private string _splineVariant, _splineLabel;
        private int _splineFitMethod;
        private bool SplineCurrent => _splinePoints!=null && !_disposed && _ready() && Session.CanEdit
            && ReferenceEquals(_splineSnapshot,Session.Snapshot);
        private bool SplineActionsReady => SplineCurrent && !(_palette()?.KeypadVisible ?? _prompt?.Editing ?? false);

        private IEnumerable<XrAction> SplineActions()
        {
            if(_splinePoints!=null && _splineVariant=="spline_fit")
                foreach(var method in new[] {(26369,"Norme"),(26370,"Energia minima"),(26371,"AutoCAD")})
                {
                    int value=method.Item1;string label=method.Item2;
                    yield return new XrAction("sketch.spline.method."+value,"Metodo: "+label,SplineInputTab,
                        ()=>SplineActionsReady && _splineVariant=="spline_fit",
                        ()=>{_splineFitMethod=value;_notice("Metodo spline: "+label+". Punti conservati; concludi per preparare la bozza.");_changed();},
                        ()=>"Completa o annulla il campo numerico prima di scegliere il metodo.",
                        kind:XrActionKind.Toggle,isOn:()=>SplineCurrent && _splineVariant=="spline_fit" && _splineFitMethod==value);
                }
            yield return new XrAction("sketch.spline.finish","Concludi spline",SplineInputTab,
                ()=>SplineActionsReady && _splinePoints.Count>=2,FinishSpline,()=>"Raccogli almeno due punti e completa il campo numerico.");
            yield return new XrAction("sketch.spline.undo","Rimuovi ultimo punto",SplineInputTab,
                ()=>SplineActionsReady && _splinePoints.Count>0,()=>{_splinePoints.RemoveAt(_splinePoints.Count-1);ArmSplinePoint();},()=>"Nessun punto da rimuovere.");
            yield return new XrAction("sketch.spline.numeric","Punto tramite X/Y",SplineInputTab,
                ()=>SplineActionsReady && _splinePoints.Count<64,PromptSplinePoint,()=>"Completa il campo corrente; massimo 64 punti.");
            yield return new XrAction("sketch.spline.cancel","Annulla spline",SplineInputTab,
                ()=>SplineActionsReady,()=>{CancelPrompt();_notice("Spline annullata.");_changed();},()=>"Completa o annulla il campo numerico.");
        }
        private void StartSpline(string variant,string label)
        {
            if(!CanCreate || !Session.Snapshot.SupportsVariant(variant)) return;
            CancelCandidates();++_promptGeneration;
            _splinePoints=new List<CadPoint>();_splineSnapshot=Session.Snapshot;_splineVariant=variant;_splineLabel=label;
            _splineFitMethod=variant=="spline_fit" ? _splineSnapshot.SplineFitMethod.Value : 0;
            _palette()?.ShowTabGroup(new[] {SplineInputTab},true);ArmSplinePoint();
        }
        private void ArmSplinePoint()
        {
            if(!SplineCurrent) return;
            ArmCoordinatePick(_splineSnapshot,_promptGeneration,AcceptSplinePoint);
            _notice(_splineLabel+": "+_splinePoints.Count+" punti. Trigger aggiunge un punto; Concludi spline prepara la bozza. Massimo 64 punti.");
            _changed();
        }
        private void AcceptSplinePoint(CadPoint point)
        {
            if(!SplineCurrent) return;
            if(_splinePoints.Count>=64) {_notice("Raggiunto il limite di 64 punti: concludi la spline oppure rimuovi l'ultimo punto.");return;}
            CloseCoordinateField();_splinePoints.Add(point);ArmSplinePoint();
        }
        private void PromptSplinePoint()
        {
            if(!SplineActionsReady || _splinePoints.Count>=64) return;
            CloseCoordinateField();int generation=_promptGeneration;var snapshot=_splineSnapshot;double x=0;
            void Field(bool y)
            {
                var entry=new NumericEntry("sketch.spline."+(y ? "y" : "x"),QuantityUnit.Millimeters,0,-100000,100000);
                _prompt=entry;
                entry.Committed+=()=>
                {
                    if(!SplineCurrent || generation!=_promptGeneration || !ReferenceEquals(snapshot,_splineSnapshot) || _prompt!=entry
                        || (_palette()!=null && !_palette().KeypadVisible)) return;
                    if(!y) {x=entry.Value;Field(true);} else AcceptSplinePoint(new CadPoint(x,entry.Value));
                };
                if(_palette()!=null) _palette().ShowKeypad(entry,_splineLabel+" • punto "+(_splinePoints.Count+1)+" "+(y ? "Y" : "X"));
                else entry.BeginEdit();
                ArmCoordinatePick(snapshot,generation,AcceptSplinePoint);_changed();
            }
            Field(false);
        }
        private void FinishSpline()
        {
            if(!SplineActionsReady || _splinePoints.Count<2) return;
            var points=new JArray();foreach(var point in _splinePoints) points.Add(new JArray(point.X,point.Y));
            string variant=_splineVariant;int method=_splineFitMethod;CancelPrompt();
            var options=new JObject {["points_mm"]=points};
            if(variant=="spline_fit") options["fit_method"]=method;
            try {Session.SetOperation(variant,options);_notice("Spline pronta: Anteprima, poi Applica.");}
            catch(Exception ex) {_notice(ex.Message);}
            _changed();
        }
    }
}
