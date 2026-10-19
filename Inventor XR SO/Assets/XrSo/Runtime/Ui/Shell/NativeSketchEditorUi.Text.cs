using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        public const string NativeTextTab = "_sketch.text.content";
        private JArray _nativeTextRuns;
        private NativeSketchSnapshot _nativeTextSnapshot;
        private SketchEntityRef _nativeTextTarget;
        private string _nativeTextVariant, _nativeTextStyle;
        private bool _nativeTextRectangle;
        private double[] _nativeTextPosition;

        private bool NativeTextCurrent => _nativeTextRuns!=null && !_disposed && _ready() && Session.CanEdit
            && !Session.HasDraft && !WindowActive && ReferenceEquals(_nativeTextSnapshot,Session.Snapshot)
            && Session.Snapshot.SupportsVariant(_nativeTextVariant);
        private bool NativeTextIdle => NativeTextCurrent && _prompt==null && _textPrompt==null;
        private NativeSketchObject TextSelection(string variant,bool reconfirming=false)
        {
            if(!CanSelect || (Session.HasDraft && !reconfirming) || !Session.Snapshot.SupportsVariant(variant)
                || Selected.Count!=1) return null;
            var reference=Selected[0];
            if(reference.Type!="text" || reference.Subpart!=SketchSubpart.Entity || !reference.Matches(Session.Snapshot.State,Session.SketchId)) return null;
            return Session.Snapshot.Entities.SingleOrDefault(e=>e.Selectable && e.Reference?.EntityId==reference.EntityId);
        }
        private void ResetNativeTextInput()
        {
            _nativeTextRuns=null;_nativeTextSnapshot=null;_nativeTextTarget=null;
            _nativeTextVariant=null;_nativeTextStyle=null;_nativeTextPosition=null;_nativeTextRectangle=false;
        }
        private IEnumerable<XrAction> NativeTextActions()
        {
            var snapshot=Session.Snapshot;
            // Backend disclosure remains authoritative. These variants are intentionally hidden today.
            if(snapshot==null || !new[]{"text_create","text_update","text_delete"}.Any(snapshot.SupportsVariant)) yield break;
            bool Current()=>ReferenceEquals(snapshot,Session.Snapshot);
            string reason()=>"Apri uno schizzo aggiornato con la capacità testo verificata; seleziona un solo testo intero per modificarlo.";
            string family=SketchCommandCatalog.FamilyPrefix+"Testo";
            if(snapshot.SupportsVariant("text_create"))
            {
                yield return new XrAction("sketch.native.SketchCreateTextCmd","Testo adattato",family,
                    ()=>Current() && CanCreate,()=>StartNativeText(false),reason);
                yield return new XrAction("sketch.text.rectangle","Testo in rettangolo",family,
                    ()=>Current() && CanCreate,()=>StartNativeText(true),reason);
            }
            if(snapshot.SupportsVariant("text_update"))
                yield return new XrAction("sketch.text.update","Modifica testo selezionato",family,
                    ()=>Current() && TextSelection("text_update")!=null,()=>StartNativeText(false,true),reason);
            if(snapshot.SupportsVariant("text_delete"))
                yield return new XrAction("sketch.text.delete","Elimina testo selezionato",family,
                    ()=>Current() && TextSelection("text_delete")!=null,()=>
                    {
                        var text=TextSelection("text_delete");if(text==null) return;
                        Session.SetOperation("text_delete",new JObject{["references"]=new JArray(text.Reference.ToJson())});
                        _notice("Eliminazione pronta: Anteprima, poi Applica.");_changed();
                    },reason);
            if(_nativeTextRuns==null) yield break;
            int generation=_promptGeneration;
            bool Idle()=>Current() && generation==_promptGeneration && NativeTextIdle;
            string inputReason()=>"Completa il campo corrente oppure riapri il testo nella revisione aggiornata.";
            yield return new XrAction("sketch.text.literal","Aggiungi testo letterale",NativeTextTab,
                ()=>Idle() && NativeTextCanAppend("x",false),()=>PromptNativeTextRun(false),inputReason);
            yield return new XrAction("sketch.text.parameter","Aggiungi parametro locale",NativeTextTab,
                ()=>Idle() && _nativeTextRuns.Count<64,()=>PromptNativeTextRun(true),inputReason);
            yield return new XrAction("sketch.text.newline","A capo",NativeTextTab,
                ()=>Idle() && NativeTextCanAppend("\n",false),()=>AppendNativeTextRun("\n",false),inputReason);
            yield return new XrAction("sketch.text.undo","Rimuovi ultimo elemento",NativeTextTab,
                ()=>Idle() && _nativeTextRuns.Count>0,()=>{_nativeTextRuns.Last.Remove();_changed();},inputReason);
            yield return new XrAction("sketch.text.position",_nativeTextRectangle ? "Due angoli del rettangolo" : "Posizione mm",NativeTextTab,
                Idle,PromptNativeTextPosition,inputReason);
            if(_nativeTextVariant=="text_create")
            {
                yield return new XrAction("sketch.text.style","Nome stile nativo",NativeTextTab,Idle,PromptNativeTextStyle,inputReason);
                yield return new XrAction("sketch.text.style.default","Usa stile del documento",NativeTextTab,Idle,
                    ()=>{_nativeTextStyle=null;_changed();},inputReason);
            }
            yield return new XrAction("sketch.text.finish","Prepara operazione testo",NativeTextTab,
                ()=>Idle() && (_nativeTextVariant=="text_create" ? _nativeTextRuns.Count>0 && _nativeTextPosition!=null
                    : _nativeTextRuns.Count>0 || _nativeTextPosition!=null),FinishNativeText,inputReason);
            yield return new XrAction("sketch.text.summary","Contenuto e opzioni",NativeTextTab,Idle,
                ()=>_notice(NativeTextSummary()),inputReason);
            yield return new XrAction("sketch.text.cancel","Annulla input testo",NativeTextTab,
                ()=>Current() && generation==_promptGeneration && NativeTextCurrent,()=>{CancelPrompt();_changed();},inputReason);
        }
        private void StartNativeText(bool rectangle,bool update=false)
        {
            var text=update ? TextSelection("text_update") : null;
            if(update ? text==null : !CanCreate || !Session.Snapshot.SupportsVariant("text_create")) return;
            CancelCandidates();++_promptGeneration;_nativeTextSnapshot=Session.Snapshot;
            _nativeTextRuns=new JArray();_nativeTextTarget=text?.Reference;_nativeTextVariant=update ? "text_update" : "text_create";
            _nativeTextRectangle=rectangle;_nativeTextPosition=null;_nativeTextStyle=null;
            _notice(update ? "Aggiungi il nuovo contenuto per sostituirlo interamente, oppure modifica solo la posizione. Il contenuto nativo attuale resta intatto fino ad Applica."
                : "Componi testo letterale e parametri locali, poi indica la posizione. Anteprima verifica parametri e stile nel documento.");
            _changed();_palette()?.ShowTabGroup(new[]{NativeTextTab},true);
        }
        private bool NativeTextCanAppend(string value,bool parameter)
        {
            if(_nativeTextRuns==null || _nativeTextRuns.Count>=64 || value==null) return false;
            try { XmlConvert.VerifyXmlChars(value); } catch(XmlException) {return false;}
            if(parameter) return !string.IsNullOrWhiteSpace(value) && value.Length<=128 && !value.Any(char.IsControl);
            return _nativeTextRuns.Sum(r=>((string)r["text"] ?? "").Length)+value.Length<=4096;
        }
        private void AppendNativeTextRun(string value,bool parameter)
        {
            if(!NativeTextCurrent || !NativeTextCanAppend(value,parameter)) {_notice("Contenuto non valido: massimo 64 elementi e 4096 caratteri letterali.");return;}
            _nativeTextRuns.Add(new JObject{[parameter ? "parameter_name" : "text"]=value});_changed();
        }
        private void PromptNativeTextRun(bool parameter)
        {
            if(!NativeTextIdle) return;
            int generation=_promptGeneration;var snapshot=_nativeTextSnapshot;
            int remaining=4096-_nativeTextRuns.Sum(r=>((string)r["text"] ?? "").Length);
            if(!parameter && remaining<=0) return;
            var entry=new TextEntry("sketch.text.run", "",parameter ? 128 : remaining);_textPrompt=entry;
            entry.Committed+=()=>
            {
                if(!NativeTextCurrent || generation!=_promptGeneration || !ReferenceEquals(snapshot,Session.Snapshot) || _textPrompt!=entry) return;
                _textPrompt=null;
                if(!NativeTextCanAppend(entry.Value,parameter)) {_notice("Contenuto non valido: caratteri XML, nome parametro o limite del testo.");_changed();return;}
                AppendNativeTextRun(entry.Value,parameter);
                _notice(parameter ? "Parametro aggiunto per nome: Anteprima ne verifica l'esistenza nel documento." : "Testo letterale aggiunto senza interpretare markup.");_changed();
            };
            if(_palette()!=null) _palette().ShowTextKeypad(entry,parameter ? "Nome del parametro locale" : "Testo letterale");else entry.BeginEdit();_changed();
        }
        private void PromptNativeTextStyle()
        {
            if(!NativeTextIdle || _nativeTextVariant!="text_create") return;
            int generation=_promptGeneration;var entry=new TextEntry("sketch.text.style.name",_nativeTextStyle ?? "",256);_textPrompt=entry;
            entry.Committed+=()=>
            {
                if(!NativeTextCurrent || generation!=_promptGeneration || _textPrompt!=entry) return;
                _textPrompt=null;_nativeTextStyle=entry.Value;_changed();
            };
            if(_palette()!=null) _palette().ShowTextKeypad(entry,"Nome esatto dello stile nel documento");else entry.BeginEdit();_changed();
        }
        private void PromptNativeTextPosition()
        {
            if(!NativeTextIdle) return;
            int generation=_promptGeneration,index=0;var snapshot=_nativeTextSnapshot;
            var values=_nativeTextPosition==null ? new double[_nativeTextRectangle ? 4 : 2] : (double[])_nativeTextPosition.Clone();
            void Finish()
            {
                _prompt=null;ResetCoordinatePick();
                if(values.Length==4 && (Math.Abs(values[0]-values[2])<1e-8 || Math.Abs(values[1]-values[3])<1e-8))
                    _notice("Il rettangolo richiede larghezza e altezza positive: indica nuovamente i due angoli.");
                else {_nativeTextPosition=values;_notice("Posizione conservata nella composizione testo.");}
                _changed();
            }
            void Next()
            {
                ResetCoordinatePick();var entry=new NumericEntry("sketch.text.position."+index,QuantityUnit.Millimeters,values[index],-100000,100000);_prompt=entry;
                bool Current()=>NativeTextCurrent && generation==_promptGeneration && ReferenceEquals(snapshot,Session.Snapshot) && _prompt==entry;
                entry.Committed+=()=>{if(!Current()) return;values[index++]=entry.Value;if(index==values.Length) Finish();else Next();};
                if(index%2==0)
                    ArmCoordinatePick(snapshot,generation,point=>
                    {if(!Current()) return;CloseCoordinateField();values[index++]=point.X;values[index++]=point.Y;if(index==values.Length) Finish();else Next();});
                if(_palette()!=null) _palette().ShowKeypad(entry,(index<2 ? "Posizione " : "Angolo opposto ")+(index%2==0 ? "X mm" : "Y mm"));else entry.BeginEdit();_changed();
            }
            Next();
        }
        private string NativeTextSummary()
        {
            string content=string.Concat(_nativeTextRuns.Select(r=>r["parameter_name"]!=null ? "[parametro: "+(string)r["parameter_name"]+"]" : (string)r["text"]));
            return (_nativeTextVariant=="text_update" ? "Sostituzione: " : "Contenuto: ")+(content.Length==0 ? "invariato / non impostato" : content)
                +" • "+(_nativeTextPosition==null ? "posizione non impostata" : string.Join(", ",_nativeTextPosition.Select(v=>v.ToString("G",System.Globalization.CultureInfo.InvariantCulture)))+" mm")
                +" • "+(_nativeTextVariant=="text_update" ? "stile nativo invariato" : _nativeTextStyle ?? "stile del documento");
        }
        private void FinishNativeText()
        {
            if(!NativeTextIdle || (_nativeTextVariant=="text_create" ? _nativeTextRuns.Count==0 || _nativeTextPosition==null : _nativeTextRuns.Count==0 && _nativeTextPosition==null)) return;
            var options=new JObject();string variant=_nativeTextVariant;
            if(_nativeTextTarget!=null) options["references"]=new JArray(_nativeTextTarget.ToJson());
            if(_nativeTextRuns.Count>0) options["content"]=new JObject{["schema"]=1,["runs"]=_nativeTextRuns.DeepClone()};
            if(_nativeTextPosition!=null)
            {
                options["position_mm"]=new JArray(_nativeTextPosition[0],_nativeTextPosition[1]);
                if(_nativeTextRectangle) options["opposite_corner_mm"]=new JArray(_nativeTextPosition[2],_nativeTextPosition[3]);
            }
            if(_nativeTextStyle!=null) options["style_name"]=_nativeTextStyle;
            CancelPrompt();Session.SetOperation(variant,options);_notice("Testo pronto: Anteprima, poi Applica.");_changed();
        }
    }
}
