using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum NativeSketchConstraintGlyphState
    {
        Visible, HiddenByFilter, OmittedByBudget, Unpositionable, Unsupported
    }

    public enum NativeSketchConstraintAnchorSource { None, NativeAnnotation, RelatedSubpart, RelatedEntityAnchor }

    /// <summary>Footprints are supplied by the renderer in sketch mm; this is view state only.</summary>
    public sealed class NativeSketchConstraintGlyphOptions
    {
        public const int MaximumVisible = 512;
        public const int CandidateCount = 33;
        public NativeSketchConstraintGlyphOptions(int maxVisible = 256, double widthMm = 8,
            double heightMm = 4, double gapMm = 1, bool showAll = true, IEnumerable<string> nativeTypes = null)
        {
            if(maxVisible < 1 || maxVisible > MaximumVisible) throw new ArgumentOutOfRangeException(nameof(maxVisible));
            if(!Finite(widthMm) || widthMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm));
            if(!Finite(heightMm) || heightMm <= 0) throw new ArgumentOutOfRangeException(nameof(heightMm));
            if(!Finite(gapMm) || gapMm < 0) throw new ArgumentOutOfRangeException(nameof(gapMm));
            // Bound arithmetic even when the caller supplies an unusable world-to-sketch scale.
            if(widthMm > 100000 || heightMm > 100000 || gapMm > 100000) throw new ArgumentOutOfRangeException(nameof(widthMm));
            var types = nativeTypes?.ToArray();
            if(types != null && (types.Length > 2000 || types.Any(string.IsNullOrWhiteSpace)))
                throw new ArgumentException("Specify native constraint type names.", nameof(nativeTypes));
            MaxVisible = maxVisible; WidthMm = widthMm; HeightMm = heightMm; GapMm = gapMm; ShowAll = showAll;
            NativeTypes = types == null ? null : Array.AsReadOnly(types.Distinct(StringComparer.Ordinal).OrderBy(t=>t,StringComparer.Ordinal).ToArray());
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public int MaxVisible { get; }
        public double WidthMm { get; }
        public double HeightMm { get; }
        public double GapMm { get; }
        public bool ShowAll { get; }
        /// <summary>Null allows all types; an empty list hides all except the selected constraint.</summary>
        public IReadOnlyList<string> NativeTypes { get; }
    }

    public sealed class NativeSketchConstraintGlyph
    {
        internal NativeSketchConstraintGlyph(NativeSketchObject constraint, string nativeType, bool selected,
            NativeSketchConstraintGlyphState state, string reason, CadPoint? anchor, CadPoint? position,
            NativeSketchConstraintAnchorSource anchorSource, bool overlap, bool associationsAvailable, string associationReason)
        {
            Constraint = constraint; NativeType = nativeType; Selected = selected; State = state; Reason = reason;
            AnchorMm = anchor; PositionMm = position; AnchorSource = anchorSource; MayOverlap = overlap;
            Label = NativeSketchConstraintRules.Label(nativeType);
            AssociationsAvailable = associationsAvailable; AssociationReason = associationReason;
        }
        /// <summary>The original parsed DTO; its Data getter retains its own defensive-copy contract.</summary>
        public NativeSketchObject Constraint { get; }
        public SketchEntityRef Reference => Constraint.Reference;
        public string NativeType { get; }
        public string Label { get; }
        public bool Selected { get; }
        public NativeSketchConstraintGlyphState State { get; }
        public string Reason { get; }
        public bool AssociationsAvailable { get; }
        public string AssociationReason { get; }
        /// <summary>Associations safe for this layout's highlight/leader rendering; never use unvalidated source data instead.</summary>
        public IReadOnlyList<SketchEntityRef> RelatedEntities => AssociationsAvailable ? Constraint.RelatedEntities : Array.Empty<SketchEntityRef>();
        /// <summary>Actual native location. A displaced label may draw a leader back to this location.</summary>
        public CadPoint? AnchorMm { get; }
        /// <summary>Label centre, present only for visible entries. This never changes CAD geometry.</summary>
        public CadPoint? PositionMm { get; }
        public NativeSketchConstraintAnchorSource AnchorSource { get; }
        public bool MayOverlap { get; }
    }

    public sealed class NativeSketchConstraintGlyphResult
    {
        internal NativeSketchConstraintGlyphResult(NativeSketchSnapshot snapshot, NativeSketchConstraintGlyphOptions options,
            List<NativeSketchConstraintGlyph> entries, bool selectedFound)
        {
            State = snapshot.State; SketchId = snapshot.SketchId; SnapshotPartial = snapshot.Partial;
            Options = options; Entries = entries.AsReadOnly(); SelectedReferenceFound = selectedFound;
        }
        public DocumentState State { get; }
        public string SketchId { get; }
        public bool SnapshotPartial { get; }
        public NativeSketchConstraintGlyphOptions Options { get; }
        public IReadOnlyList<NativeSketchConstraintGlyph> Entries { get; }
        /// <summary>False for absent, stale, foreign, or non-constraint selection. No selection is inferred.</summary>
        public bool SelectedReferenceFound { get; }
    }

    /// <summary>
    /// Deterministic, bounded annotation layout. Native annotations and validated related anchors are
    /// the only position sources; association completeness is independent of label visibility.
    /// This model supplies labels/positions, not an assertion of desktop glyph equivalence.
    /// </summary>
    public static class NativeSketchConstraintGlyphLayout
    {
        public static NativeSketchConstraintGlyphResult Build(NativeSketchSnapshot snapshot,
            NativeSketchConstraintGlyphOptions options = null, SketchEntityRef selected = null)
        {
            if(snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            options = options ?? new NativeSketchConstraintGlyphOptions();
            bool SelectionMatches(NativeSketchObject item) => selected != null && item.Reference != null
                && selected.Type == "constraint" && selected.Subpart == SketchSubpart.Entity
                && selected.Matches(snapshot.State,snapshot.SketchId) && item.Reference.EntityId == selected.EntityId
                && item.Reference.Type == selected.Type;
            var entities = snapshot.Entities.Where(e=>e.Reference != null).ToDictionary(e=>e.Reference.EntityId,StringComparer.Ordinal);
            var rows = snapshot.Constraints.Select((item,index)=>new { Item=item, Index=index, Selected=SelectionMatches(item) })
                .OrderByDescending(row=>row.Selected).ThenBy(row=>row.Item.Reference?.EntityId ?? "",StringComparer.Ordinal).ThenBy(row=>row.Index);
            var entries = new List<NativeSketchConstraintGlyph>();
            var occupied = new List<CadPoint>();
            foreach(var row in rows)
            {
                var item = row.Item; string nativeType = (string)item.Data["native_type"] ?? item.Type;
                var state = NativeSketchConstraintGlyphState.Visible; string reason = "";
                bool associationsAvailable = AssociationsCurrent(item,snapshot,out string associationReason);
                CadPoint? anchor = Anchor(item,entities,associationsAvailable,out var source), position = null;
                bool overlap = false;
                if(!item.Supported || item.Reference == null)
                { state = NativeSketchConstraintGlyphState.Unsupported; reason = item.UnavailableReason.Length > 0 ? item.UnavailableReason : "Native constraint is unsupported."; }
                else if(!row.Selected && (!options.ShowAll || (options.NativeTypes != null && !options.NativeTypes.Contains(nativeType,StringComparer.Ordinal))))
                { state = NativeSketchConstraintGlyphState.HiddenByFilter; reason = "Hidden by the constraint view filter."; }
                else if(!anchor.HasValue)
                { state = NativeSketchConstraintGlyphState.Unpositionable; reason = "No native annotation or related native anchor is available."; }
                else if(occupied.Count >= options.MaxVisible)
                { state = NativeSketchConstraintGlyphState.OmittedByBudget; reason = "Visible constraint annotation budget reached."; }
                else
                {
                    position = Place(anchor.Value,occupied,options,out overlap);
                    occupied.Add(position.Value);
                    if(overlap) reason = "All bounded label positions overlap; the constraint remains visible.";
                }
                entries.Add(new NativeSketchConstraintGlyph(item,nativeType,row.Selected,state,reason,anchor,position,source,overlap,
                    associationsAvailable,associationReason));
            }
            return new NativeSketchConstraintGlyphResult(snapshot,options,entries,entries.Any(e=>e.Selected));
        }

        private static bool AssociationsCurrent(NativeSketchObject constraint, NativeSketchSnapshot snapshot, out string reason)
        {
            reason = (string)constraint.Data["binding_reason"] ?? "Native associations are unavailable.";
            if(constraint.BindingState != "complete" || constraint.RelatedEntities.Count == 0) return false;
            if(constraint.RelatedEntities.Any(r=>!r.Matches(snapshot.State,snapshot.SketchId)))
            { reason = "Constraint associations belong to another CAD state."; return false; }
            // The schema's references are snapshot-scoped. If additional scope metadata is supplied,
            // never use contradictory metadata as a current association, even if the opaque id exists.
            foreach(var binding in (Newtonsoft.Json.Linq.JArray)constraint.Data["related_entities"])
                foreach(var scope in new[] {("document_id",snapshot.State.DocumentId),("revision",snapshot.State.Revision),("sketch_id",snapshot.SketchId)})
                    if(binding[scope.Item1] != null && (binding[scope.Item1].Type != Newtonsoft.Json.Linq.JTokenType.String
                        || (string)binding[scope.Item1] != scope.Item2))
                    { reason = "Constraint association scope metadata is stale or foreign."; return false; }
            reason = ""; return true;
        }

        private static CadPoint? Anchor(NativeSketchObject constraint, Dictionary<string,NativeSketchObject> entities, bool associationsAvailable,
            out NativeSketchConstraintAnchorSource source)
        {
            source = NativeSketchConstraintAnchorSource.None;
            if(constraint.AnnotationMm.HasValue)
            { source = NativeSketchConstraintAnchorSource.NativeAnnotation; return constraint.AnnotationMm; }
            if(!associationsAvailable) return null;
            // Prefer an explicitly related endpoint/centre over a whole-entity display anchor.
            foreach(var reference in constraint.RelatedEntities.OrderBy(r=>r.Subpart == SketchSubpart.Entity)
                .ThenBy(r=>r.EntityId,StringComparer.Ordinal).ThenBy(r=>r.Subpart))
            {
                if(!entities.TryGetValue(reference.EntityId,out var entity) || !entity.Supported || entity.Type != reference.Type) continue;
                if(reference.Subpart != SketchSubpart.Entity && entity.TryGetSubpartPoint(reference.Subpart,out var point))
                { source = NativeSketchConstraintAnchorSource.RelatedSubpart; return point; }
                if(reference.Subpart == SketchSubpart.Entity)
                    foreach(var part in new[] {SketchSubpart.Center,SketchSubpart.Point,SketchSubpart.Start,SketchSubpart.End})
                        if(entity.TryGetSubpartPoint(part,out point))
                        { source = NativeSketchConstraintAnchorSource.RelatedEntityAnchor; return point; }
            }
            return null;
        }

        private static CadPoint Place(CadPoint anchor, List<CadPoint> occupied, NativeSketchConstraintGlyphOptions options, out bool overlap)
        {
            double xStep = options.WidthMm + options.GapMm, yStep = options.HeightMm + options.GapMm;
            bool Free(CadPoint candidate) => occupied.All(p=>Math.Abs(p.X-candidate.X) >= xStep || Math.Abs(p.Y-candidate.Y) >= yStep);
            if(Free(anchor)) { overlap = false; return anchor; }
            // Four rings, eight compass offsets each: at most 33 candidates per annotation.
            var offsets = new[] {(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1),(0,-1),(1,-1)};
            for(int ring=1;ring<=4;ring++) foreach(var offset in offsets)
            {
                var candidate = new CadPoint(anchor.X + offset.Item1*ring*xStep,anchor.Y + offset.Item2*ring*yStep);
                if(Free(candidate)) { overlap = false; return candidate; }
            }
            overlap = true; return anchor;
        }
    }
}
