using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class NativeSketchCheckpoint
    {
        private readonly JArray _draft;
        internal NativeSketchCheckpoint(NativeSketchSnapshot snapshot, string sketchId, JArray draft, bool review, DocumentState parameterBaseline = null)
        { Snapshot = snapshot; SketchId = sketchId; _draft = draft == null ? null : (JArray)draft.DeepClone(); RequiresCadReview = review; ParameterBaseline = parameterBaseline; }
        public NativeSketchSnapshot Snapshot { get; }
        public string SketchId { get; }
        public JArray Draft => _draft == null ? null : (JArray)_draft.DeepClone();
        public bool RequiresCadReview { get; }
        internal DocumentState ParameterBaseline { get; }
    }
    /// <summary>Shared part/sheet-metal editor. Native snapshot survives interrupted reads; Apply remains owned by DesignSession.</summary>
    public sealed partial class NativeSketchSession : IDisposable
    {
        private readonly INativeSketchBackend _backend;
        private readonly bool _ownsMutation;
        private CancellationTokenSource _read = new CancellationTokenSource();
        private DocumentState _state;
        private bool _online, _part, _disposed;
        private int _generation;
        private JArray _draft;
        public NativeSketchSession(INativeSketchBackend backend, Func<DateTimeOffset> clock = null, DesignSession mutation = null)
        { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _ownsMutation = mutation == null;
            Mutation = mutation ?? new DesignSession(backend, clock); }
        public DesignSession Mutation { get; }
        public NativeSketchSnapshot Snapshot { get; private set; }
        public string SketchId { get; private set; }
        public string Error { get; private set; }
        public bool Loading { get; private set; }
        public bool NeedsRefresh { get; private set; }
        public bool HasDraft => _draft != null;
        public bool CanConfirmDraft => CanEdit && _draft!=null && (IsParameterDraft
            ? Same(_parameterBaseline, Snapshot.State) : _draft.OfType<JObject>()
            .All(operation=>Snapshot.SupportsVariant((string)operation["arguments"]?["variant"])
                && CopySupported((string)operation["arguments"]?["variant"],operation["arguments"]?["options"])
                && ((string)operation["arguments"]?["variant"]!="arc_tangent" || NativeSketchTangentArcRules.OptionsCurrent(Snapshot,operation["arguments"]?["options"] as JObject))
                && ReferencesCurrent(operation["arguments"]?["options"]?["references"],(string)operation["arguments"]?["variant"])));
        public bool NeedsReferenceSelection => CanEdit && HasDraft && Mutation.Status==DesignStatus.Empty && !CanConfirmDraft
            && _draft.Count==1 && _draft[0]["arguments"]?["options"]?["references"] is JArray;
        public void ReconfirmSelection(System.Collections.Generic.IEnumerable<SketchEntityRef> selected)
        {
            if(!NeedsReferenceSelection || selected==null) throw new InvalidOperationException("No preserved operation awaiting current native selections.");
            var options=(JObject)_draft[0]["arguments"]["options"].DeepClone();
            if(options["pick_points_mm"]!=null)
                throw new InvalidOperationException("Select two fresh curve picks before reconfirming an operation with corner solutions.");
            if((string)_draft[0]["arguments"]["variant"]=="arc_tangent")
                options=NativeSketchTangentArcRules.ReconfirmOptions(Snapshot,options,selected.ToArray());
            else options["references"]=new JArray(selected.Select(reference=>reference.ToJson()));
            SetOperation((string)_draft[0]["arguments"]["variant"],options);
        }
        private bool CopySupported(string variant,JToken options)=>variant!="transform_rotate" || (bool?)options?["copy"]!=true
            || Snapshot.SupportsVariant("transform_copy");
        private bool ReferencesCurrent(JToken token,string variant)
        {
            bool stretch=variant=="transform_stretch",transform=variant=="transform_move" || variant=="transform_copy" || variant=="transform_rotate" || stretch;
            bool offset=variant=="offset_distance" || variant=="offset_point";
            bool patternEdit=variant=="pattern_update" || variant=="pattern_delete",pattern=patternEdit || variant=="pattern_rectangular" || variant=="pattern_circular";
            if((transform || offset || pattern || variant=="arc_tangent") && token==null) return false;
            if(token==null) return true;
            if(variant=="arc_tangent" && !NativeSketchTangentArcRules.AcceptsReferences(Snapshot,token)) return false;
            if(!(token is JArray references) || references.Count<1 || references.Count>2000 || references.Any(r=>!(r is JObject))) return false;
            var objects=variant=="constraint_delete" || patternEdit ? Snapshot.Constraints : variant=="dimension_update" || variant=="dimension_delete" ? Snapshot.Dimensions : Snapshot.Entities;
            if((variant=="dimension_update" || variant=="dimension_delete" || variant=="constraint_delete" || patternEdit) && references.Count!=1) return false;
            if(variant=="line_midpoint" && (references.Count>2
                || references.Select(r=>((string)r["entity_id"],(string)r["subpart"])).Distinct().Count()!=references.Count
                || references.Any(r=>!Enum.TryParse((string)r["subpart"],false,out SketchSubpart part)
                    || !NativeSketchMidpointRules.IsPoint((string)r["type"],part)))) return false;
            if(variant=="transform_rotate" && references.Any(r=>(string)r["type"]=="text")) return false;
            if(pattern && references.Any(r=>(string)r["subpart"]!="Entity")) return false;
            if(patternEdit && !objects.Any(e=>e.Reference?.EntityId==(string)references[0]["entity_id"] && e.Data["pattern_definition"] is JObject)) return false;
            if(stretch && (references.Select(r=>((string)r["entity_id"],(string)r["subpart"])).Distinct().Count()!=references.Count
                || references.Any(r=>!NativeSketchStretchRules.IsSelection((string)r["type"],(string)r["subpart"])))) return false;
            if(transform && !stretch && (references.Select(r=>(string)r["entity_id"]).Distinct(StringComparer.Ordinal).Count()!=references.Count
                || references.Any(r=>(string)r["subpart"]!="Entity" && !((string)r["type"]=="point" && (string)r["subpart"]=="Point")))) return false;
            if(offset && (references.Select(r=>(string)r["entity_id"]).Distinct(StringComparer.Ordinal).Count()!=references.Count
                || references.Any(r=>(string)r["subpart"]!="Entity" || !NativeSketchOffsetRules.IsCurve((string)r["type"])))) return false;
            return references.Cast<JObject>().All(reference=>
                (string)reference["document_id"]==Snapshot.State.DocumentId && (string)reference["revision"]==Snapshot.State.Revision
                && (string)reference["sketch_id"]==SketchId && objects.Any(e=>e.Selectable && e.Reference?.EntityId==(string)reference["entity_id"]
                    && e.Type==(string)reference["type"]
                    && Enum.TryParse((string)reference["subpart"],false,out SketchSubpart part) && Enum.IsDefined(typeof(SketchSubpart),part)
                    && e.CanSelectSubpart(part)));
        }
        public JArray Draft => _draft == null ? null : (JArray)_draft.DeepClone();
        public bool CanEdit => !_disposed && _online && _part && !Loading && !NeedsRefresh
            && Snapshot != null && Snapshot.SketchId == SketchId && Snapshot.NativeMutationComplete && Same(Snapshot.State, _state) && Mutation.CanEdit;
        public event Action Changed;

        public NativeSketchCheckpoint Checkpoint()
        {
            bool review = Mutation.Status == DesignStatus.Committing || Mutation.Status == DesignStatus.RefreshRequired;
            return new NativeSketchCheckpoint(Snapshot, SketchId, review ? null : _draft, review, review ? null : _parameterBaseline);
        }
        public void Restore(NativeSketchCheckpoint checkpoint)
        {
            if (_disposed || Snapshot != null || SketchId != null || _draft != null || checkpoint == null)
                throw new InvalidOperationException("Restore only into an empty native sketch session.");
            Snapshot = checkpoint.Snapshot; SketchId = checkpoint.SketchId; _draft = checkpoint.Draft; NeedsRefresh = SketchId != null;
            _parameterBaseline = checkpoint.ParameterBaseline;
            if (checkpoint.RequiresCadReview && Mutation.Status != DesignStatus.RefreshRequired) Mutation.RequireCadReview();
            Changed?.Invoke();
        }

        public void SetEnvironment(DocumentState state, bool online, bool isPart)
        {
            if (Same(_state, state) && _online == online && _part == isPart) return;
            InvalidateRead(); _state = state; _online = online; _part = isPart;
            NeedsRefresh = Snapshot != null;
            Mutation.SetContext(state, online, isPart);
            // Keep the frozen snapshot and pending operation for inspection. Never rebase it silently.
            Changed?.Invoke();
        }

        public async Task OpenAsync(string sketchId)
        {
            if (_disposed || !_online || !_part || _state == null || Mutation.Status == DesignStatus.Committing)
                throw new InvalidOperationException("Editor requires an online part or sheet-metal document.");
            if (_draft != null && SketchId != sketchId) throw new InvalidOperationException("Finish or discard the current sketch operation before opening another sketch.");
            if (Mutation.Status == DesignStatus.RefreshRequired)
                throw new InvalidOperationException("Refresh and review the attempted mutation before reopening the editor.");
            if (!SketchEntityRef.Opaque(sketchId)) throw new ArgumentException("An opaque native sketch reference is required.", nameof(sketchId));
            if (_draft != null) Mutation.Cancel();
            InvalidateRead(); int generation = _generation; var state = _state; var ct = _read.Token;
            SketchId = sketchId; Loading = true; NeedsRefresh = true; Error = null; Changed?.Invoke();
            try
            {
                var snapshot = await _backend.GetNativeSketchAsync(state, sketchId, ct);
                if (_disposed || generation != _generation) return;
                if (snapshot == null || !Same(snapshot.State, state) || snapshot.SketchId != sketchId)
                    throw new FormatException("Backend returned a snapshot from another sketch state.");
                Snapshot = snapshot; NeedsRefresh = false;
                if (_draft != null) Error = IsParameterDraft && !Same(_parameterBaseline, Snapshot.State)
                    ? "Bozza parametro di una revisione precedente: annulla, rileggi il parametro e prepara nuovamente la modifica."
                    : "Operazione conservata: confermare la nuova revisione prima di ripetere Anteprima.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { if (_disposed || generation != _generation) return; Error = ex.Message; }
            finally { if (!_disposed && generation == _generation) { Loading = false; Changed?.Invoke(); } }
        }

        public void SetOperation(string variant, JObject options)
        {
            if (!CanEdit) throw new InvalidOperationException("Refresh the complete native sketch before editing.");
            if (string.IsNullOrWhiteSpace(variant) || options == null) throw new ArgumentException("Native operation variant and options are required.");
            if(!Snapshot.SupportsVariant(variant)) throw new InvalidOperationException("Il backend collegato non dichiara questa operazione di schizzo.");
            if(!CopySupported(variant,options)) throw new InvalidOperationException("Il backend collegato non dichiara la copia durante la rotazione.");
            if(variant=="line_midpoint") NativeSketchMidpointRules.Roles(options);
            if(variant=="arc_tangent") NativeSketchTangentArcRules.ValidateOptions(Snapshot,options);
            if((variant=="dimension_update" || variant=="dimension_delete" || variant=="constraint_delete") && options["references"]==null)
                throw new InvalidOperationException("Select the existing native dimension or constraint.");
            if(!ReferencesCurrent(options["references"],variant)) throw new InvalidOperationException("Select native entities from the current sketch snapshot.");
            var frozenOptions=(JObject)options.DeepClone();
            if(variant=="spline_fit")
            {
                if(frozenOptions["fit_method"]==null) frozenOptions["fit_method"]=Snapshot.SplineFitMethod.Value;
                var fit=frozenOptions["fit_method"];
                if(fit.Type!=JTokenType.Integer || (int)fit<26369 || (int)fit>26371)
                    throw new ArgumentException("Metodo spline nativo non valido.");
            }
            _parameterBaseline = null;
            _draft = new JArray(new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject
                { ["sketch_id"] = SketchId, ["variant"] = variant, ["options"] = frozenOptions } });
            Mutation.SetDraft(_draft); Error = null; Changed?.Invoke();
        }

        /// <summary>Explicitly reissue a preserved operation after the user has inspected the refreshed sketch.</summary>
        public void ConfirmDraftOnCurrentRevision()
        {
            if (!CanConfirmDraft) throw new InvalidOperationException("Riferimenti della bozza non attuali: conserva le opzioni, seleziona nuovamente le entità nella revisione corrente.");
            Mutation.SetDraft(_draft); Error = null; Changed?.Invoke();
        }
        public async Task PreviewAsync()
        {
            if (!CanConfirmDraft || Mutation.Status == DesignStatus.Empty)
                throw new InvalidOperationException("Confirm a current native operation before Preview.");
            await Mutation.PreviewAsync(); Changed?.Invoke();
        }
        public async Task ApplyAsync()
        {
            if (!CanEdit || !Mutation.CanApply) throw new InvalidOperationException("Render the validated preview before Apply.");
            await Mutation.ApplyAsync(); _draft = null; _parameterBaseline = null; NeedsRefresh = true; Changed?.Invoke();
        }
        public void AcknowledgeMutationRefresh(DocumentState state, bool unknownOutcomeReviewed = false)
        {
            if (!Same(state, _state)) throw new InvalidOperationException("Fetch the authoritative current document state first.");
            Mutation.AcknowledgeRefresh(state, unknownOutcomeReviewed); NeedsRefresh = true; Changed?.Invoke();
        }
        public void DiscardOperation()
        {
            Mutation.Cancel(); _draft = null; _parameterBaseline = null; Error = null; Changed?.Invoke();
        }
        public void InvalidatePresentation()
        {
            if (_disposed || Mutation.Status == DesignStatus.Committing || Mutation.Status == DesignStatus.RefreshRequired) return;
            Mutation.Cancel(); Changed?.Invoke();
        }
        public void Close()
        {
            if (_draft != null || Mutation.Status == DesignStatus.Committing || Mutation.Status == DesignStatus.RefreshRequired)
                throw new InvalidOperationException("Resolve or discard the pending operation before closing the sketch editor.");
            InvalidateRead(); Snapshot = null; SketchId = null; NeedsRefresh = false; Error = null; Changed?.Invoke();
        }
        private static bool Same(DocumentState a, DocumentState b) => a?.DocumentId == b?.DocumentId && a?.Revision == b?.Revision;
        private void InvalidateRead() { _generation++; Loading = false; _read.Cancel(); _read.Dispose(); _read = new CancellationTokenSource(); }
        public void Dispose() { if (_disposed) return; _disposed = true; _generation++; _read.Cancel(); _read.Dispose(); if (_ownsMutation) Mutation.Dispose(); Changed = null; }
    }
}
