using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool _splineClosed;
        private bool SplineCurrent => _splinePoints!=null && !_disposed && _ready() && Session.CanEdit
            && ReferenceEquals(_splineSnapshot,Session.Snapshot);
        private bool SplineActionsReady => SplineCurrent && !(_palette()?.KeypadVisible ?? _prompt?.Editing ?? false);

        private IEnumerable<XrAction> SplineActions()
        {
            var snapshot = Session.Snapshot;
            yield return new XrAction("sketch.spline.point", "Modifica punto spline", SketchCommandCatalog.TabPrefix+"Modifica",
                () => ReferenceEquals(snapshot, Session.Snapshot) && SplinePointSelection(), PromptSplinePointEdit,
                () => "Seleziona una spline intera e un punto nativo; Anteprima ne verifica l'appartenenza alla spline.");
            yield return new XrAction("sketch.spline.tension", "Modifica tensione spline", SketchCommandCatalog.TabPrefix+"Modifica",
                () => ReferenceEquals(snapshot, Session.Snapshot) && SplineTensionSelection(), PromptSplineTension,
                () => "Seleziona una sola spline interpolata intera nello schizzo aggiornato; il backend deve supportarne la tensione.");
            yield return new XrAction("sketch.spline.closed","Spline chiusa",SplineInputTab,
                ()=>SplineActionsReady,
                ()=>{_splineClosed=!_splineClosed;_notice(_splineClosed ? "Spline chiusa: punti conservati." : "Spline aperta: punti conservati.");_changed();},
                ()=>"Completa o annulla il campo numerico prima di cambiare la chiusura.",
                kind:XrActionKind.Toggle,isOn:()=>SplineCurrent && _splineClosed);
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
        private bool SplinePointSelection(bool reconfirming = false) => CanSelect && (!Session.HasDraft || reconfirming)
            && NativeSketchSplinePointRules.TrySelection(Session.Snapshot, Selected, out _, out _);

        private void PromptSplinePointEdit()
        {
            if (!SplinePointSelection() || !NativeSketchSplinePointRules.TrySelection(Session.Snapshot, Selected, out var curve, out var point)) return;
            var snapshot = Session.Snapshot;
            snapshot.Entities.Single(e => e.Reference?.EntityId == point.EntityId).TryGetSubpartPoint(SketchSubpart.Point, out var position);
            CancelCandidates(); int generation = ++_promptGeneration;
            var values = new[] { position.X, position.Y }; int index = 0;
            bool Current() => !_disposed && generation == _promptGeneration && _ready() && Session.CanEdit
                && ReferenceEquals(snapshot, Session.Snapshot);
            void Finish(CadPoint destination)
            {
                if (!Current()) return;
                CloseCoordinateField();
                Session.SetOperation(NativeSketchSplinePointRules.Variant, new JObject {
                    ["references"] = new JArray(curve.ToJson(), point.ToJson()),
                    ["position_mm"] = new JArray(destination.X, destination.Y) });
                _notice("Punto spline pronto: Anteprima verifica l'appartenenza nativa, poi Applica."); _changed();
            }
            void Field()
            {
                var entry = new NumericEntry("sketch.spline.point."+(index == 0 ? "x" : "y"), QuantityUnit.Millimeters, values[index], -100000, 100000);
                _prompt = entry;
                entry.Committed += () =>
                {
                    if (!Current() || _prompt != entry) return;
                    values[index] = entry.Value;
                    if (++index == 1) Field(); else Finish(new CadPoint(values[0], values[1]));
                };
                if (_palette() != null) _palette().ShowKeypad(entry, "Punto spline • destinazione "+(index == 0 ? "X" : "Y")+" mm"); else entry.BeginEdit();
                ArmCoordinatePick(snapshot, generation, Finish);
                _notice("Indica la destinazione sul foglio con Trigger oppure inserisci X/Y. Anteprima verifica che il punto appartenga alla spline.");
                _changed();
            }
            Field();
        }
        private bool SplineTensionSelection(bool reconfirming = false) => CanSelect && (!Session.HasDraft || reconfirming)
            && Selected.Count == 1 && NativeSketchSplineTensionRules.TryGetTension(Session.Snapshot, Selected[0], out _);

        private void PromptSplineTension()
        {
            if (!SplineTensionSelection()) return;
            var snapshot = Session.Snapshot; var target = Selected.Single();
            NativeSketchSplineTensionRules.TryGetTension(snapshot, target, out int tension);
            CancelCandidates(); int generation = ++_promptGeneration;
            var entry = new NumericEntry("sketch.spline.tension.input", QuantityUnit.None, tension, 0, 100, integerOnly: true);
            _prompt = entry;
            entry.Committed += () =>
            {
                if (_disposed || generation != _promptGeneration || _prompt != entry || !_ready() || !Session.CanEdit
                    || !ReferenceEquals(snapshot, Session.Snapshot) || !NativeSketchSplineTensionRules.TryGetTension(snapshot, target, out _)) return;
                _prompt = null;
                Session.SetOperation(NativeSketchSplineTensionRules.Variant, new JObject {
                    ["references"] = new JArray(target.ToJson()), ["tension"] = (int)entry.Value });
                _notice("Tensione spline pronta: Anteprima, poi Applica."); _changed();
            };
            if (_palette() != null) _palette().ShowKeypad(entry, "Tensione spline • intero 0–100"); else entry.BeginEdit();
            _changed();
        }
        private void StartSpline(string variant,string label)
        {
            if(!CanCreate || !Session.Snapshot.SupportsVariant(variant)) return;
            CancelCandidates();++_promptGeneration;
            _splinePoints=new List<CadPoint>();_splineSnapshot=Session.Snapshot;_splineVariant=variant;_splineLabel=label;
            _splineClosed=false;
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
            if(_splineVariant=="spline_control" && !_splineClosed && _splinePoints[0].X==_splinePoints[_splinePoints.Count-1].X
                && _splinePoints[0].Y==_splinePoints[_splinePoints.Count-1].Y)
            {
                _notice("Il primo e l'ultimo punto coincidono: attiva Spline chiusa oppure rimuovi l'ultimo punto.");
                return;
            }
            string variant=_splineVariant;int method=_splineFitMethod;bool closed=_splineClosed;CancelPrompt();
            var options=new JObject {["points_mm"]=points,["closed"]=closed};
            if(variant=="spline_fit") options["fit_method"]=method;
            try {Session.SetOperation(variant,options);_notice("Spline pronta: Anteprima, poi Applica.");}
            catch(Exception ex) {_notice(ex.Message);}
            _changed();
        }
    }
}
