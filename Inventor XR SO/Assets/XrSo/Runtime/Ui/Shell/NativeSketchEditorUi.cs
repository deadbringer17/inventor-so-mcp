using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>One editor implementation for part and sheet-metal; the host owns the existing commit bar and mutation guard.</summary>
    public sealed partial class NativeSketchEditorUi : IDisposable
    {
        public const string PolygonOptionsTab = "_sketch.polygon.options";
        public const string ConstraintOptionsTab = "_sketch.constraint.options";
        public const string DimensionTab = "_sketch.dimension.variants";
        public const string DimensionObjectsTab = "_sketch.dimension.objects", DimensionEditTab = "_sketch.dimension.edit";
        public const string DimensionTextOptionsTab="_sketch.dimension.text";
        public const string ConstraintObjectsTab="_sketch.constraint.objects",ConstraintEditTab="_sketch.constraint.edit";
        public const string TransformOptionsTab="_sketch.transform.options";
        public const string OffsetTab="_sketch.offset";
        public const string PatternTab="_sketch.pattern";
        public const string SelectionTab = "_sketch.selection", FilterTab = "_sketch.selection.filters";
        private readonly Func<PaletteView> _palette;
        private readonly Func<bool> _ready;
        private readonly Func<bool> _presented;
        private readonly Func<DesignGeometryView> _geometry;
        private readonly Func<Transform> _head;
        private readonly Action _changed, _closed;
        private readonly Action<string> _notice;
        private readonly Action<NativeSketchSnapshot> _opened;
        private NumericEntry _prompt;
        private TextEntry _textPrompt;
        private bool _dimensionUseExpression;
        private string _newDimensionExpression="",_newDimensionName="";
        private int _promptGeneration;
        private bool _disposed;
        private readonly List<(string id,SketchSubpart part)> _selected = new List<(string,SketchSubpart)>();
        private readonly Dictionary<(string id,SketchSubpart part),NativeSketchHit> _selectedHits = new Dictionary<(string,SketchSubpart),NativeSketchHit>();
        private NativeSketchSnapshot _selectedHitSnapshot;
        /// <summary>Ordered original planar pointer positions. Window or refreshed selections have no point.</summary>
        public IReadOnlyList<CadPoint?> SelectedPickPoints => _selected.Select(key=>
            ReferenceEquals(_selectedHitSnapshot,Session.Snapshot) && _selectedHits.TryGetValue(key,out var hit)
                ? hit.PickPointMm : null).ToArray();
        public JObject FreezeSelectedCurveOptions(JObject options)
        {
            if(!CanSelect || !ReferenceEquals(_selectedHitSnapshot,Session.Snapshot))
                throw new InvalidOperationException("Seleziona nuovamente le due curve nello schizzo corrente.");
            return NativeSketchPickSelection.FreezeTwoCurveOptions(Session.Snapshot,
                _selected.Select(key=>_selectedHits.TryGetValue(key,out var hit) ? hit : null).ToArray(),options);
        }
        private NativeSketchHit[] _candidates = Array.Empty<NativeSketchHit>();
        private SketchEntityRef[] _constraintCandidates = Array.Empty<SketchEntityRef>();
        private NativeSketchSnapshot _candidateSnapshot;
        private bool _candidateAdditive;
        private int _pickGeneration;
        private string _selectionType;
        private SketchSubpart? _selectionPart = SketchSubpart.Entity;
        private bool _constraintAxisOne=true,_constraintAxisTwo=true;
        private CadPoint? _constraintProximity;
        private bool _dimensionDriven,_dimensionLinearDiameter,_dimensionPositiveSide;
        private NativeSketchObject _selectedDimension;
        private NativeSketchObject _selectedConstraint;
        private bool _showConstraints=true;
        private bool _transformRemoveConstraints,_rotateCopy;
        private bool _offsetConnected,_offsetConstraints=true,_offsetNatural=true;
        private bool _patternTwoDirections,_patternAssociative=true,_patternNatural=true,_patternSymmetric,_patternRectFitted,_patternCircleFitted=true,_patternExpressions;
        private bool _patternNaturalY=true,_patternSymmetricY;
        private int[] _patternSuppressed=Array.Empty<int>();
        public IReadOnlyList<SketchEntityRef> Selected => Session.Snapshot==null ? Array.Empty<SketchEntityRef>()
            : _selected.Select(s=>Session.Snapshot.Entities.Single(e=>e.Reference?.EntityId==s.id).Reference.WithSubpart(s.part)).ToArray();
        public NativeSketchSession Session { get; }
        public bool IsOpen => Session.SketchId != null;
        public bool PromptOpen => _nativeTextRuns != null || _splinePoints != null || (_prompt != null && (_palette()?.KeypadVisible ?? _prompt.Editing))
            || (_textPrompt!=null && (_palette()?.KeypadVisible ?? _textPrompt.Editing));
        public NumericEntry ActiveEntry => PromptOpen && _prompt?.Editing==true ? _prompt : null;
        public TextEntry ActiveTextEntry=>PromptOpen && _textPrompt?.Editing==true ? _textPrompt : null;
        public bool CanCreate => !_disposed && _ready() && Session.CanEdit && !Session.HasDraft && !PromptOpen && !WindowActive;

        public NativeSketchEditorUi(INativeSketchBackend backend, DesignSession mutation, Func<PaletteView> palette,
            Func<bool> ready, Func<DesignGeometryView> geometry, Func<Transform> head, Action changed,
            Action<string> notice, Action<NativeSketchSnapshot> opened, Action closed, NativeSketchCheckpoint checkpoint = null,
            Func<bool> presented = null)
        {
            _palette = palette; _ready = ready; _presented = presented ?? ready; _geometry = geometry; _head = head; _changed = changed;
            _notice = notice; _opened = opened; _closed = closed;
            Session = new NativeSketchSession(backend, mutation: mutation);
            _parameterBackend = backend as IDesignWorkspaceBackend;
            if (checkpoint != null && checkpoint.SketchId != null) Session.Restore(checkpoint);
            Session.Changed += OnChanged;
        }
        public void SetEnvironment(DocumentState state, bool online, bool isPart)
        {
            bool current = Session.Snapshot?.State.DocumentId == state?.DocumentId && Session.Snapshot?.State.Revision == state?.Revision;
            if (!online || !isPart || !current) { ResetParameterResources(); ResetImportResources(); ResetWindow(false); CancelPrompt(); CancelCandidates(); ClearSelected(); _selectedDimension=null;_selectedConstraint=null; }
            Session.SetEnvironment(state,online,isPart);
        }
        public async Task OpenAsync(string sketchId)
        {
            ResetParameterResources(); ResetImportResources(); ResetWindow(false); CancelPrompt(); CancelCandidates(); ClearSelected(); _selectedDimension=null;_selectedConstraint=null; await Session.OpenAsync(sketchId);
            if (_disposed) return;
            if (!Session.NeedsRefresh && Session.Snapshot != null) _opened(Session.Snapshot);
            if (!string.IsNullOrEmpty(Session.Error)) _notice(Session.Error);
            else if (Session.Snapshot?.Partial == true) _notice(Session.Snapshot.NativeMutationComplete
                ? "Immagini di sola lettura: puoi modificare le altre geometrie; editing immagini non disponibile."
                : "Schizzo parziale: modifica disabilitata, dati non supportati da verificare.");
            Render(); _changed();
        }
        public void Render()
        {
            RefreshParameterContextState();
            RefreshImportContext();
            if (_disposed || !_presented() || !IsOpen || _geometry() == null) return;
            if (Session.Mutation.Status == DesignStatus.PreviewReady) { _geometry().Clear(); return; }
            var snapshot = Session.Snapshot;
            if (snapshot != null) _geometry().ShowNative(snapshot,_head(),
                _selectedConstraint!=null ? Selected.Concat(new[] {_selectedConstraint.Reference})
                    : _selectedDimension==null ? Selected : Selected.Concat(new[] {_selectedDimension.Reference}),_showConstraints,_constraintNativeTypes);
        }
        public bool Pick(CadPoint point, bool additive = false)
        {
            if (PickCoordinate(point)) return true;
            if (WindowActive) return StartWindow(point, additive);
            if (!CanSelect) return false;
            _selectedDimension=null;_selectedConstraint=null;
            CancelCandidates();
            var hits = Session.Snapshot.PickTargets(point,2,_selectionType,_selectionPart);
            if (hits.Count > 1)
            {
                _candidates=hits.ToArray(); _candidateSnapshot=Session.Snapshot; _candidateAdditive=additive;
                _changed(); _palette()?.ShowTabGroup(new[] {SelectionTab},true);
                _notice("Geometrie sovrapposte: scegli uno dei " + hits.Count + " candidati.");
                return true;
            }
            if (!additive) ClearSelected();
            if (hits.Count == 1) Select(hits[0],additive);
            Render(); _changed(); return hits.Count > 0;
        }
        /// <summary>Queries the displayed label rectangles only; coordinate entry and selection windows retain priority.</summary>
        public bool PickConstraint(Ray ray)
        {
            if(!CanSelect || (_selectionType!=null && _selectionType!="constraint")
                || (_selectionPart.HasValue && _selectionPart!=SketchSubpart.Entity)) return false;
            var hits=_geometry()?.PickConstraintLabels(Session.Snapshot,ray) ?? Array.Empty<SketchEntityRef>();
            if(hits.Count==0) return false;
            CancelCandidates();
            if(hits.Count==1) SelectConstraint(Session.Snapshot.Constraints.Single(c=>c.Reference?.EntityId==hits[0].EntityId));
            else
            {
                _constraintCandidates=hits.ToArray();_candidateSnapshot=Session.Snapshot;
                _changed();_palette()?.ShowTabGroup(new[] {SelectionTab},true);
                _notice("Etichette sovrapposte: scegli uno dei "+hits.Count+" vincoli.");
            }
            return true;
        }
        private void SelectConstraint(NativeSketchObject constraint)
        {
            ClearSelected();CancelCandidates();_selectedDimension=null;_selectedConstraint=constraint;Render();_changed();
            if(_geometry()?.ConstraintLayout?.Entries.SingleOrDefault(g=>g.Reference?.EntityId==constraint.Reference?.EntityId)?.AssociationsAvailable!=true)
                _notice("Associazioni native non disponibili per questo vincolo.");
            if(!Session.NeedsReferenceSelection) _palette()?.ShowTabGroup(new[] {ConstraintEditTab},true);
        }
        private bool CanSelect => CanSelectCore && !WindowActive;
        private bool CanSelectCore => !_disposed && _ready() && Session.CanEdit && (!Session.HasDraft || Session.NeedsReferenceSelection)
            && !PromptOpen && Session.Mutation.Status==DesignStatus.Empty;
        private NativeSketchObject[] FormatSelection(string variant,bool reconfirming=false)
        {
            if(!CanSelect || !Session.Snapshot.SupportsVariant(variant) || (Session.HasDraft && !reconfirming) || Selected.Count==0) return Array.Empty<NativeSketchObject>();
            var selected=Selected;
            if(selected.Any(r=>!r.Matches(Session.Snapshot.State,Session.SketchId)
                || (r.Subpart!=SketchSubpart.Entity && !(variant=="format_hole_center" && r.Subpart==SketchSubpart.Point))))
                return Array.Empty<NativeSketchObject>();
            var entities=selected.Select(r=>Session.Snapshot.Entities.Single(e=>e.Reference.EntityId==r.EntityId)).ToArray();
            if(entities.Select(e=>e.Reference.EntityId).Distinct(StringComparer.Ordinal).Count()!=entities.Length) return Array.Empty<NativeSketchObject>();
            string property=variant=="format_construction" ? "construction" : variant=="format_centerline" ? "centerline" : "hole_center";
            if(entities.Any(e=>!e.Selectable || !(e.Data[property] is JValue value) || value.Type!=JTokenType.Boolean
                || (variant=="format_centerline" && e.Type!="line") || (variant=="format_hole_center" && e.Type!="point")))
                return Array.Empty<NativeSketchObject>();
            return entities;
        }
        private bool CanReconfirmSelection
        {
            get
            {
                if(!CanSelect || !Session.NeedsReferenceSelection) return false;
                string variant=(string)Session.Draft[0]["arguments"]["variant"];
                if(variant=="line_midpoint") return MidpointSelection(NativeSketchMidpointRules.Roles((JObject)Session.Draft[0]["arguments"]["options"]),true);
                if(variant=="dimension_update" || variant=="dimension_delete") return DimensionEditSelection(variant,true);
                if(variant=="constraint_delete") return ConstraintDeleteSelection(true);
                if(variant==NativeSketchSplinePointRules.Variant) return SplinePointSelection(true);
                if(variant==NativeSketchSplineTensionRules.Variant) return SplineTensionSelection(true);
                if(variant=="text_update" || variant=="text_delete") return TextSelection(variant,true)!=null;
                if(variant=="offset_distance" || variant=="offset_point") return OffsetSelection(variant,true);
                if(variant=="pattern_update" || variant=="pattern_delete") return PatternEditSelection(variant,true);
                if(variant=="pattern_rectangular" || variant=="pattern_circular") return PatternSelection(variant,true);
                if(variant.StartsWith("transform_",StringComparison.Ordinal)) return TransformSelection(variant,true);
                if(variant.StartsWith("constraint_",StringComparison.Ordinal)) return ConstraintSelection(variant.Substring(11),true);
                if(variant.StartsWith("dimension_",StringComparison.Ordinal)) return DimensionSelection(variant.Substring(10),true);
                return FormatSelection(variant,true).Length>0;
            }
        }
        private bool DimensionSelection(string kind,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant("dimension_"+kind) && NativeSketchDimensionRules.Variants(Selected).Contains(kind)
            && (reconfirming || _dimensionDriven || !_dimensionUseExpression || !string.IsNullOrWhiteSpace(_newDimensionExpression));
        private bool DimensionEditSelection(string variant,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant(variant) && _selectedDimension?.Selectable==true
            && _selectedDimension.Reference.Matches(Session.Snapshot.State,Session.SketchId)
            && Session.Snapshot.Dimensions.Any(d=>ReferenceEquals(d,_selectedDimension));
        private bool ConstraintDeleteSelection(bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant("constraint_delete") && _selectedConstraint?.Selectable==true
            && (bool?)_selectedConstraint.Data["deletable"]==true
            && _selectedConstraint.Reference.Matches(Session.Snapshot.State,Session.SketchId)
            && Session.Snapshot.Constraints.Any(c=>ReferenceEquals(c,_selectedConstraint));
        private bool TransformSelection(string variant,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant(variant) && Selected.Count>0 && Selected.Count<=2000
            && (variant!="transform_rotate" || Selected.All(r=>r.Type!="text"))
            && (variant!="transform_rotate" || !(reconfirming ? (bool?)Session.Draft?[0]?["arguments"]?["options"]?["copy"]==true : _rotateCopy)
                || Session.Snapshot.SupportsVariant("transform_copy"))
            && (variant=="transform_stretch" ? NativeSketchStretchRules.Accepts(Selected)
                : Selected.Select(r=>r.EntityId).Distinct(StringComparer.Ordinal).Count()==Selected.Count
                    && Selected.All(r=>r.Subpart==SketchSubpart.Entity || (r.Type=="point" && r.Subpart==SketchSubpart.Point)));
        private bool OffsetSelection(string variant,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant(variant) && NativeSketchOffsetRules.Accepts(Selected,
                reconfirming ? (bool?)Session.Draft[0]["arguments"]["options"]["include_connected"] ?? false : _offsetConnected);
        private bool PatternSelection(string variant,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant(variant) && NativeSketchPatternRules.Accepts(variant,Selected,
                reconfirming ? (bool?)Session.Draft[0]["arguments"]["options"]["two_directions"] ?? false : _patternTwoDirections);
        private bool PatternEditSelection(string variant,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant(variant) && _selectedConstraint?.Selectable==true
            && _selectedConstraint.Reference.Matches(Session.Snapshot.State,Session.SketchId)
            && Session.Snapshot.Constraints.Any(c=>ReferenceEquals(c,_selectedConstraint)) && _selectedConstraint.Data["pattern_definition"] is JObject;
        private void SetPatternChange(JObject changes,string variant="pattern_update")
        {
            if(!PatternEditSelection(variant)) return;
            changes["references"]=new JArray(_selectedConstraint.Reference.ToJson());CancelCandidates();Session.SetOperation(variant,changes);
            _notice("Modifica serie pronta: Anteprima, poi Applica.");_changed();
        }
        private void SetDimensionEdit(JObject changes,string variant="dimension_update")
        {
            if(!DimensionEditSelection(variant)) return;
            changes["references"]=new JArray(_selectedDimension.Reference.ToJson());
            CancelCandidates();Session.SetOperation(variant,changes);_notice("Modifica quota pronta: Anteprima, poi Applica.");_changed();
        }
        private bool ConstraintSelection(string kind,bool reconfirming=false)=>CanSelect && (!Session.HasDraft || reconfirming)
            && Session.Snapshot.SupportsVariant("constraint_"+kind) && NativeSketchConstraintRules.Accepts(kind,Selected);
        private void SetConstraint(string kind)
        {
            if(!ConstraintSelection(kind)) return;
            var refs=new JArray(Selected.Select(r=>r.ToJson())); CancelCandidates();
            var options=new JObject {["references"]=refs,
                ["ellipse_one_major_axis"]=_constraintAxisOne,["ellipse_two_major_axis"]=_constraintAxisTwo};
            if((kind=="tangent" || kind=="smooth") && _constraintProximity.HasValue)
                options["proximity_mm"]=new JArray(_constraintProximity.Value.X,_constraintProximity.Value.Y);
            Session.SetOperation("constraint_"+kind,options);
            _notice("Vincolo pronto: Anteprima, poi Applica."); _changed();
        }
        private void SetFormat(string variant)
        {
            var entities=FormatSelection(variant); if(entities.Length==0) return;
            string property=variant=="format_construction" ? "construction" : variant=="format_centerline" ? "centerline" : "hole_center";
            var references=new JArray(Selected.Select(r=>r.ToJson()));
            bool value=!entities.All(e=>(bool)e.Data[property]);
            CancelCandidates(); Session.SetOperation(variant,new JObject { ["references"]=references,["value"]=value });
            _notice("Formato pronto: Anteprima, poi Applica."); _changed();
        }
        private void CancelCandidates() { _pickGeneration++; _candidates=Array.Empty<NativeSketchHit>(); _constraintCandidates=Array.Empty<SketchEntityRef>(); _candidateSnapshot=null; }
        private void ClearSelected() { _selected.Clear();_selectedHits.Clear();_selectedHitSnapshot=null; }
        private void Select(NativeSketchHit hit,bool additive)
        {
            if (!additive) ClearSelected();
            if(!additive || !ReferenceEquals(_selectedHitSnapshot,Session.Snapshot)) _selectedHits.Clear();
            _selectedHitSnapshot=Session.Snapshot;
            var key=(hit.Reference.EntityId,hit.Reference.Subpart);
            if (_selected.Contains(key)) { _selected.Remove(key);_selectedHits.Remove(key); }
            else { _selected.Add(key);_selectedHits[key]=hit; }
            _notice("Selezionata " + TypeLabel(hit.Entity.Type) + " • " + PartLabel(hit.Reference.Subpart) + ".");
        }
        private static string PartLabel(SketchSubpart? part)
        { switch(part) { case null:return "Geometrie e sottoparti"; case SketchSubpart.Entity:return "Geometria intera";
            case SketchSubpart.Start:return "Estremo iniziale"; case SketchSubpart.End:return "Estremo finale";
            case SketchSubpart.Center:return "Centro"; case SketchSubpart.Point:return "Punto"; default:return "Sottoparte"; } }
        private static string TypeLabel(string type)
        {
            switch(type) { case "line": return "linea"; case "circle": return "cerchio"; case "arc": return "arco";
                case "ellipse": return "ellisse"; case "point": return "punto"; case "text": return "testo";
                case "constraint": return "vincolo";
                case "control_point_spline": return "spline a vertici di controllo";
                case "spline_fit": case "spline_control": case "spline": return "spline"; default: return type; }
        }
        public void CancelPrompt()
        {
            CancelParameterInput();
            ResetNativeTextInput();
            bool spline = _splinePoints != null;
            _splinePoints=null;_splineSnapshot=null;
            ResetCoordinatePick();
            _promptGeneration++;if(_prompt==null && _textPrompt==null && !spline) return;
            _prompt?.CancelEdit();_textPrompt?.CancelEdit();_prompt=null;_textPrompt=null;_palette()?.HideKeypad();
        }
        public void DiscardOperation()
        { ResetWindow(false); CancelPrompt(); CancelCandidates(); Session.DiscardOperation(); Render(); _changed(); }
        public void Close()
        { ResetParameterResources(); ResetImportResources(); ResetWindow(false); CancelPrompt(); CancelCandidates(); Session.Close(); ClearSelected(); _selectedDimension=null;_selectedConstraint=null; _geometry()?.Clear(); _closed(); _changed(); }
        private void OnChanged() { if (_disposed) return; RefreshParameterContextState(); if(!ReferenceEquals(_selectedHitSnapshot,Session.Snapshot)) { _selectedHits.Clear();_selectedHitSnapshot=null; } if(_nativeTextRuns!=null && !NativeTextCurrent) CancelPrompt(); if(_splinePoints!=null && !SplineCurrent) CancelPrompt(); if (!CanSelectCore || WindowActive && !ReferenceEquals(_windowSnapshot,Session.Snapshot)) ResetWindow(false); if (!CanSelect) CancelCandidates(); Render(); _changed(); }

        private static string ConstraintLabel(NativeSketchObject constraint)=>NativeSketchConstraintRules.Label((string)constraint.Data["native_type"]);
        public IEnumerable<XrAction> Actions()
        {
            foreach (var parameterAction in ParameterActions()) yield return parameterAction;
            foreach (var textAction in NativeTextActions()) yield return textAction;
            foreach (var importAction in ImportResourceActions()) yield return importAction;
            foreach (var splineAction in SplineActions()) yield return splineAction;
            foreach (var midpointAction in MidpointActions()) yield return midpointAction;
            foreach (var windowAction in WindowActions()) yield return windowAction;
            yield return new XrAction("sketch.pattern.options","Opzioni serie ▸",SketchCommandCatalog.TabPrefix+"Serie",()=>CanCreate,
                ()=>_palette()?.ShowTabGroup(new[] {PatternTab},true),()=>"Completa la bozza corrente.");
            yield return new XrAction("sketch.pattern.expressions","Input tramite espressioni",PatternTab,()=>CanCreate,
                ()=>{_patternExpressions=!_patternExpressions;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_patternExpressions);
            foreach(var item in new[] {("two","Due direzioni rettangolari"),("associative","Serie associativa"),("natural","Direzione naturale X / asse"),("symmetric","Simmetria X / asse"),("natural_y","Direzione naturale Y"),("symmetric_y","Simmetria Y"),("rect_fitted","Rettangolare: distanza totale"),("circle_fitted","Circolare: angolo totale")})
            {
                var option=item;
                bool Value()=>option.Item1=="two" ? _patternTwoDirections : option.Item1=="associative" ? _patternAssociative : option.Item1=="natural" ? _patternNatural
                    : option.Item1=="symmetric" ? _patternSymmetric : option.Item1=="natural_y" ? _patternNaturalY : option.Item1=="symmetric_y" ? _patternSymmetricY : option.Item1=="rect_fitted" ? _patternRectFitted : _patternCircleFitted;
                bool y=option.Item1=="natural_y" || option.Item1=="symmetric_y";
                yield return new XrAction("sketch.pattern."+option.Item1,option.Item2,PatternTab,()=>CanCreate && (!y || _patternTwoDirections),()=>
                {if(option.Item1=="two") _patternTwoDirections=!_patternTwoDirections;else if(option.Item1=="associative") _patternAssociative=!_patternAssociative;
                 else if(option.Item1=="natural") _patternNatural=!_patternNatural;else if(option.Item1=="symmetric") _patternSymmetric=!_patternSymmetric;
                 else if(option.Item1=="natural_y") _patternNaturalY=!_patternNaturalY;else if(option.Item1=="symmetric_y") _patternSymmetricY=!_patternSymmetricY;
                 else if(option.Item1=="rect_fitted") _patternRectFitted=!_patternRectFitted;else _patternCircleFitted=!_patternCircleFitted;_changed();},
                    ()=>y && !_patternTwoDirections ? "Attiva due direzioni rettangolari." : "Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:Value);
            }
            yield return new XrAction("sketch.pattern.suppressed","Elementi soppressi ▸",PatternTab,()=>CanCreate,
                ()=>PromptPatternSuppression(),()=>"Completa la bozza corrente.");
            yield return new XrAction("sketch.pattern.suppressed.clear","Nessuna soppressione",PatternTab,()=>CanCreate,
                ()=>{_patternSuppressed=Array.Empty<int>();_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_patternSuppressed.Length==0);
            foreach(var definition in new[] {("SketchRectangularPatternCmd","pattern_rectangular"),("SketchCircularPatternCmd","pattern_circular")})
            {
                var pattern=definition;var command=SketchCommandCatalog.Commands.Single(c=>c.NativeId==pattern.Item1);
                yield return new XrAction(command.Id,command.Label,command.Tab,()=>PatternSelection(pattern.Item2),()=>PromptPattern(pattern.Item2),
                    ()=>VariantReason(pattern.Item2,"Seleziona prima le sorgenti, poi la linea/le linee direzione o il punto/arco/cerchio asse."));
            }
            yield return new XrAction("sketch.pattern.edit.parameters","Modifica parametri serie",ConstraintEditTab,()=>PatternEditSelection("pattern_update"),
                ()=>PromptPattern("pattern_update"),()=>"Seleziona una serie nativa aggiornata.");
            yield return new XrAction("sketch.pattern.edit.expressions","Modifica espressioni serie",ConstraintEditTab,()=>PatternEditSelection("pattern_update"),
                ()=>PromptPattern("pattern_update",true),()=>"Seleziona una serie nativa aggiornata.");
            yield return new XrAction("sketch.pattern.edit.suppressed","Elementi soppressi serie ▸",ConstraintEditTab,()=>PatternEditSelection("pattern_update"),
                ()=>PromptPatternSuppression(true),()=>"Seleziona una serie nativa aggiornata.");
            yield return new XrAction("sketch.pattern.edit.unsuppress","Riattiva tutti gli elementi",ConstraintEditTab,()=>PatternEditSelection("pattern_update"),
                ()=>SetPatternChange(new JObject {["suppressed_elements"]=new JArray()}),()=>"Seleziona una serie nativa aggiornata.");
            yield return new XrAction("sketch.pattern.edit.delete","Elimina serie nativa",ConstraintEditTab,()=>PatternEditSelection("pattern_delete"),
                ()=>SetPatternChange(new JObject(),"pattern_delete"),()=>"Seleziona una serie nativa aggiornata.");
            var offsetCommand=SketchCommandCatalog.Commands.Single(c=>c.NativeId=="SketchOffsetCmd");
            yield return new XrAction(offsetCommand.Id,"Offset ▸",offsetCommand.Tab,
                ()=>CanCreate && (Session.Snapshot.SupportsVariant("offset_distance") || Session.Snapshot.SupportsVariant("offset_point")),
                ()=>_palette()?.ShowTabGroup(new[] {OffsetTab},true),()=>VariantReason("offset_distance","Apri uno schizzo aggiornato."));
            yield return new XrAction("sketch.offset.connected","Includi catena connessa",OffsetTab,()=>CanCreate,
                ()=>{_offsetConnected=!_offsetConnected;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_offsetConnected);
            yield return new XrAction("sketch.offset.constraints","Crea vincoli offset",OffsetTab,()=>CanCreate,
                ()=>{_offsetConstraints=!_offsetConstraints;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_offsetConstraints);
            yield return new XrAction("sketch.offset.natural","Direzione naturale",OffsetTab,()=>CanCreate,
                ()=>{_offsetNatural=!_offsetNatural;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_offsetNatural);
            foreach(var byPoint in new[] {false,true})
            {
                bool point=byPoint;string variant=point ? "offset_point" : "offset_distance";
                yield return new XrAction("sketch."+variant,point ? "Offset per punto" : "Offset per distanza",OffsetTab,
                    ()=>OffsetSelection(variant),()=>PromptOffset(point),()=>VariantReason(variant,
                        "Seleziona curve intere nell'ordine di connessione; con catena automatica scegli solo la curva base."));
            }
            yield return new XrAction("sketch.transform.options","Opzioni Modifica ▸",SketchCommandCatalog.TabPrefix+"Modifica",()=>CanCreate,
                ()=>_palette()?.ShowTabGroup(new[] {TransformOptionsTab},true),()=>"Completa la bozza corrente.");
            yield return new XrAction("sketch.transform.remove_constraints","Consenti rimozione vincoli",TransformOptionsTab,()=>CanCreate,
                ()=>{_transformRemoveConstraints=!_transformRemoveConstraints;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_transformRemoveConstraints);
            yield return new XrAction("sketch.transform.rotate_copy","Copia durante Ruota",TransformOptionsTab,
                ()=>CanCreate && (_rotateCopy || Session.Snapshot.SupportsVariant("transform_copy")),
                ()=>{_rotateCopy=!_rotateCopy;_changed();},()=>"La copia richiede il supporto del backend e una bozza libera.",kind:XrActionKind.Toggle,isOn:()=>_rotateCopy);
            foreach(var definition in new[] {("SketchMoveCmd","transform_move"),("SketchCopyCmd","transform_copy"),("SketchRotateCmd","transform_rotate"),("SketchStretchCmd","transform_stretch")})
            {
                var transform=definition;var command=SketchCommandCatalog.Commands.Single(c=>c.NativeId==transform.Item1);
                yield return new XrAction(command.Id,command.Label,command.Tab,()=>TransformSelection(transform.Item2),
                    ()=>PromptTransform(transform.Item2,command.Label),()=>VariantReason(transform.Item2,
                        transform.Item2=="transform_rotate" && Selected.Any(r=>r.Type=="text") ? "Rotazione del testo temporaneamente non disponibile: orientamento nativo ancora da verificare."
                        : transform.Item2=="transform_stretch" ? "Seleziona curve intere, estremi, centri o punti nativi nella revisione attuale."
                        : "Seleziona oggetti interi nella revisione attuale; estremi e centri richiedono altri strumenti."));
            }
            var show=SketchCommandCatalog.Commands.Single(c=>c.NativeId=="SketchShowAllConstraintsCmd");
            yield return new XrAction(show.Id,show.Label,show.Tab,()=>IsOpen && Session.Snapshot!=null && !PromptOpen,
                ()=>{CancelCandidates();_showConstraints=!_showConstraints;Render();_changed();},()=>"Apri uno schizzo nativo.",kind:XrActionKind.Toggle,isOn:()=>_showConstraints);
            foreach(var action in ConstraintVisibilityActions()) yield return action;
            yield return new XrAction("sketch.constraint.existing","Vincoli esistenti ▸",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanSelect && Session.Snapshot.Constraints.Any(c=>c.Selectable && c.Reference!=null),
                ()=>_palette()?.ShowTabGroup(new[] {ConstraintObjectsTab},true),()=>"Apri uno schizzo aggiornato con vincoli nativi.");
            int constraintOrdinal=0;
            foreach(var item in Session.Snapshot?.Constraints ?? Array.Empty<NativeSketchObject>())
            {
                var constraint=item;var snapshot=Session.Snapshot;
                yield return new XrAction("sketch.constraint.pick."+constraint.Reference?.EntityId,
                    "Vincolo "+(++constraintOrdinal)+": "+ConstraintLabel(constraint),ConstraintObjectsTab,
                    ()=>CanSelect && constraint.Selectable && constraint.Reference!=null && ReferenceEquals(snapshot,Session.Snapshot),
                    ()=>SelectConstraint(constraint),
                    ()=>"Ricarica i vincoli nella revisione attuale.",isOn:()=>ReferenceEquals(constraint,_selectedConstraint));
            }
            yield return new XrAction("sketch.constraint.edit.delete","Elimina vincolo",ConstraintEditTab,()=>ConstraintDeleteSelection(),
                ()=>{var reference=_selectedConstraint.Reference;CancelCandidates();Session.SetOperation("constraint_delete",
                    new JObject {["references"]=new JArray(reference.ToJson())});_notice("Eliminazione vincolo pronta: Anteprima, poi Applica.");},
                ()=>VariantReason("constraint_delete","Seleziona un vincolo aggiornato che Inventor consente di eliminare."));
            yield return new XrAction("sketch.dimension.text.options","Nuove quote: espressione / nome",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanCreate,()=>_palette()?.ShowTabGroup(new[] {DimensionTextOptionsTab},true),()=>"Completa la bozza corrente.");
            yield return new XrAction("sketch.dimension.text.use","Usa espressione",DimensionTextOptionsTab,()=>CanCreate,
                ()=>{_dimensionUseExpression=!_dimensionUseExpression;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_dimensionUseExpression);
            yield return new XrAction("sketch.dimension.text.expression","Espressione con unità",DimensionTextOptionsTab,()=>CanCreate,
                ()=>PromptNewDimensionText(false),()=>"Completa la bozza corrente.");
            yield return new XrAction("sketch.dimension.text.name","Nome della prossima quota",DimensionTextOptionsTab,()=>CanCreate,
                ()=>PromptNewDimensionText(true),()=>"Completa la bozza corrente.");
            yield return new XrAction("sketch.dimension.text.name.auto","Nome automatico",DimensionTextOptionsTab,()=>CanCreate,
                ()=>{_newDimensionName="";_changed();},()=>"Completa la bozza corrente.",isOn:()=>_newDimensionName.Length==0);
            yield return new XrAction("sketch.dimension.edit.expression","Modifica espressione",DimensionEditTab,
                ()=>DimensionEditSelection("dimension_update") && (bool?)_selectedDimension.Data["driven"]==false,
                ()=>PromptDimensionText(false),()=>VariantReason("dimension_update","Seleziona una quota driving aggiornata."));
            yield return new XrAction("sketch.dimension.edit.name","Nome parametro",DimensionEditTab,
                ()=>DimensionEditSelection("dimension_update"),()=>PromptDimensionText(true),
                ()=>VariantReason("dimension_update","Seleziona una quota nativa aggiornata."));
            yield return new XrAction("sketch.dimension.existing","Quote esistenti ▸",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanSelect && Session.Snapshot.Dimensions.Any(d=>d.Selectable && d.Reference!=null),
                ()=>_palette()?.ShowTabGroup(new[] {DimensionObjectsTab},true),()=>"Apri uno schizzo aggiornato con quote native.");
            foreach(var item in Session.Snapshot?.Dimensions ?? Array.Empty<NativeSketchObject>())
            {
                var dimension=item;var snapshot=Session.Snapshot;
                string name=(string)dimension.Data["name"] ?? "Quota";
                string expression=(string)dimension.Data["expression"] ?? "";
                if(expression.Length>60) expression=expression.Substring(0,57)+"…";
                yield return new XrAction("sketch.dimension.pick."+dimension.Reference?.EntityId,name+" = "+expression,DimensionObjectsTab,
                    ()=>CanSelect && dimension.Selectable && dimension.Reference!=null && ReferenceEquals(snapshot,Session.Snapshot),
                    ()=>{ClearSelected();CancelCandidates();_selectedConstraint=null;_selectedDimension=dimension;Render();_changed();
                        if(!Session.NeedsReferenceSelection) _palette()?.ShowTabGroup(new[] {DimensionEditTab},true);},
                    ()=>"Ricarica le quote nella revisione attuale.",isOn:()=>ReferenceEquals(dimension,_selectedDimension));
            }
            yield return new XrAction("sketch.dimension.edit.value","Modifica valore",DimensionEditTab,
                ()=>DimensionEditSelection("dimension_update") && (bool?)_selectedDimension.Data["driven"]==false
                    && (_selectedDimension.Data["value_mm"]!=null || _selectedDimension.Data["value_deg"]!=null),
                ()=>PromptExistingDimension(false),()=>VariantReason("dimension_update","Seleziona una quota driving aggiornata; le quote di riferimento misurano la geometria."));
            yield return new XrAction("sketch.dimension.edit.position","Posizione testo",DimensionEditTab,
                ()=>DimensionEditSelection("dimension_update") && _selectedDimension.Data["text_mm"] is JArray,
                ()=>PromptExistingDimension(true),()=>VariantReason("dimension_update","Seleziona una quota nativa aggiornata."));
            yield return new XrAction("sketch.dimension.edit.driven","Quota di riferimento",DimensionEditTab,
                ()=>DimensionEditSelection("dimension_update"),()=>SetDimensionEdit(new JObject {["driven"]=(bool?)_selectedDimension.Data["driven"]!=true}),
                ()=>VariantReason("dimension_update","Seleziona una quota nativa aggiornata."),kind:XrActionKind.Toggle,isOn:()=>(bool?)_selectedDimension?.Data["driven"]==true);
            yield return new XrAction("sketch.dimension.edit.delete","Elimina quota",DimensionEditTab,
                ()=>DimensionEditSelection("dimension_delete"),()=>SetDimensionEdit(new JObject(),"dimension_delete"),
                ()=>VariantReason("dimension_delete","Seleziona una quota nativa aggiornata: verifica l'Anteprima prima di Applica."));
            var general=SketchCommandCatalog.Commands.Single(c=>c.NativeId=="SketchGeneralDimensionCmd");
            yield return new XrAction(general.Id,general.Label,general.Tab,
                ()=>CanSelect && !Session.HasDraft && NativeSketchDimensionRules.Variants(Selected).Any(k=>Session.Snapshot.SupportsVariant("dimension_"+k)),
                ()=>_palette()?.ShowTabGroup(new[] {DimensionTab},true),()=>"Seleziona geometrie o punti compatibili nella revisione attuale.");
            foreach(var definition in new[] {("aligned","Allineata"),("horizontal","Orizzontale"),("vertical","Verticale"),
                ("angle_two_lines","Angolo fra linee"),("angle_three_points","Angolo fra tre punti"),("radius","Raggio"),
                ("diameter","Diametro"),("arc_length","Lunghezza arco"),("ellipse_major","Raggio maggiore ellisse"),
                ("ellipse_minor","Raggio minore ellisse"),("offset","Distanza dalla linea"),
                ("tangent_distance","Distanza tangente"),("offset_spline","Offset spline")})
            {
                var choice=definition;
                yield return new XrAction("sketch.dimension."+choice.Item1,choice.Item2,DimensionTab,()=>DimensionSelection(choice.Item1),
                    ()=>PromptDimension(choice.Item1,choice.Item2),()=>VariantReason("dimension_"+choice.Item1,"Seleziona geometrie compatibili, nell'ordine della quota."));
            }
            yield return new XrAction("sketch.dimension.driven","Quota di riferimento",DimensionTab,()=>CanCreate,
                ()=>{_dimensionDriven=!_dimensionDriven;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_dimensionDriven);
            yield return new XrAction("sketch.dimension.linear_diameter","Diametro lineare",DimensionTab,()=>CanCreate,
                ()=>{_dimensionLinearDiameter=!_dimensionLinearDiameter;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_dimensionLinearDiameter);
            yield return new XrAction("sketch.dimension.positive_side","Lato positivo ellisse",DimensionTab,()=>CanCreate,
                ()=>{_dimensionPositiveSide=!_dimensionPositiveSide;_changed();},()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>_dimensionPositiveSide);
            yield return new XrAction("sketch.constraint.options","Assi ellissi / estremo G2",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanCreate,()=>_palette()?.ShowTabGroup(new[] {ConstraintOptionsTab},true),()=>"Apri uno schizzo aggiornato e completa la bozza corrente.");
            foreach(int number in new[] {1,2})
            {
                int selectedAxis=number;
                yield return new XrAction("sketch.constraint.axis."+number,"Ellisse "+number+": asse maggiore",ConstraintOptionsTab,
                    ()=>CanCreate,()=> {if(selectedAxis==1) _constraintAxisOne=!_constraintAxisOne; else _constraintAxisTwo=!_constraintAxisTwo; _changed();},
                    ()=>"Completa la bozza corrente.",kind:XrActionKind.Toggle,isOn:()=>selectedAxis==1 ? _constraintAxisOne : _constraintAxisTwo);
            }
            yield return new XrAction("sketch.constraint.proximity.auto","Estremo automatico",ConstraintOptionsTab,()=>CanCreate,
                ()=> {_constraintProximity=null;_changed();},()=>"Completa la bozza corrente.",isOn:()=>!_constraintProximity.HasValue);
            yield return new XrAction("sketch.constraint.proximity.point","Indica estremo vicino",ConstraintOptionsTab,()=>CanCreate,
                PromptConstraintProximity,()=>"Completa la bozza corrente.",isOn:()=>_constraintProximity.HasValue);
            foreach(var constraint in new[] {("SketchCoincidentConstraintCmd","coincident"),("SketchParallelConstraintCmd","parallel"),
                ("SketchTangentConstraintCmd","tangent"),("SketchColinearConstraintCmd","collinear"),("SketchPerpendicularConstraintCmd","perpendicular"),
                ("SketchCurvatureConstraintCmd","smooth"),("SketchConcentricConstraintCmd","concentric"),("SketchHorizontalConstraintCmd","horizontal"),
                ("SketchSymmetricConstraintCmd","symmetry"),("SketchFixConstraintCmd","ground"),("SketchVerticalConstraintCmd","vertical"),("SketchEqualConstraintCmd","equal")})
            {
                var definition=constraint; var command=SketchCommandCatalog.Commands.Single(c=>c.NativeId==definition.Item1);
                yield return new XrAction(command.Id,command.Label,command.Tab,()=>ConstraintSelection(definition.Item2),
                    ()=>SetConstraint(definition.Item2),()=>VariantReason("constraint_"+definition.Item2,
                        "Seleziona geometrie/sottoparti compatibili nell'ordine richiesto; per Simmetrico scegli l'asse come terza linea."));
            }
            yield return new XrAction("sketch.editor.reselect.confirm","Conferma nuove selezioni",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanReconfirmSelection,()=>
                { string variant=(string)Session.Draft[0]["arguments"]["variant"];
                    Session.ReconfirmSelection(variant=="constraint_delete" || variant=="pattern_update" || variant=="pattern_delete" ? new[] {_selectedConstraint.Reference}
                        : variant=="dimension_update" || variant=="dimension_delete" ? new[] {_selectedDimension.Reference} : Selected);
                    _notice("Selezioni riconfermate; opzioni conservate. Esegui una nuova Anteprima."); },
                ()=>"Ricarica lo schizzo, seleziona le geometrie attuali e riconferma la bozza conservata.");
            foreach(var format in new[] {("SketchConstructionCmd","format_construction","construction"),
                ("SketchCenterlineCmd","format_centerline","centerline"),("SketchHoleCenterCmd","format_hole_center","hole_center")})
            {
                var definition=format; var command=SketchCommandCatalog.Commands.Single(c=>c.NativeId==definition.Item1);
                yield return new XrAction(command.Id,command.Label,command.Tab,()=>FormatSelection(definition.Item2).Length>0,
                    ()=>SetFormat(definition.Item2),()=>VariantReason(definition.Item2,"Seleziona geometrie intere aggiornate: linee per Linea d'asse, punti per Punto centrale."),
                    kind:XrActionKind.Toggle,isOn:()=> { var entities=FormatSelection(definition.Item2); return entities.Length>0 && entities.All(e=>(bool)e.Data[definition.Item3]); });
            }
            yield return new XrAction("sketch.selection.filters","Filtri selezione",SketchCommandCatalog.TabPrefix+"Schizzo",
                ()=>CanSelect,()=>_palette()?.ShowTabGroup(new[] {FilterTab},true),()=>"Apri uno schizzo aggiornato e completa l'operazione corrente.");
            foreach(var part in new SketchSubpart?[] {null,SketchSubpart.Entity,SketchSubpart.Start,SketchSubpart.End,SketchSubpart.Center,SketchSubpart.Point})
            {
                var filter=part;
                yield return new XrAction("sketch.selection.part."+(filter?.ToString()??"all"),PartLabel(filter),FilterTab,()=>CanSelect,
                    ()=> { if(!CanSelect) return; _selectionPart=filter; CancelCandidates(); _changed(); },
                    ()=>"Selezione non disponibile.",isOn:()=>_selectionPart==filter);
            }
            foreach(var type in new[] { (string)null,"constraint" }.Concat(Session.Snapshot?.Entities.Where(e=>e.Selectable).Select(e=>e.Type)
                .Distinct().OrderBy(t=>t,StringComparer.Ordinal) ?? Enumerable.Empty<string>()))
            {
                string filter=type;
                yield return new XrAction("sketch.selection.filter."+(filter??"all"),filter==null ? "Tutte le geometrie" : TypeLabel(filter),FilterTab,
                    ()=>CanSelect,()=> { if (!CanSelect) return; _selectionType=filter; CancelCandidates(); _changed(); },
                    ()=>"Selezione non disponibile.",isOn:()=>_selectionType==filter);
            }
            int pickGeneration=_pickGeneration;
            var candidateSnapshot=_candidateSnapshot;
            for(int i=0;i<_constraintCandidates.Length;i++)
            {
                int index=i;var reference=_constraintCandidates[i];
                bool valid()=>CanSelect && pickGeneration==_pickGeneration && ReferenceEquals(candidateSnapshot,Session.Snapshot)
                    && reference.Matches(Session.Snapshot.State,Session.SketchId);
                var constraint=candidateSnapshot.Constraints.Single(c=>c.Reference?.EntityId==reference.EntityId);
                yield return new XrAction("sketch.constraint.candidate."+pickGeneration+"."+index,
                    (index+1)+" • "+ConstraintLabel(constraint),SelectionTab,valid,()=>
                    { if(valid()) SelectConstraint(constraint); },()=>"Candidato scaduto: seleziona di nuovo nella revisione attuale.");
            }
            for(int i=0;i<_candidates.Length;i++)
            {
                int index=i; var entity=_candidates[i];
                bool valid()=>CanSelect && pickGeneration==_pickGeneration && ReferenceEquals(candidateSnapshot,Session.Snapshot)
                    && entity.Reference.Matches(Session.Snapshot.State,Session.SketchId);
                yield return new XrAction("sketch.selection.candidate."+pickGeneration+"."+index,
                    (index+1)+" • "+TypeLabel(entity.Entity.Type)+" • "+PartLabel(entity.Reference.Subpart),SelectionTab,valid,()=>
                    {
                        if (!valid()) return;
                        Select(entity,_candidateAdditive); CancelCandidates(); Render(); _changed(); _palette()?.TryLeaveGroup();
                    },()=>"Candidato scaduto: seleziona di nuovo nella revisione attuale.");
            }
            string reason() => !IsOpen ? "Apri prima uno schizzo nativo esistente." : !Session.CanEdit
                ? "Ricarica lo schizzo nella revisione attuale; risolvi prima eventuali dati parziali o esiti incerti."
                : Session.HasDraft ? "Applica o annulla prima l'operazione corrente." : PromptOpen ? "Completa o annulla le coordinate." : "Workspace non disponibile.";
            yield return new XrAction("sketch.editor.refresh", "Ricarica schizzo", SketchCommandCatalog.TabPrefix+"Schizzo",
                () => !_disposed && IsOpen && _ready() && !Session.Loading && Session.Mutation.Status != DesignStatus.RefreshRequired,
                async () => { try { await OpenAsync(Session.SketchId); } catch (Exception ex) { _notice(ex.Message); } }, reason);
            yield return new XrAction("sketch.editor.confirm", "Conferma bozza conservata", SketchCommandCatalog.TabPrefix+"Schizzo",
                () => !_disposed && _ready() && Session.CanConfirmDraft && Session.Mutation.Status == DesignStatus.Empty,
                () => Session.ConfirmDraftOnCurrentRevision(), ()=>"I riferimenti conservati devono appartenere alla revisione attuale; altrimenti seleziona nuovamente le geometrie.");
            yield return new XrAction("sketch.editor.finish", "Concludi editor", SketchCommandCatalog.TabPrefix+"Esci",
                () => !_disposed && IsOpen && !Session.HasDraft && !PromptOpen && Session.Mutation.Status == DesignStatus.Empty,
                Close, () => "Applica o annulla prima l'operazione corrente, quindi aggiorna il documento.");
            foreach (bool inscribed in new[] { true,false })
            {
                bool mode=inscribed;
                yield return new XrAction("sketch.polygon."+(mode ? "inscribed" : "circumscribed"),mode ? "Inscritto" : "Circoscritto",PolygonOptionsTab,
                    ()=>CanCreate && Session.Snapshot.SupportsVariant("polygon"),()=>Prompt(Creations.Single(c=>c.Variant=="polygon"),"Poligono",mode),()=>VariantReason("polygon",reason()));
            }
            foreach (var creation in Creations)
            {
                var definition = creation;
                var command = SketchCommandCatalog.Commands.Single(c => c.NativeId == definition.NativeId);
                yield return new XrAction(command.Id, command.Label, command.FamilyTab, () => CanCreate && Session.Snapshot.SupportsVariant(definition.Variant)
                    && (definition.Variant!="line_midpoint" || MidpointSelection(_midpointRoles)),
                    () => { if (definition.Variant=="polygon" && _palette()!=null) _palette().ShowTabGroup(new[] {PolygonOptionsTab},true);
                        else if(definition.Variant=="line_midpoint") PromptMidpoint();
                        else if(definition.Variant=="spline_fit" || definition.Variant=="spline_control") StartSpline(definition.Variant,command.Label);
                        else Prompt(definition,command.Label); },
                    ()=>definition.Variant=="line_midpoint" && CanCreate && Session.Snapshot.SupportsVariant(definition.Variant)
                        ? "Seleziona i punti nativi nell'ordine punto medio, estremo secondo la modalità scelta." : VariantReason(definition.Variant,reason()));
            }
        }
        private void PromptPattern(string variant,bool expressions=false)
        {
            bool edit=variant=="pattern_update";
            if(edit ? !PatternEditSelection(variant) : !PatternSelection(variant)) return;
            expressions=expressions || !edit && _patternExpressions;
            var definition=edit ? (JObject)_selectedConstraint.Data["pattern_definition"] : null;
            bool circular=edit ? (string)definition["kind"]=="circular" : variant=="pattern_circular";
            bool two=!circular && (edit ? (bool?)definition["two_directions"]==true : _patternTwoDirections);
            string[] fields=circular ? new[] {"count","angle_deg"} : two ? new[] {"x_count","x_spacing_mm","y_count","y_spacing_mm"} : new[] {"x_count","x_spacing_mm"};
            double[] values=fields.Select(f=>edit ? (double)definition[f]["value"] : f=="angle_deg" ? 360d : f.EndsWith("count",StringComparison.Ordinal) ? 3d : 10d).ToArray();
            var options=new JObject {["references"]=edit ? new JArray(_selectedConstraint.Reference.ToJson()) : new JArray(Selected.Select(r=>r.ToJson()))};
            if(!edit)
            {
                options["associative"]=_patternAssociative;options["fitted"]=circular ? _patternCircleFitted : _patternRectFitted;
                if(circular) {options["natural_axis"]=_patternNatural;options["symmetric"]=_patternSymmetric;}
                else {options["two_directions"]=two;options["natural_x"]=_patternNatural;options["symmetric_x"]=_patternSymmetric;
                    if(two) {options["natural_y"]=_patternNaturalY;options["symmetric_y"]=_patternSymmetricY;}}
                if(_patternSuppressed.Length>0) options["suppressed_elements"]=new JArray(_patternSuppressed);
            }
            CancelCandidates();int generation=++_promptGeneration,index=0;var snapshot=Session.Snapshot;
            void Next()
            {
                string field=fields[index];bool count=field.EndsWith("count",StringComparison.Ordinal),angle=field=="angle_deg";
                string label=count ? "Numero "+(field.StartsWith("y",StringComparison.Ordinal) ? "Y" : circular ? "elementi" : "X") : angle ? "Angolo °" : "Distanza "+(field.StartsWith("y",StringComparison.Ordinal) ? "Y mm" : "X mm");
                if(expressions)
                {
                    string initial=edit ? (string)definition[field]["expression"] : null;
                    initial=initial ?? values[index].ToString(System.Globalization.CultureInfo.InvariantCulture)+(count ? "" : angle ? " deg" : " mm");
                    var text=new TextEntry("sketch.pattern.expression."+field,initial,2048);_textPrompt=text;
                    text.Committed+=()=>
                    {
                        if(_disposed || generation!=_promptGeneration || _textPrompt!=text || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                        options[field]=text.Value;index++;if(index<fields.Length){Next();return;}
                        _textPrompt=null;Session.SetOperation(variant,options);_notice("Serie pronta: Anteprima verifica espressioni, unità e parametri; poi Applica.");_changed();
                    };
                    if(_palette()!=null) _palette().ShowTextKeypad(text,"Serie • "+label);else text.BeginEdit();_changed();return;
                }
                var entry=new NumericEntry("sketch.pattern.input."+field,count ? QuantityUnit.None : angle ? QuantityUnit.Degrees : QuantityUnit.Millimeters,values[index],count ? 1 : .001,count ? 2000 : angle ? 360000 : 100000);_prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    if(count && entry.Value!=Math.Truncate(entry.Value)) {_prompt=null;_notice("Il numero di elementi deve essere intero. Riavvia la serie.");_changed();return;}
                    options[field]=entry.Value;index++;if(index<fields.Length) {Next();return;}
                    _prompt=null;Session.SetOperation(variant,options);_notice("Serie pronta: Anteprima, poi Applica.");_changed();
                };
                if(_palette()!=null) _palette().ShowKeypad(entry,"Serie • "+label);else entry.BeginEdit();_changed();
            }
            Next();
        }
        private void PromptPatternSuppression(bool edit=false)
        {
            if(edit ? !PatternEditSelection("pattern_update") : !CanCreate) return;
            CancelCandidates();int generation=++_promptGeneration;var snapshot=Session.Snapshot;
            var constraint=edit ? _selectedConstraint : null;
            var previous=edit ? ((JArray)constraint.Data["pattern_definition"]["suppressed_elements"]).Values<int>().ToArray() : _patternSuppressed;
            var entry=new TextEntry("sketch.pattern.suppressed.input",string.Join(",",previous),12000);_textPrompt=entry;
            entry.Committed+=()=>
            {
                if(_disposed || generation!=_promptGeneration || _textPrompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                _textPrompt=null;var parts=entry.Value.Split(new[] {',',';',' '},StringSplitOptions.RemoveEmptyEntries);var indices=new List<int>();var seen=new HashSet<int>();
                foreach(string part in parts)
                {
                    if(!int.TryParse(part,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int index)
                        || index<1 || index>2000 || !seen.Add(index))
                    {_notice("Inserisci indici nativi distinti da 1 a 2000, separati da virgole. Nessuna modifica alle opzioni.");_changed();return;}
                    indices.Add(index);
                }
                if(indices.Count==0) {_notice("Inserisci almeno un indice oppure scegli Nessuna soppressione.");_changed();return;}
                if(edit)
                {
                    Session.SetOperation("pattern_update",new JObject {["references"]=new JArray(constraint.Reference.ToJson()),["suppressed_elements"]=new JArray(indices)});
                    _notice("Soppressioni pronte: Anteprima verifica gli elementi disponibili, poi Applica.");
                }
                else {_patternSuppressed=indices.ToArray();_notice("Soppressioni conservate per la prossima serie; Anteprima verifica gli elementi disponibili.");}
                _changed();
            };
            if(_palette()!=null) _palette().ShowTextKeypad(entry,"Serie • indici nativi, es. 2, 4");else entry.BeginEdit();_changed();
        }
        private void PromptOffset(bool byPoint)
        {
            string variant=byPoint ? "offset_point" : "offset_distance";
            if(!OffsetSelection(variant)) return;
            CancelCandidates();int generation=++_promptGeneration,index=0;var snapshot=Session.Snapshot;
            var values=byPoint ? new[] {0d,10d} : new[] {10d};
            var options=new JObject {["references"]=new JArray(Selected.Select(r=>r.ToJson())),
                ["include_connected"]=_offsetConnected,["create_constraints"]=_offsetConstraints};
            if(!byPoint) options["natural_direction"]=_offsetNatural;
            void Next()
            {
                var entry=new NumericEntry("sketch.offset.input."+index,QuantityUnit.Millimeters,values[index],byPoint ? -100000 : .001,100000);_prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[index++]=entry.Value;if(index<values.Length) {Next();return;}
                    _prompt=null;if(byPoint) options["point_mm"]=new JArray(values[0],values[1]);else options["distance_mm"]=values[0];
                    Session.SetOperation(variant,options);_notice("Offset pronto: Anteprima, poi Applica.");_changed();
                };
                string field=byPoint ? "Punto "+(index==0 ? "X mm" : "Y mm") : "Distanza mm";
                if(_palette()!=null) _palette().ShowKeypad(entry,"Offset • "+field);else entry.BeginEdit();_changed();
            }
            Next();
        }
        private void PromptTransform(string variant,string label)
        {
            if(!TransformSelection(variant)) return;
            CancelCandidates();int generation=++_promptGeneration,index=0;var snapshot=Session.Snapshot;
            bool rotate=variant=="transform_rotate";
            var values=rotate ? new[] {0d,0d,90d} : new[] {10d,0d};
            var options=new JObject {["references"]=new JArray(Selected.Select(r=>r.ToJson())),["remove_constraints"]=_transformRemoveConstraints};
            if(rotate) options["copy"]=_rotateCopy;
            void Next()
            {
                bool angle=rotate && index==2;
                var entry=new NumericEntry("sketch.transform.input."+index,angle ? QuantityUnit.Degrees : QuantityUnit.Millimeters,
                    values[index],angle ? -360000 : -100000,angle ? 360000 : 100000);_prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[index++]=entry.Value;if(index<values.Length) {Next();return;}
                    _prompt=null;options[rotate ? "center_mm" : "delta_mm"]=new JArray(values[0],values[1]);
                    if(rotate) options["angle_deg"]=values[2];Session.SetOperation(variant,options);
                    _notice("Modifica pronta: Anteprima, poi Applica.");_changed();
                };
                string field=angle ? "Angolo °" : (rotate ? "Centro " : "Spostamento ")+(index==0 ? "X mm" : "Y mm");
                if(_palette()!=null) _palette().ShowKeypad(entry,label+" • "+field);else entry.BeginEdit();_changed();
            }
            Next();
        }
        private void Prompt(Creation creation, string label, bool inscribed = true)
        {
            if (!CanCreate || !Session.Snapshot.SupportsVariant(creation.Variant)) return;
            CancelCandidates();
            int generation = ++_promptGeneration, index = 0;
            bool ellipseAxisPicked=false;
            NativeSketchArcInput arcInput=null;
            var values = (double[])creation.Defaults.Clone();
            var snapshot = Session.Snapshot;
            void Finish()
            {
                _prompt=null;ResetCoordinatePick();
                try { var options=creation.Options(values); if (creation.Variant=="polygon") options["inscribed"]=inscribed;
                    Session.SetOperation(creation.Variant,options); _notice("Operazione pronta: Anteprima, poi Applica."); }
                catch (Exception ex) { _notice(ex.Message); }
                _changed();
            }
            void Next()
            {
                ResetCoordinatePick();
                bool sides = index>=creation.PointCount*2 && creation.OptionsNames[index-creation.PointCount*2]=="sides";
                var unit = sides ? QuantityUnit.None : creation.Labels[index].EndsWith("°",StringComparison.Ordinal) ? QuantityUnit.Degrees : QuantityUnit.Millimeters;
                var entry = new NumericEntry("sketch.coordinate."+index,unit,values[index],sides ? 3 : -100000,sides ? 120 : 100000);
                _prompt = entry;
                entry.Committed += () =>
                {
                    if (_disposed || generation != _promptGeneration || _prompt != entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[index++] = entry.Value;
                    if(ellipseAxisPicked && index==4) {Finish();return;}
                    if (index < values.Length) { Next(); return; }
                    Finish();
                };
                if (_palette() != null) _palette().ShowKeypad(entry,label+" • "+creation.Labels[index]); else entry.BeginEdit();
                if(index<creation.PointCount*2)
                {
                    int pair=index-index%2;
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        if(pair==4 && (creation.Variant=="arc_three_points" || creation.Variant=="slot_three_point_arc")
                            && !NativeSketchSlotInput.TryThreePointArc(new CadPoint(values[0],values[1]),new CadPoint(values[2],values[3]),point,out _,out _))
                        {_notice("Indica un punto di passaggio distinto dagli estremi e non allineato, con raggio entro 100.000 mm.");return;}
                        CloseCoordinateField();values[pair]=point.X;values[pair+1]=point.Y;index=pair+2;
                        if(index<values.Length) Next();else Finish();
                    });
                    string role=(creation.Variant=="arc_three_points" || creation.Variant=="slot_three_point_arc")
                        ? (pair==0 ? "iniziale" : pair==2 ? "finale" : "di passaggio sull'arco") : (pair/2+1).ToString();
                    _notice(label+": indica il punto "+role+" sul foglio con Trigger oppure inserisci X/Y nel tastierino.");
                }
                else if(index==4 && (creation.Variant=="slot_center_to_center" || creation.Variant=="slot_overall" || creation.Variant=="slot_center"))
                {
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        if(!NativeSketchSlotInput.TryWidth(creation.Variant,new CadPoint(values[0],values[1]),new CadPoint(values[2],values[3]),point,out double width))
                        {_notice("Indica un asse non nullo e una larghezza positiva entro 100.000 mm, oppure usa il tastierino.");return;}
                        CloseCoordinateField();values[4]=width;Finish();
                    });
                    _notice(label+": indica la larghezza con un terzo punto sul foglio e Trigger oppure inseriscila nel tastierino.");
                }
                else if(creation.Variant=="circle_center" && index==2)
                {
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        double dx=point.X-values[0],dy=point.Y-values[1],radius=Math.Sqrt(dx*dx+dy*dy);
                        if(radius<=0 || radius>100000) {_notice("Indica un punto distinto dal centro, entro un raggio di 100.000 mm.");return;}
                        CloseCoordinateField();values[index]=radius;Finish();
                    });
                    _notice(label+": indica un punto sulla circonferenza con Trigger oppure inserisci il raggio nel tastierino.");
                }
                else if(creation.Variant=="arc_center" && index==2)
                {
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        try {arcInput=new NativeSketchArcInput(new CadPoint(values[0],values[1]),point);}
                        catch(ArgumentException ex) {_notice(ex.Message);return;}
                        CloseCoordinateField();values[2]=arcInput.RadiusMm;values[3]=arcInput.StartDegrees;index=4;Next();
                    });
                    _notice(label+": indica il punto iniziale con Trigger oppure inserisci raggio e angolo nel tastierino.");
                }
                else if((creation.Variant=="arc_center" || creation.Variant=="slot_center_arc") && index==4)
                {
                    if(arcInput==null && creation.Variant=="slot_center_arc")
                    {
                        try {arcInput=new NativeSketchArcInput(new CadPoint(values[0],values[1]),new CadPoint(values[2],values[3]));}
                        catch(ArgumentException) { /* Numeric alternative remains available for an invalid axis. */ }
                    }
                    if(arcInput==null && creation.Variant=="arc_center" && values[2]>1e-9 && values[2]<=100000)
                    {
                        double angle=values[3]*Math.PI/180;
                        arcInput=new NativeSketchArcInput(new CadPoint(values[0],values[1]),new CadPoint(values[0]+values[2]*Math.Cos(angle),values[1]+values[2]*Math.Sin(angle)));
                    }
                    if(arcInput!=null)
                        ArmCoordinatePick(snapshot,generation,point=>
                        {
                            if(!arcInput.TryEnd(point,out double sweep)) {_notice("Indica un punto finale distinto dal centro e un arco non nullo, inferiore a 360°.");return;}
                            CloseCoordinateField();values[4]=sweep;
                            if(creation.Variant=="slot_center_arc") {index=5;Next();} else Finish();
                        },point=>
                        {
                            if(arcInput.Observe(point)) _geometry()?.ArcCursor(snapshot.Frame,new CadPoint(values[0],values[1]),arcInput.RadiusMm,arcInput.StartDegrees,arcInput.SweepDegrees);
                            else _geometry()?.Cursor(null,default,null,SketchShape.Line);
                        },()=>arcInput.Suspend());
                    _notice(label+": segui il verso dell'arco con la penna e indica il punto finale con Trigger; oppure inserisci l'ampiezza nel tastierino. Guida locale, poi Anteprima CAD.");
                }
                else if((creation.Variant=="slot_center_arc" && index==5) || (creation.Variant=="slot_three_point_arc" && index==6))
                {
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        var first=new CadPoint(values[0],values[1]);var second=new CadPoint(values[2],values[3]);
                        CadPoint center=first,start=second;double sweep=values[4];
                        if(creation.Variant=="slot_three_point_arc")
                        {
                            if(!NativeSketchSlotInput.TryThreePointArc(first,second,new CadPoint(values[4],values[5]),out center,out sweep))
                            {_notice("I tre punti devono definire un arco non nullo; correggili o annulla il comando.");return;}
                            start=first;
                        }
                        if(!NativeSketchSlotInput.TryArcWidth(center,start,sweep,point,out double width))
                        {_notice("Indica una larghezza positiva entro 100.000 mm su un arco non nullo, oppure usa il tastierino.");return;}
                        CloseCoordinateField();values[index]=width;Finish();
                    });
                    _notice(label+": indica la larghezza con un quarto punto sul foglio e Trigger oppure inseriscila nel tastierino.");
                }
                else if(creation.Variant=="ellipse" && index==2)
                {
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        double dx=point.X-values[0],dy=point.Y-values[1],radius=Math.Sqrt(dx*dx+dy*dy);
                        if(radius<=0 || radius>100000) {_notice("Indica un primo asse non nullo, entro 100.000 mm.");return;}
                        CloseCoordinateField();values[2]=radius;values[4]=Math.Atan2(dy,dx)*180/Math.PI;
                        ellipseAxisPicked=true;index=3;Next();
                    });
                    _notice(label+": indica l'estremo del primo semiasse con Trigger oppure inserisci il semiasse nel tastierino.");
                }
                else if(creation.Variant=="ellipse" && index==3 && ellipseAxisPicked)
                {
                    ArmCoordinatePick(snapshot,generation,point=>
                    {
                        double angle=values[4]*Math.PI/180,dx=point.X-values[0],dy=point.Y-values[1];
                        double along=dx*Math.Cos(angle)+dy*Math.Sin(angle),across=-dx*Math.Sin(angle)+dy*Math.Cos(angle);
                        double remaining=1-along*along/(values[2]*values[2]);
                        if(remaining<=0 || Math.Abs(across)<1e-9) {_notice("Indica un punto sull'ellisse fuori dal primo asse e tra i suoi estremi.");return;}
                        double second=Math.Abs(across)/Math.Sqrt(remaining);
                        if(second>100000) {_notice("Il secondo semiasse supera 100.000 mm; scegli un punto più vicino al primo asse.");return;}
                        CloseCoordinateField();values[3]=second;Finish();
                    });
                    _notice(label+": indica un punto sull'ellisse con Trigger oppure inserisci il secondo semiasse nel tastierino.");
                }
                else _notice(label+": imposta "+creation.Labels[index]+" nel tastierino.");
                _changed();
            }
            Next();
        }
        private void PromptNewDimensionText(bool name)
        {
            if(!CanCreate) return;
            CancelCandidates();int generation=++_promptGeneration;var snapshot=Session.Snapshot;
            var entry=new TextEntry("sketch.dimension.new."+(name ? "name" : "expression"),name ? _newDimensionName : _newDimensionExpression,name ? 128 : 2048);_textPrompt=entry;
            entry.Committed+=()=>
            {
                if(_disposed || generation!=_promptGeneration || _textPrompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                _textPrompt=null;if(name) _newDimensionName=entry.Value;else {_newDimensionExpression=entry.Value;_dimensionUseExpression=true;}
                _notice(name ? "Nome conservato per la prossima quota." : "Espressione conservata: scegli la variante della nuova quota.");_changed();
            };
            if(_palette()!=null) _palette().ShowTextKeypad(entry,name ? "Nome della prossima quota" : "Espressione con unità");else entry.BeginEdit();_changed();
        }
        private void PromptDimensionText(bool name)
        {
            if(!DimensionEditSelection("dimension_update") || (!name && (bool?)_selectedDimension.Data["driven"]!=false)) return;
            CancelCandidates();int generation=++_promptGeneration;var snapshot=Session.Snapshot;var dimension=_selectedDimension;
            string property=name ? "parameter_name" : "expression";
            var entry=new TextEntry("sketch.dimension.edit."+property,(string)dimension.Data[name ? "name" : "expression"],name ? 128 : 2048);
            _textPrompt=entry;
            entry.Committed+=()=>
            {
                if(_disposed || generation!=_promptGeneration || _textPrompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                _textPrompt=null;Session.SetOperation("dimension_update",new JObject {[property]=entry.Value,["references"]=new JArray(dimension.Reference.ToJson())});
                _notice("Testo pronto: Anteprima verifica espressione, unità e parametri; poi Applica.");_changed();
            };
            if(_palette()!=null) _palette().ShowTextKeypad(entry,name ? "Nome parametro" : "Espressione con unità");else entry.BeginEdit();
            _changed();
        }
        private void PromptExistingDimension(bool position)
        {
            if(!DimensionEditSelection("dimension_update")) return;
            CancelCandidates();int generation=++_promptGeneration,index=0;
            var snapshot=Session.Snapshot;var dimension=_selectedDimension;var data=dimension.Data;
            bool angle=data["value_deg"]!=null;
            var values=position ? ((JArray)data["text_mm"]).Values<double>().ToArray() : new[] {(double)data[angle ? "value_deg" : "value_mm"]};
            void Next()
            {
                var entry=new NumericEntry("sketch.dimension.edit.input."+index,!position && angle ? QuantityUnit.Degrees : QuantityUnit.Millimeters,
                    values[index],position ? -100000 : .000001,100000);_prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[index++]=entry.Value;if(index<values.Length) {Next();return;}
                    _prompt=null;
                    var options=new JObject {["references"]=new JArray(dimension.Reference.ToJson())};
                    options[position ? "text_mm" : angle ? "value_deg" : "value_mm"]=position ? (JToken)new JArray(values) : new JValue(values[0]);
                    Session.SetOperation("dimension_update",options);_notice("Modifica quota pronta: Anteprima, poi Applica.");_changed();
                };
                string label=(string)data["name"]+" • "+(position ? (index==0 ? "Testo X mm" : "Testo Y mm") : angle ? "Valore °" : "Valore mm");
                if(_palette()!=null) _palette().ShowKeypad(entry,label);else entry.BeginEdit();_changed();
            }
            Next();
        }
        private void PromptDimension(string kind,string label)
        {
            if(!DimensionSelection(kind)) return;
            CancelCandidates(); int generation=++_promptGeneration,index=0;
            var snapshot=Session.Snapshot;
            var options=new JObject {["references"]=new JArray(Selected.Select(r=>r.ToJson())),["driven"]=_dimensionDriven,
                ["linear_diameter"]=_dimensionLinearDiameter,["positive_side"]=_dimensionPositiveSide};
            bool driven=_dimensionDriven,angular=NativeSketchDimensionRules.Angular(kind),tangent=kind=="tangent_distance";
            bool numeric=!driven && !_dimensionUseExpression;
            if(!driven && _dimensionUseExpression) options["expression"]=_newDimensionExpression;
            if(_newDimensionName.Length>0) options["parameter_name"]=_newDimensionName;
            var values=new double[2+(numeric ? 1 : 0)+(tangent ? 4 : 0)]; values[0]=values[1]=50;
            if(numeric) values[2]=angular ? 60 : 12;
            void Next()
            {
                bool valueStep=numeric && index==2;
                var entry=new NumericEntry("sketch.dimension.input."+index,valueStep && angular ? QuantityUnit.Degrees : QuantityUnit.Millimeters,
                    values[index],valueStep ? 0.000001 : -100000,100000);
                _prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[index++]=entry.Value;
                    if(index<values.Length) {Next();return;}
                    _prompt=null; options["text_mm"]=new JArray(values[0],values[1]);
                    if(numeric) options[angular ? "value_deg" : "value_mm"]=values[2];
                    if(tangent) {int first=numeric ? 3 : 2;options["proximity_one_mm"]=new JArray(values[first],values[first+1]);options["proximity_two_mm"]=new JArray(values[first+2],values[first+3]);}
                    Session.SetOperation("dimension_"+kind,options);_newDimensionName=""; _notice("Quota pronta: Anteprima, poi Applica."); _changed();
                };
                int proximityIndex=index-(numeric ? 3 : 2);
                string field=index<2 ? "Testo "+(index==0 ? "X mm" : "Y mm") : valueStep ? (angular ? "Valore °" : "Valore mm")
                    : "Punto vicino "+(proximityIndex/2+1)+" "+(proximityIndex%2==0 ? "X mm" : "Y mm");
                if(_palette()!=null) _palette().ShowKeypad(entry,label+" • "+field); else entry.BeginEdit();
                _changed();
            }
            Next();
        }
        private void PromptConstraintProximity()
        {
            if(!CanCreate) return;
            CancelCandidates(); int generation=++_promptGeneration,index=0;
            var snapshot=Session.Snapshot;
            var values=new[] {_constraintProximity?.X??0,_constraintProximity?.Y??0};
            void Next()
            {
                var entry=new NumericEntry("sketch.constraint.proximity."+index,QuantityUnit.Millimeters,values[index],-100000,100000);
                _prompt=entry;
                entry.Committed+=()=>
                {
                    if(_disposed || generation!=_promptGeneration || _prompt!=entry || !Session.CanEdit || !ReferenceEquals(snapshot,Session.Snapshot)) return;
                    values[index++]=entry.Value;
                    if(index<2) {Next();return;}
                    _prompt=null; _constraintProximity=new CadPoint(values[0],values[1]);
                    _notice("Punto vicino conservato per Tangente e Uniforme G2; seleziona il vincolo."); _changed();
                };
                if(_palette()!=null) _palette().ShowKeypad(entry,"Estremo vicino • "+(index==0 ? "X mm" : "Y mm")); else entry.BeginEdit();
                _changed();
            }
            Next();
        }
        private string VariantReason(string variant,string fallback)=>Session.Snapshot!=null && !Session.Snapshot.SupportsVariant(variant)
            ? "Il backend collegato non supporta questo comando; aggiorna le capacità dello schizzo." : fallback;
        private sealed class Creation
        {
            public string NativeId, Variant;
            public int PointCount;
            public string[] Labels, OptionsNames;
            public double[] Defaults;
            public JObject Options(double[] values)
            {
                var points = new JArray(); for (int i=0;i<PointCount;i++) points.Add(new JArray(values[2*i],values[2*i+1]));
                var options = new JObject { ["points_mm"] = points };
                for (int i=0;i<OptionsNames.Length;i++)
                {
                    double value=values[PointCount*2+i];
                    if (OptionsNames[i]=="sides")
                    { if (value!=Math.Truncate(value) || value<3 || value>120) throw new ArgumentException("Il numero di lati deve essere intero, fra 3 e 120."); options[OptionsNames[i]]=(int)value; }
                    else options[OptionsNames[i]]=value;
                }
                if (Variant == "point") options["hole_center"] = true;
                return options;
            }
        }
        private static Creation Create(string id,string variant,int points,double[] defaults,params string[] tail)
        {
            var names = new Dictionary<string,string> { ["radius_mm"]="Raggio mm",["width_mm"]="Larghezza mm",["start_deg"]="Angolo iniziale °",
                ["sweep_deg"]="Ampiezza °",["major_radius_mm"]="Primo semiasse mm",["minor_radius_mm"]="Secondo semiasse mm",["rotation_deg"]="Rotazione °",["sides"]="Numero lati" };
            return new Creation { NativeId=id,Variant=variant,PointCount=points,Defaults=defaults,OptionsNames=tail,
                Labels=Enumerable.Range(1,points).SelectMany(i=>new[] {"X"+i+" mm","Y"+i+" mm"}).Concat(tail.Select(n=>names[n])).ToArray() };
        }
        private static readonly Creation[] Creations =
        {
            Create("SketchLineCmd","line",2,new double[] {0,0,20,0}),
            Create("SketchMidpointLineWrapperCmd","line_midpoint",2,new double[] {0,0,20,0}),
            Create("SketchTwoPointRectangleCmd","rectangle_two_points",2,new double[] {0,0,20,10}),
            Create("SketchThreePointRectangleCmd","rectangle_three_points",3,new double[] {0,0,20,0,20,10}),
            Create("SketchTwoPointCenRectangleCmd","rectangle_center_two_points",2,new double[] {0,0,20,10}),
            Create("SketchThreePointCenRectangleCmd","rectangle_center_three_points",3,new double[] {0,0,20,0,20,10}),
            Create("SketchPolygonCmd","polygon",2,new double[] {0,0,20,0,6},"sides"),
            Create("SketchCenterPointCircleCmd","circle_center",1,new double[] {0,0,10},"radius_mm"),
            Create("SketchHoleCenterPointCmd","point",1,new double[] {0,0}),
            Create("SketchThreePointArcCmd","arc_three_points",3,new double[] {0,0,20,0,10,10}),
            Create("SketchCenterPointArcCmd","arc_center",1,new double[] {0,0,10,0,90},"radius_mm","start_deg","sweep_deg"),
            Create("SketchEllipseCmd","ellipse",1,new double[] {0,0,20,10,0},"major_radius_mm","minor_radius_mm","rotation_deg"),
            Create("SketchSplineCmd","spline_fit",3,new double[] {0,0,10,10,20,0}),
            Create("SketchCPSplineCmd","spline_control",3,new double[] {0,0,10,10,20,0}),
            Create("SketchCentertoCenterSlotCmd","slot_center_to_center",2,new double[] {0,0,40,0,10},"width_mm"),
            Create("SketchOverallLengthSlotCmd","slot_overall",2,new double[] {0,0,40,0,10},"width_mm"),
            Create("SketchCenterPointSlotCmd","slot_center",2,new double[] {0,0,20,0,10},"width_mm"),
            Create("SketchThreePointArcSlotCmd","slot_three_point_arc",3,new double[] {0,0,20,20,40,0,5},"width_mm"),
            Create("SketchCenterPointArcSlotCmd","slot_center_arc",2,new double[] {0,0,20,0,90,5},"sweep_deg","width_mm")
        };
        public void Dispose() { if (_disposed) return; ResetParameterResources(); ResetImportResources(); ResetWindow(false); CancelPrompt(); _disposed=true; Session.Changed-=OnChanged; Session.Dispose(); }
    }
}
