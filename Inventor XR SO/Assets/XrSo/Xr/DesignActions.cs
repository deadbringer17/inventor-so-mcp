using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Declaration of the Progettazione actions for the palette, the ring, the commit bar and voice: same ids, same
    /// enablement and same Invoke for every path. Long choosers (planes, profiles, parameters, constraints) are dynamic
    /// picker tabs of at most eight entries each, reached with the palette stick; nothing is paginated or dropped.
    /// </summary>
    public sealed partial class DesignWorkspace
    {
        public const string TabSketch = "schizzo", TabConstraints = "vincoli", TabFeature = "feature",
            TabOptions = "opzioni", TabParameters = "parametri", TabView = "vista";
        public const string PickTabPrefix = "_pick.";

        private static readonly XrTab[] StaticTabs =
        {
            new XrTab(TabSketch, "Schizzo"), new XrTab(TabConstraints, "Vincoli"), new XrTab(TabFeature, "Feature"),
            new XrTab(TabOptions, "Opzioni feature"), new XrTab(TabParameters, "Parametri"),
        };

        private sealed class PickerItem
        {
            public PickerItem(string label, Action choose) { Label = label; Choose = choose; }
            public string Label { get; }
            public Action Choose { get; }
        }
        private sealed class Picker
        {
            public string Title;
            public List<PickerItem> Items;
            public string ReturnTab;
            public int Pages => (Items.Count + ActionCatalog.MaxPalette - 1) / ActionCatalog.MaxPalette;
        }

        private XrAction[] _actions;
        private Picker _picker;

        public CommitBarState CommitBar { get; } = new CommitBarState();

        public IReadOnlyList<XrTab> Tabs
        {
            get
            {
                if (_picker == null && _featureEdit == null) return StaticTabs;
                var tabs = new List<XrTab>(StaticTabs);
                if (_featureEdit != null) tabs.Add(new XrTab(FeatureTabId, "Feature: " + _featureEdit.Name));
                if (_picker == null) return tabs;
                for (int page = 0; page < _picker.Pages; page++)
                    tabs.Add(new XrTab(PickTabPrefix + page, _picker.Pages == 1 ? _picker.Title : _picker.Title + " " + (page + 1) + "/" + _picker.Pages));
                return tabs;
            }
        }

        public IEnumerable<XrAction> Actions => _actions ?? (_actions = BuildActions());

        /// <summary>Ring: planar face = Schizzo, Estrusione, Foro; edge = Raccordo, Smusso (same actions as the palette).</summary>
        public IEnumerable<XrAction> ContextActions(SelectionKind selection)
        {
            string[] ids = selection == SelectionKind.PlanarFace ? new[] { "design.sketch.create", "design.extrude", "design.hole" }
                : selection == SelectionKind.Edge ? new[] { "design.fillet", "design.chamfer" } : Array.Empty<string>();
            var all = Actions.ToList();
            return ids.Select(id => all.First(a => a.Id == id)).ToArray();
        }

        // ---------------------------------------------------------------- pickers

        private void OpenPicker(string title, IEnumerable<PickerItem> items)
        {
            var list = items.ToList();
            if (list.Count == 0) { SetNotice("Nessun elemento disponibile."); return; }
            string back = _picker?.ReturnTab ?? _shell?.Palette.CurrentTab;
            _picker = new Picker { Title = title, Items = list, ReturnTab = back };
            Refresh();
            _shell?.Palette.ShowTabGroup(Enumerable.Range(0, _picker.Pages).Select(p => PickTabPrefix + p).ToList());
        }

        private void ClosePicker(bool refresh = true)
        {
            if (_picker == null) return;
            string back = _picker.ReturnTab;
            _picker = null;
            _shell?.Palette.ClearTabGroup();
            if (refresh) Refresh();
            if (back != null) _shell?.Palette.ShowTab(back);
        }

        private bool PickerOpen(Picker picker) => Active && _picker == picker;

        // ---------------------------------------------------------------- actions

        private bool EditableNow => Active && _kind == "part" && _online && !_busy && _context != null && _pendingMutations == 0
            && _session != null && _session.CanEdit && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.Previewing;
        private bool SketchOn => EditableNow && _screen == "sketch" && _sketch != null;
        private bool FeatureOn => EditableNow && _screen == "feature";
        private bool CommitReady => Active && _session != null && _pendingMutations == 0
            && _session.Status != DesignStatus.RefreshRequired && _session.Status != DesignStatus.Committing;

        private string EditReason()
        {
            if (!Active) return "Progettazione non è aperta.";
            if (_kind != "part") return "Progettazione richiede una parte attiva in Inventor.";
            if (!_online) return "Offline: anteprima e modifiche CAD disabilitate.";
            if (_busy) return "Lettura del contesto CAD in corso.";
            if (_pendingMutations > 0) return "Attendo la conclusione della richiesta CAD precedente.";
            if (_session?.Status == DesignStatus.RefreshRequired)
                return _session.CommitOutcomeUnknown ? "Esito della modifica non confermato: controlla il CAD in Inventor." : "Aggiorna prima il documento.";
            if (_context == null) return "Riferimenti non disponibili: usa Aggiorna riferimenti.";
            if (_session?.Status == DesignStatus.Previewing || _session?.Status == DesignStatus.Committing) return "Operazione CAD in corso.";
            return "Non disponibile ora.";
        }
        private string NeedSketch() => EditableNow ? "Crea o scegli prima uno schizzo." : EditReason();
        private string NeedFeature() => EditableNow ? "Scegli prima una feature: Estrusione, Foro, Raccordo o Smusso." : EditReason();
        private string NeedHole() => EditableNow && _screen == "feature" ? "Solo per il Foro." : NeedFeature();
        private string NeedExtrude() => EditableNow && _screen == "feature" ? "Solo per l’Estrusione." : NeedFeature();

        private XrAction[] BuildActions()
        {
            string undoReason() => EditableNow ? (_session.Status != DesignStatus.Empty ? "Chiudi o annulla prima il comando in corso." : "Nessuna modifica XR da annullare.") : EditReason();
            string redoReason() => EditableNow ? (_session.Status != DesignStatus.Empty ? "Chiudi o annulla prima il comando in corso." : "Nessuna modifica XR da ripetere.") : EditReason();
            var list = new List<XrAction>
            {
                // Schizzo
                new XrAction("design.sketch.create", "Crea schizzo", TabSketch, () => EditableNow, CreateSketch, EditReason, new[] { "nuovo schizzo" }, icon: "sketch"),
                Shape("design.shape.line", "Linea", SketchShape.Line),
                Shape("design.shape.rectangle", "Rettangolo", SketchShape.Rectangle),
                Shape("design.shape.circle", "Cerchio", SketchShape.Circle),
                new XrAction("design.sketch.numeric", "Coordinate numeriche", TabSketch, () => SketchOn, NumericShape, NeedSketch),
                new XrAction("design.sketch.dimension", "Quota geometria", TabSketch, () => SketchOn, () =>
                    { _dimensionStep = 1; _dimensionIndex = -1; _first = null; SetNotice("Quota: seleziona una linea, un rettangolo o un cerchio con la penna."); Refresh(); }, NeedSketch, icon: "dimension"),
                new XrAction("design.sketch.removelast", "Rimuovi ultimo", TabSketch, () => SketchOn, () =>
                    { _dimensionStep = 0; _sketch.RemoveLast(); UpdateDraft(); Refresh(); }, NeedSketch),

                // Vincoli
                new XrAction("design.constraint.add", "Aggiungi vincolo", TabConstraints, () => SketchOn, () => { _dimensionStep = 0; _first = null; OpenConstraintKinds(); }, NeedSketch),
                new XrAction("design.constraint.show", "Vincoli della geometria", TabConstraints, () => SketchOn, () =>
                    {
                        if (CurrentSketchSnapshot == null) SetNotice("Calcola prima l’anteprima per leggere i vincoli reali.");
                        else { _dimensionStep = 3; _first = null; SetNotice("Seleziona la geometria per vedere i vincoli di Inventor."); }
                        Refresh();
                    }, NeedSketch),
                new XrAction("design.constraint.all", "Mostra tutti i vincoli", TabConstraints, () => SketchOn, () =>
                    {
                        var snapshot = CurrentSketchSnapshot;
                        if (snapshot == null) { SetNotice("Calcola prima l’anteprima per leggere i vincoli reali."); Refresh(); }
                        else ShowConstraints(snapshot.Constraints);
                    }, NeedSketch),
                new XrAction("design.constraint.removelast", "Rimuovi ultimo vincolo", TabConstraints, () => SketchOn && _sketch.ConstraintCount > 0, () =>
                    { _sketch.RemoveLastConstraint(); UpdateDraft(); Refresh(); }, () => SketchOn ? "Nessun vincolo nella bozza." : NeedSketch()),

                // Feature
                new XrAction("design.extrude", "Estrusione", TabFeature, () => EditableNow, PickExtrusionSketch, EditReason,
                    new[] { "estrudi", "estrudi schizzo", "crea estrusione", "fai estrusione" }, icon: "extrude"),
                new XrAction("design.hole", "Foro", TabFeature, () => EditableNow, () => Feature("hole"), EditReason, new[] { "fora", "crea foro" }, icon: "hole"),
                new XrAction("design.fillet", "Raccordo", TabFeature, () => EditableNow, () => EdgeFeature("fillet"), EditReason, new[] { "raccorda" }, icon: "fillet"),
                new XrAction("design.chamfer", "Smusso", TabFeature, () => EditableNow, () => EdgeFeature("chamfer"), EditReason, new[] { "smussa" }, icon: "chamfer"),
                new XrAction("design.history.undo", "Annulla modifica XR", TabFeature, () => EditableNow && _session.Status == DesignStatus.Empty && _history?.CanUndo == true,
                    () => ApplyHistory(false), undoReason, new[] { "annulla ultima modifica" }, icon: "undo"),
                new XrAction("design.history.redo", "Ripeti modifica XR", TabFeature, () => EditableNow && _session.Status == DesignStatus.Empty && _history?.CanRedo == true,
                    () => ApplyHistory(true), redoReason, new[] { "ripeti ultima modifica" }, icon: "redo"),

                // Opzioni feature
                new XrAction("design.dimension", "Dimensione numerica", TabOptions, () => FeatureOn, OpenDimensionKeypad, NeedFeature, kind: XrActionKind.Numeric),
                new XrAction("design.diameter", "Diametro", TabOptions, () => FeatureOn && _feature == "hole", () =>
                    Ask("Diametro foro", new[] { "mm" }, new[] { _diameter }, n => { _diameter = n[0]; UpdateDraft(); }), NeedHole, kind: XrActionKind.Numeric),
                new XrAction("design.through", "Foro passante", TabOptions, () => FeatureOn && _feature == "hole", () =>
                    { _through = !_through; UpdateDraft(); Refresh(); }, NeedHole, kind: XrActionKind.Toggle, isOn: () => _through),
                new XrAction("design.position", "Posizione esatta XYZ", TabOptions, () => FeatureOn && _feature == "hole", () =>
                    Ask("Centro foro nel modello", new[] { "X mm", "Y mm", "Z mm" }, new[] { _facePoint.X, _facePoint.Y, _facePoint.Z },
                        n => { _facePoint = new CadPoint(n[0], n[1], n[2]); UpdateDraft(); }), NeedHole, kind: XrActionKind.Numeric),
                new XrAction("design.operation", "Operazione: " + OperationLabel(_operation), TabOptions, () => FeatureOn && _feature == "extrude", () =>
                    { var options = new[] { "join", "cut", "intersect", "new_body" }; _operation = options[(Array.IndexOf(options, _operation) + 1) % 4]; UpdateDraft(); Refresh(); }, NeedExtrude),
                new XrAction("design.direction", "Direzione: " + (_symmetric ? "Simmetrica" : _negative ? "Negativa" : "Positiva"), TabOptions, () => FeatureOn && _feature == "extrude", () =>
                    { if (_symmetric) { _symmetric = false; _negative = false; } else if (_negative) _symmetric = true; else _negative = true; UpdateDraft(); Refresh(); }, NeedExtrude),

                // Parametri
                new XrAction("design.parameters", "Parametri", TabParameters, () => EditableNow, OpenParameters, EditReason, icon: "parameters"),
                new XrAction("design.refresh", "Aggiorna riferimenti", TabParameters,
                    () => Active && _online && _kind == "part" && !_busy && CommitReady && _session.Status != DesignStatus.Previewing, LoadContext,
                    () => !_online ? "Offline." : _busy ? "Lettura del contesto CAD in corso." : EditReason(), icon: "refresh"),

                // Vista
                new XrAction("design.view.model", "Vista modello", TabView, () => _sheet != null && _sheet.State == InventorXrSo.Unity.Scene.SketchSheetState.Sheet, ShowModelView,
                    () => "Il modello è già in vista modello.", isOn: () => _sheet == null || _sheet.State != InventorXrSo.Unity.Scene.SketchSheetState.Sheet, kind: XrActionKind.Toggle),
                new XrAction("design.view.sheet", "Foglio", TabView,
                    () => Active && _sheet != null && _bench?.Frame != null && _sketch?.Frame != null && _sheet.State != InventorXrSo.Unity.Scene.SketchSheetState.Sheet, ShowSheetView,
                    () => _sketch?.Frame == null ? "Serve prima uno schizzo con il suo piano." : "Il foglio è già visibile.",
                    isOn: () => _sheet != null && _sheet.State == InventorXrSo.Unity.Scene.SketchSheetState.Sheet, kind: XrActionKind.Toggle),

                // Barra di conferma: l'unico percorso verso il CAD
                new XrAction(CommitIds.Preview, "Anteprima", ActionCatalog.CommitTab,
                    () => CommitReady && InDraftScreen && _session.CanEdit && _session.Status != DesignStatus.Previewing,
                    () => { if (_screen == "parameter") PreviewParameter(); else Preview(); },
                    () => !InDraftScreen ? "Nessun comando in corso." : EditReason()),
                new XrAction(CommitIds.Apply, "Applica", ActionCatalog.CommitTab,
                    () => CommitReady && InDraftScreen && _session.CanApply, Apply,
                    () => !InDraftScreen ? "Nessun comando in corso." : "Serve un’anteprima verificata e visualizzata.", voiceInvokes: false),
                new XrAction(CommitIds.Cancel, "Annulla comando", ActionCatalog.CommitTab,
                    () => CommitReady && InDraftScreen, CancelDraft, () => "Nessun comando da annullare."),
                new XrAction(CommitIds.Recover, _session?.CommitOutcomeUnknown == true ? "Ho controllato il CAD" : "Aggiorna documento", ActionCatalog.CommitTab,
                    () => Active && _online && _session?.Status == DesignStatus.RefreshRequired && _pendingMutations == 0, ReviewUnknown,
                    () => _pendingMutations > 0 ? "Attendo la conclusione della richiesta CAD precedente." : "Il documento è aggiornato.",
                    new[] { "aggiorna documento", "ho controllato il cad" }),
            };

            if (_featureEdit != null) list.AddRange(BuildFeatureEditActions());
            if (_picker != null)
            {
                var picker = _picker;
                for (int i = 0; i < picker.Items.Count; i++)
                {
                    var item = picker.Items[i];
                    list.Add(new XrAction("design.pick." + i, item.Label, PickTabPrefix + i / ActionCatalog.MaxPalette,
                        () => PickerOpen(picker), () => { ClosePicker(); item.Choose(); }, () => "L’elenco è stato chiuso."));
                }
            }
            return list.ToArray();
        }

        private XrAction Shape(string id, string label, SketchShape shape) => new XrAction(id, label, TabSketch, () => SketchOn, () =>
            { _shape = shape; _first = null; _dimensionStep = 0; _geometry.ShowDraft(_sketch); Refresh(); }, NeedSketch,
            isOn: () => _screen == "sketch" && _sketch != null && _shape == shape, kind: XrActionKind.Toggle,
            icon: shape == SketchShape.Line ? "line" : shape == SketchShape.Rectangle ? "rectangle" : "circle");

        private void EdgeFeature(string feature)
        {
            string edge = _ringEdge;
            Feature(feature);
            if (edge != null) { _edges.Add(edge); UpdateDraft(); Refresh(); }
            HideRing();
        }

        private static string OperationLabel(string operation) => operation switch
        {
            "join" => "Unisci", "cut" => "Taglia", "intersect" => "Interseca", "new_body" => "Nuovo corpo", _ => operation
        };

        private void OpenParameters()
        {
            var items = new List<PickerItem>();
            foreach (var p in _context.Parameters.OfType<Newtonsoft.Json.Linq.JObject>()
                .Where(p => p["value_mm"] != null || p["value_deg"] != null || (string)p["unit"] == "ul"))
            {
                string name = (string)p["name"], unit = p["value_mm"] != null ? "mm" : p["value_deg"] != null ? "deg" : "ul";
                double value = (double?)(p["value_mm"] ?? p["value_deg"] ?? p["value_unitless"]) ?? 0;
                var quantity = unit == "deg" ? QuantityUnit.Degrees : unit == "mm" ? QuantityUnit.Millimeters : QuantityUnit.None;
                items.Add(new PickerItem(name + " = " + Format(value) + " " + unit, () => Ask("Parametro " + name, new[] { unit }, new[] { value }, values =>
                {
                    _feature = null; _sketch = null; _parameterName = name; _screen = "parameter";
                    _session.SetDraft(new Newtonsoft.Json.Linq.JArray(DesignOperations.Parameter(name, values[0], unit)));
                    PreviewParameter();
                }, quantity)));
            }
            OpenPicker("Parametri CAD", items);
        }

        // Constraint flow: kind -> entities (one list per pick, excluding the ones already taken) -> confirmation.
        private void OpenConstraintKinds()
        {
            var items = new List<PickerItem>();
            foreach (var item in new[] { ("Tangente", "tangent"), ("Uguale", "equal"), ("Simmetria", "symmetric"),
                ("Parallelo", "parallel"), ("Perpendicolare", "perpendicular"), ("Concentrico", "concentric") })
            {
                var kind = item.Item2;
                items.Add(new PickerItem(item.Item1, () => { _constraintKind = kind; _constraintPicks.Clear(); OpenConstraintEntities(); }));
            }
            OpenPicker("Vincolo da confermare", items);
        }

        private void OpenConstraintEntities()
        {
            int needed = _constraintKind == "symmetric" ? 3 : 2;
            var entities = _sketch.ConstraintEntityKeys.Where(i => !_constraintPicks.Contains(i)).ToArray();
            if (entities.Length == 0)
            {
                SetNotice("Nessuna geometria disponibile. Disegna linee o cerchi separati.");
                Refresh();
                return;
            }
            SetNotice(_constraintPicks.Count == 2 && needed == 3 ? "Seleziona la linea asse di simmetria." : "Seleziona geometria " + (_constraintPicks.Count + 1) + " di " + needed + ".");
            var items = new List<PickerItem>();
            foreach (int index in entities)
            {
                int selected = index;
                items.Add(new PickerItem(_sketch.ConstraintEntityLabel(index), () =>
                {
                    _constraintPicks.Add(selected);
                    _geometry.ShowDraft(_sketch, selected / 4, _sketch.Elements[selected / 4].Shape == SketchShape.Rectangle ? selected % 4 : -1);
                    if (_constraintPicks.Count == needed) OpenConstraintConfirm(); else OpenConstraintEntities();
                }));
            }
            items.Add(new PickerItem("Annulla selezione", () => { _constraintPicks.Clear(); Refresh(); }));
            OpenPicker("Geometrie del vincolo", items);
        }

        private void OpenConstraintConfirm()
        {
            string kindLabel = _constraintKind switch { "tangent" => "Tangente", "equal" => "Uguale", "symmetric" => "Simmetria", "parallel" => "Parallelo", "perpendicular" => "Perpendicolare", _ => "Concentrico" };
            SetNotice(kindLabel + " • " + string.Join(", ", _constraintPicks.Select(_sketch.ConstraintEntityLabel)) + ".\nInserisce il vincolo nella bozza; poi Anteprima e Applica.");
            OpenPicker("Conferma vincolo", new[]
            {
                new PickerItem("Conferma nella bozza", () =>
                {
                    try
                    {
                        _sketch.AddEntityConstraint(_constraintKind, _constraintPicks[0], _constraintPicks[1], _constraintPicks.Count == 3 ? _constraintPicks[2] : -1);
                        SetNotice("Vincolo aggiunto alla bozza. Calcola l’anteprima."); UpdateDraft(); Refresh();
                    }
                    catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
                }),
                new PickerItem("Scegli di nuovo", () => { _constraintPicks.Clear(); OpenConstraintEntities(); }),
                new PickerItem("Annulla vincolo", () => { _constraintPicks.Clear(); Refresh(); }),
            });
        }

        // ---------------------------------------------------------------- voice / runner command surface

        /// <summary>Same enablement as the declared actions "Crea schizzo", "Raccordo", "Smusso", "Annulla/Ripeti modifica XR", "Annulla comando" and "Applica".</summary>
        public bool IsEnabled(string commandId)
        {
            var id = CommandActionId(commandId);
            return id != null && Actions.First(a => a.Id == id).Enabled;
        }

        /// <summary>Runs the command exactly as its action would. Apply only shows a notice: the commit bar is the single commit path (M5-11).</summary>
        public bool Invoke(string commandId)
        {
            var id = CommandActionId(commandId);
            if (id == null || !IsEnabled(commandId)) return false;
            if (commandId == CommandIds.Apply)
            {
                SetNotice("Anteprima verificata. Conferma premendo Applica sulla barra di conferma.");
                Refresh();
                return true;
            }
            return Actions.First(a => a.Id == id).TryInvoke();
        }

        private static string CommandActionId(string commandId)
        {
            switch (commandId)
            {
                case CommandIds.CreateSketch: return "design.sketch.create";
                case CommandIds.Fillet: return "design.fillet";
                case CommandIds.Chamfer: return "design.chamfer";
                case CommandIds.Undo: return "design.history.undo";
                case CommandIds.Redo: return "design.history.redo";
                case CommandIds.CancelDraft: return CommitIds.Cancel;
                case CommandIds.Apply: return CommitIds.Apply;
                default: return null;   // sheet-metal, inspect commands belong to other workspaces
            }
        }

        /// <summary>Labels on offer for "premi X": every declared action (disabled ones answer with their reason).</summary>
        public IEnumerable<(string label, bool enabled)> VoiceActions => Active
            ? Actions.Select(a => (a.Label, a.Enabled)).ToArray() : Enumerable.Empty<(string, bool)>();

        public bool InvokeVoiceAction(string label)
        {
            var action = Active ? Actions.FirstOrDefault(a => a.Label == label && a.Enabled && a.VoiceInvokes) : null;
            return action != null && action.TryInvoke();
        }
    }
}
