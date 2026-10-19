using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        public const string ConstraintVisibilityTab = "_sketch.constraint.visibility";
        // Null means every type. An empty set deliberately hides every unselected type.
        private HashSet<string> _constraintNativeTypes;

        public string ConstraintVisibilitySummary
        {
            get
            {
                if(!IsOpen || Session.Snapshot == null) return "Apri uno schizzo per visualizzare i vincoli.";
                if(Session.NeedsRefresh || Session.Mutation.Status == DesignStatus.RefreshRequired)
                    return "Visualizzazione vincoli scaduta: ricarica lo schizzo.";
                var geometry = _geometry(); var layout = geometry?.ConstraintLayout;
                if(layout == null || layout.State.DocumentId != Session.Snapshot.State.DocumentId
                    || layout.State.Revision != Session.Snapshot.State.Revision || layout.SketchId != Session.Snapshot.SketchId)
                    return "Visualizzazione vincoli non disponibile. " + (geometry?.ConstraintLayoutUnavailableReason ?? "");
                var rows = layout.Entries;
                int Count(NativeSketchConstraintGlyphState state) => rows.Count(r=>r.State == state);
                return "Vincoli: mostrate " + Count(NativeSketchConstraintGlyphState.Visible)
                    + "; filtrate " + Count(NativeSketchConstraintGlyphState.HiddenByFilter)
                    + "; oltre limite " + Count(NativeSketchConstraintGlyphState.OmittedByBudget)
                    + "; senza posizione " + Count(NativeSketchConstraintGlyphState.Unpositionable)
                    + "; non supportate " + Count(NativeSketchConstraintGlyphState.Unsupported)
                    + "; associazioni non disponibili " + rows.Count(r=>!r.AssociationsAvailable)
                    + ". Il vincolo selezionato resta visibile quando posizionabile.";
            }
        }

        private static string ConstraintNativeType(NativeSketchObject constraint)
            => (string)constraint.Data["native_type"] ?? constraint.Type;

        private IEnumerable<XrAction> ConstraintVisibilityActions()
        {
            var snapshot = Session.Snapshot;
            bool Current() => CanSelect && ReferenceEquals(snapshot,Session.Snapshot);
            string reason() => "Apri uno schizzo aggiornato e completa l'input corrente.";
            yield return new XrAction("sketch.constraint.filters","Visualizza vincoli ▸",ConstraintOptionsTab,
                Current,()=>_palette()?.ShowTabGroup(new[] {ConstraintVisibilityTab},true),reason);
            yield return new XrAction("sketch.constraint.visibility.status","Stato visualizzazione",ConstraintVisibilityTab,
                ()=>IsOpen && ReferenceEquals(snapshot,Session.Snapshot),()=>_notice(ConstraintVisibilitySummary),reason);
            yield return new XrAction("sketch.constraint.filter.all","Tutti i tipi",ConstraintVisibilityTab,Current,
                ()=>{if(!Current()) return; _constraintNativeTypes=null; ConstraintVisibilityChanged();},reason,
                kind:XrActionKind.Toggle,isOn:()=>_constraintNativeTypes == null);
            var types = (snapshot?.Constraints ?? Array.Empty<NativeSketchObject>()).Select(ConstraintNativeType)
                .Distinct(StringComparer.Ordinal).OrderBy(t=>t,StringComparer.Ordinal).ToArray();
            foreach(string type in types)
            {
                string nativeType = type;
                yield return new XrAction("sketch.constraint.filter."+nativeType,NativeSketchConstraintRules.Label(nativeType),
                    ConstraintVisibilityTab,Current,()=>
                    {
                        if(!Current()) return;
                        if(_constraintNativeTypes == null) _constraintNativeTypes = new HashSet<string>(types,StringComparer.Ordinal);
                        if(!_constraintNativeTypes.Remove(nativeType)) _constraintNativeTypes.Add(nativeType);
                        ConstraintVisibilityChanged();
                    },reason,kind:XrActionKind.Toggle,isOn:()=>_constraintNativeTypes == null || _constraintNativeTypes.Contains(nativeType));
            }
        }

        private void ConstraintVisibilityChanged()
        { CancelCandidates(); Render(); _changed(); _notice(ConstraintVisibilitySummary); }
    }
}
