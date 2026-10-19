using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Xr
{
    public sealed partial class DesignWorkspace
    {
        public const string NativeParameterTab="_parameter.native";
        private TextEntry _parameterTextEntry;
        private int _parameterPromptGeneration;
        private JObject _selectedNativeParameter;
        private DesignContext _parameterContext;
        public TextEntry ActiveParameterTextEntry=>_parameterTextEntry?.Editing==true ? _parameterTextEntry : null;
        private bool NativeParameterIdle=>EditableNow && _session.Status==DesignStatus.Empty
            && _ask==null && ActiveParameterTextEntry==null
            && _context.State.DocumentId==_state?.DocumentId && _context.State.Revision==_state.Revision;
        private bool NativeParameterSelected=>NativeParameterIdle && _selectedNativeParameter!=null && ReferenceEquals(_parameterContext,_context);

        private IEnumerable<XrAction> NativeParameterActions()
        {
            yield return new XrAction("design.parameters.expressions","Espressioni e gestione",TabParameters,
                ()=>NativeParameterIdle,OpenNativeParameters,EditReason);
            yield return new XrAction("design.parameter.create","Crea parametro utente",TabParameters,
                ()=>NativeParameterIdle,CreateNativeParameter,EditReason);
            var context=_context;var selected=_selectedNativeParameter;
            bool Current()=>NativeParameterSelected && ReferenceEquals(context,_context) && ReferenceEquals(selected,_selectedNativeParameter);
            yield return new XrAction("design.parameter.expression","Modifica espressione nativa",NativeParameterTab,Current,()=>
                PromptNativeParameterFields("Espressione di "+(string)selected["name"],new[]{"Espressione Inventor"},
                    new[]{(string)selected["expression"] ?? ""},new[]{NativeParameterOperations.MaxExpressionLength},
                    values=>StageNativeParameter(NativeParameterOperations.SetExpression((string)selected["name"],values[0]),(string)selected["name"])),EditReason);
            yield return new XrAction("design.parameter.rename","Rinomina parametro",NativeParameterTab,Current,()=>
                PromptNativeParameterFields("Rinomina "+(string)selected["name"],new[]{"Nuovo nome"},new[]{(string)selected["name"]},
                    new[]{NativeParameterOperations.MaxNameLength},values=>StageNativeParameter(NativeParameterOperations.Rename((string)selected["name"],values[0]),(string)selected["name"])),EditReason);
            yield return new XrAction("design.parameter.delete","Prepara eliminazione",NativeParameterTab,Current,()=>
                StageNativeParameter(NativeParameterOperations.Delete((string)selected["name"]),(string)selected["name"]),EditReason);
            yield return new XrAction("design.parameter.info","Valore ed espressione correnti",NativeParameterTab,Current,
                ()=>{SetNotice(ParameterDescription(selected));Refresh();},EditReason);
            yield return new XrAction("design.parameter.input.cancel","Annulla input parametro",NativeParameterTab,
                ()=>Active && ActiveParameterTextEntry!=null,()=>{CancelParameterInput();Refresh();},()=>"Nessun campo parametro aperto.");
        }
        private void OpenNativeParameters()
        {
            if(!NativeParameterIdle) return;
            var context=_context;int generation=_generation;
            OpenPicker("Parametri: espressioni native",context.Parameters.OfType<JObject>()
                .Where(p=>p["name"]?.Type==JTokenType.String && !string.IsNullOrWhiteSpace((string)p["name"]))
                .Select(p=>
                {
                    var frozen=(JObject)p.DeepClone();
                    return new PickerItem(ParameterDescription(frozen),()=>
                    {
                        if(!NativeParameterIdle || generation!=_generation || !ReferenceEquals(context,_context)) return;
                        _selectedNativeParameter=frozen;_parameterContext=context;Refresh();
                        _shell?.Palette.ShowTabGroup(new[]{NativeParameterTab},true);
                    });
                }));
        }
        private static string ParameterDescription(JObject parameter)
        {
            string value;
            if(parameter["value_bool"]?.Type==JTokenType.Boolean) value=(bool)parameter["value_bool"] ? "True" : "False";
            else if(parameter["value_text"]?.Type==JTokenType.String) value="\""+(string)parameter["value_text"]+"\"";
            else
            {
                var token=new[]{"value_mm","value_deg","value_mm2","value_mm3","value_internal","value_unitless"}
                    .Select(key=>parameter[key]).FirstOrDefault(IsFiniteParameterNumber);
                value=token==null ? "valore non disponibile" : Format((double)token);
            }
            string expression=parameter["expression"]?.Type==JTokenType.String ? (string)parameter["expression"] : "espressione non disponibile";
            return (string)parameter["name"]+" = "+value+" • "+((string)parameter["unit"] ?? "unità non disponibile")+" • "+expression;
        }
        private static bool IsFiniteParameterNumber(JToken value)=>value!=null && (value.Type==JTokenType.Integer || value.Type==JTokenType.Float)
            && !double.IsNaN((double)value) && !double.IsInfinity((double)value);
        private static bool TryNumericParameter(JObject parameter,out double value,out string unit)
        {
            value=0;unit=null;
            if(parameter["value_bool"]!=null || parameter["value_text"]!=null) return false;
            JToken token;
            if(IsFiniteParameterNumber(parameter["value_mm"])) {unit="mm";token=parameter["value_mm"];}
            else if(IsFiniteParameterNumber(parameter["value_deg"])) {unit="deg";token=parameter["value_deg"];}
            else if((string)parameter["unit"]=="ul" && IsFiniteParameterNumber(parameter["value_unitless"])) {unit="ul";token=parameter["value_unitless"];}
            else return false;
            value=(double)token;return true;
        }
        private void CreateNativeParameter()
        {
            if(!NativeParameterIdle) return;
            PromptNativeParameterFields("Nuovo parametro utente",new[]{"Nome","Espressione Inventor","Unità nativa (es. mm, deg, ul)"},
                new[]{"","",""},new[]{NativeParameterOperations.MaxNameLength,NativeParameterOperations.MaxExpressionLength,NativeParameterOperations.MaxUnitLength},
                values=>StageNativeParameter(NativeParameterOperations.Create(values[0],values[1],values[2]),values[0]));
        }
        private void PromptNativeParameterFields(string title,string[] labels,string[] initial,int[] limits,Action<string[]> done)
        {
            if(!NativeParameterIdle) return;
            CancelParameterInput(false);int promptGeneration=_parameterPromptGeneration,generation=_generation,index=0;
            var context=_context;var session=_session;var values=(string[])initial.Clone();
            bool Current()=>this!=null && EditableNow && session==_session && session.Status==DesignStatus.Empty
                && _ask==null
                && generation==_generation && promptGeneration==_parameterPromptGeneration && ReferenceEquals(context,_context)
                && context.State.DocumentId==_state?.DocumentId && context.State.Revision==_state.Revision;
            void Next()
            {
                string original=values[index] ?? "";
                // TextEntry is single-line. Never rewrite an existing multiline expression silently.
                bool editable=original.Length<=limits[index] && !original.Any(char.IsControl);
                var entry=new TextEntry("design.parameter.input."+index,editable ? original : "",limits[index]);_parameterTextEntry=entry;
                entry.Committed+=()=>
                {
                    if(!Current() || _parameterTextEntry!=entry) return;
                    _parameterTextEntry=null;values[index++]=entry.Value;
                    if(index<labels.Length){Next();return;}
                    try{done(values);}catch(Exception ex){SetNotice(ex.Message);Refresh();}
                };
                if(!editable) SetNotice("L'espressione corrente supera il campo a riga singola: inserisci esplicitamente una sostituzione; il CAD resta invariato.");
                if(_shell!=null) _shell.Palette.ShowTextKeypad(entry,title+" • "+labels[index]);else entry.BeginEdit();Refresh();
            }
            Next();
        }
        private void StageNativeParameter(JObject operation,string name)
        {
            if(!NativeParameterIdle) return;
            _feature=null;_featureEdit=null;_sketch=null;_parameterName=name;_screen="parameter";
            _session.SetDraft(new JArray(operation));
            SetNotice("Operazione parametro pronta: Anteprima verifica espressione, unità e dipendenze native; poi Applica.");Refresh();
        }
        private void CancelParameterInput(bool clearSelection=true)
        {
            bool ownedKeypad=_parameterTextEntry?.Editing==true;
            ++_parameterPromptGeneration;_parameterTextEntry?.CancelEdit();_parameterTextEntry=null;
            if(clearSelection){_selectedNativeParameter=null;_parameterContext=null;}
            // Hide only this keypad; do not close numeric/sketch prompts owned elsewhere.
            if(ownedKeypad) _shell?.Palette.HideKeypad();
        }
    }
}
