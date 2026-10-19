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
        private string[] _midpointRoles=Array.Empty<string>();
        private bool MidpointSelection(string[] roles,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant("line_midpoint") && (roles.Length==0 || Selected.Count==roles.Length && Selected.All(NativeSketchMidpointRules.IsPoint));
        private IEnumerable<XrAction> MidpointActions()
        {
            foreach(var item in new[] {("coordinates","Punto medio: coordinate",Array.Empty<string>()),
                ("midpoint","Punto medio: centro selezionato",new[] {"midpoint"}),
                ("end","Punto medio: estremo selezionato",new[] {"end"}),
                ("both","Punto medio: due punti selezionati",new[] {"midpoint","end"})})
            {
                var mode=item;
                yield return new XrAction("sketch.line.midpoint."+mode.Item1,mode.Item2,SketchCommandCatalog.FamilyPrefix+"Linea",
                    ()=>CanCreate && Session.Snapshot.SupportsVariant("line_midpoint"),()=>{_midpointRoles=mode.Item3;_changed();},
                    ()=>"Apri uno schizzo aggiornato e completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_midpointRoles.SequenceEqual(mode.Item3));
            }
        }
        private void PromptMidpoint()
        {
            if(!CanCreate || !MidpointSelection(_midpointRoles)) return;
            CancelCandidates();int generation=++_promptGeneration,index=0;var snapshot=Session.Snapshot;
            var roles=(string[])_midpointRoles.Clone();var references=roles.Length==0 ? Array.Empty<SketchEntityRef>() : Selected.ToArray();
            var values=new[] {0d,0d,20d,0d};
            for(int i=0;i<roles.Length;i++)
            {
                var reference=references[i];var entity=snapshot.Entities.Single(e=>e.Reference?.EntityId==reference.EntityId);
                if(!entity.TryGetSubpartPoint(reference.Subpart==SketchSubpart.Entity ? SketchSubpart.Point : reference.Subpart,out var point)) return;
                int offset=roles[i]=="midpoint" ? 0 : 2;values[offset]=point.X;values[offset+1]=point.Y;
            }
            var fields=Enumerable.Range(0,4).Where(i=>!roles.Contains(i<2 ? "midpoint" : "end")).ToArray();
            void Finish()
            {
                ResetCoordinatePick();
                var options=new JObject {["points_mm"]=new JArray(new JArray(values[0],values[1]),new JArray(values[2],values[3]))};
                if(roles.Length>0) {options["references"]=new JArray(references.Select(r=>r.ToJson()));options["reference_roles"]=new JArray(roles);}
                try {Session.SetOperation("line_midpoint",options);_notice("Linea punto medio pronta: Anteprima, poi Applica.");}
                catch(Exception ex) {_notice(ex.Message);}
                _changed();
            }
            void Next()
            {
                ResetCoordinatePick();
                int field=fields[index];var entry=new NumericEntry("sketch.midpoint.input."+field,QuantityUnit.Millimeters,values[field],-100000,100000);_prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[field]=entry.Value;if(++index<fields.Length) {Next();return;}_prompt=null;Finish();
                };
                string label=(field<2 ? "Punto medio " : "Estremo ")+(field%2==0 ? "X mm" : "Y mm");
                if(_palette()!=null) _palette().ShowKeypad(entry,"Linea punto medio • "+label);else entry.BeginEdit();_changed();
                int pair=field-field%2;
                ArmCoordinatePick(snapshot,generation,point=>
                {
                    CloseCoordinateField();values[pair]=point.X;values[pair+1]=point.Y;
                    while(index<fields.Length && fields[index]<pair+2) index++;
                    if(index<fields.Length) Next();else Finish();
                });
                _notice("Linea punto medio: indica "+(field<2 ? "il punto medio" : "l'estremo")+" sul foglio con Trigger oppure inserisci X/Y.");
            }
            if(fields.Length==0) Finish();else Next();
        }
    }
}
