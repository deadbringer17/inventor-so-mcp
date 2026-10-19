using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        private bool _windowArmed;
        private CadPoint? _windowStart;
        private NativeSketchSnapshot _windowSnapshot;
        private NativeSketchSelectionChange _windowChange, _windowGestureChange;
        public bool WindowActive => _windowArmed;
        public bool WindowDragging => _windowArmed && _windowStart.HasValue;

        private IEnumerable<XrAction> WindowActions()
        {
            yield return new XrAction("sketch.selection.window","Selezione a finestra",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanSelectCore,()=>
                {
                    if (WindowActive) { CancelWindow(); return; }
                    CancelCandidates();_windowArmed=true;_windowSnapshot=Session.Snapshot;
                    _notice("Finestra: tieni premuto Trigger e trascina. Verso destra: contenute; verso sinistra: attraversate. X annulla.");_changed();
                },()=>"Apri uno schizzo aggiornato e completa l'operazione corrente.",kind:XrActionKind.Toggle,isOn:()=>WindowActive);
            foreach (var change in new[] {NativeSketchSelectionChange.Replace,NativeSketchSelectionChange.Add,NativeSketchSelectionChange.Toggle})
            {
                var mode = change;
                yield return new XrAction("sketch.selection.window."+mode,
                    mode==NativeSketchSelectionChange.Replace ? "Finestra: sostituisci" : mode==NativeSketchSelectionChange.Add ? "Finestra: aggiungi" : "Finestra: inverti",
                    FilterTab,()=>CanSelect,()=>{_windowChange=mode;_changed();},()=>"Completa o annulla la finestra corrente.",kind:XrActionKind.Toggle,isOn:()=>_windowChange==mode);
            }
        }
        private bool StartWindow(CadPoint point,bool additive)
        {
            if (!CanSelectCore || !ReferenceEquals(_windowSnapshot,Session.Snapshot) || _windowStart.HasValue) return false;
            _windowStart=point;_windowGestureChange=additive ? NativeSketchSelectionChange.Toggle : _windowChange;
            _geometry()?.SelectionWindow(Session.Snapshot.Frame,point,point,false);return true;
        }
        public bool MoveWindow(CadPoint point,bool crossing,bool finish=false)
        {
            if (!WindowDragging || !CanSelectCore || !ReferenceEquals(_windowSnapshot,Session.Snapshot)) { if(WindowActive) CancelWindow();return false; }
            if (!finish) { _geometry()?.SelectionWindow(Session.Snapshot.Frame,_windowStart.Value,point,crossing);return true; }
            try
            {
                var hits=NativeSketchWindow.Query(Session.Snapshot,_windowStart.Value,point,
                    crossing ? NativeSketchWindowMode.Crossing : NativeSketchWindowMode.Contained,_selectionType,_selectionPart);
                var next=_windowGestureChange==NativeSketchSelectionChange.Replace ? new List<(string id,SketchSubpart part)>() : _selected.ToList();
                foreach (var hit in hits)
                {
                    var key=(hit.Reference.EntityId,hit.Reference.Subpart);
                    if (_windowGestureChange==NativeSketchSelectionChange.Toggle && next.Contains(key)) next.Remove(key);
                    else if (!next.Contains(key)) next.Add(key);
                }
                if (next.Count>2000) throw new ArgumentException("La selezione supera il limite di 2.000 riferimenti; restringi la finestra o i filtri.");
                // A region identifies entities, never an individual corner solution.
                _selectedHits.Clear();_selectedHitSnapshot=null;
                _selected.Clear();_selected.AddRange(next);_selectedDimension=null;_selectedConstraint=null;
                _notice(hits.Count+" bersagli nella finestra; "+next.Count+" selezionati.");
                ResetWindow(false);Render();_changed();return true;
            }
            catch (Exception ex) when(ex is ArgumentException || ex is FormatException) { _notice(ex.Message);CancelWindow();return false; }
        }
        public bool WindowRay(Transform model,Ray ray,bool overUi,bool finish=false)
        {
            if (!WindowDragging) return false;
            if (overUi || !CadCoordinates.SketchRay(model,_windowSnapshot.Frame,ray,out var point))
            {
                if (finish) CancelWindow();else _geometry()?.SelectionWindow(null,default,default,false);
                return false;
            }
            var start=model.TransformPoint(CadCoordinates.ToLocal(_windowSnapshot.Frame.ToModel(_windowStart.Value)));
            var end=model.TransformPoint(CadCoordinates.ToLocal(_windowSnapshot.Frame.ToModel(point)));
            bool crossing=Vector3.Dot(end-start,_head()?.right ?? model.right)<0;
            return MoveWindow(point,crossing,finish);
        }
        public void CancelWindow() { ResetWindow(true); }
        private void ResetWindow(bool notify)
        {
            bool active=WindowActive;_windowArmed=false;_windowStart=null;_windowSnapshot=null;
            _geometry()?.SelectionWindow(null,default,default,false);
            if (active && notify) { Render();_changed(); }
        }
    }
}
