using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Declaration of the Lamiera actions for the palette, the ring, the commit bar and voice: same ids, same enablement and
    /// same Invoke for every path. The rule list and the sketch list are dynamic picker tabs of at most eight entries each,
    /// reached with the palette stick; nothing is paginated or dropped.
    /// </summary>
    public sealed partial class LamieraWorkspace
    {
        public const string TabLamiera = "lamiera", TabSketch = "schizzo", TabFlat = "sviluppo", TabView = "vista";
        public const string PickTabPrefix = "_pick.";

        public const string IdRule = "lamiera.rule", IdFlange = "lamiera.flange", IdFlangeHeight = "lamiera.flange.height",
            IdFlangeAngle = "lamiera.flange.angle", IdFlangeDatum = "lamiera.flange.datum", IdFlangeClear = "lamiera.flange.clear",
            IdUndo = "lamiera.history.undo", IdRedo = "lamiera.history.redo",
            IdFace = "lamiera.face", IdCut = "lamiera.cut", IdSketchChange = "lamiera.sketch.change", IdCutExtent = "lamiera.cut.extent",
            IdCutDirection = "lamiera.cut.direction", IdCutAcrossBends = "lamiera.cut.acrossbends", IdDesign = "lamiera.design",
            IdFlatCreate = "lamiera.flat.create", IdFlatShow = "lamiera.flat.show", IdFlatHide = "lamiera.flat.hide",
            IdFlatDetach = "lamiera.flat.detach", IdFlatAttach = "lamiera.flat.attach",
            IdFit = "lamiera.view.fit", IdRecenter = "lamiera.view.recenter", IdRefresh = "lamiera.refresh",
            IdViewFolded = "lamiera.view.folded", IdViewFlat = "lamiera.view.flat",
            IdPickPrefix = "lamiera.pick.";

        private static readonly XrTab[] StaticTabs =
        {
            new XrTab(TabLamiera, "Lamiera"), new XrTab(TabSketch, "Schizzo"), new XrTab(TabFlat, "Sviluppo"), new XrTab(TabView, "Vista"),
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
                if (_picker == null) return StaticTabs;
                var tabs = new List<XrTab>(StaticTabs);
                for (int page = 0; page < _picker.Pages; page++)
                    tabs.Add(new XrTab(PickTabPrefix + page, _picker.Pages == 1 ? _picker.Title : _picker.Title + " " + (page + 1) + "/" + _picker.Pages));
                return tabs;
            }
        }

        public IEnumerable<XrAction> Actions => _actions ?? (_actions = BuildActions());

        /// <summary>Ring: planar face = Flangia, Faccia, Taglio; edge = Flangia (same actions as the palette).</summary>
        public IEnumerable<XrAction> ContextActions(UiSelectionKind selection)
        {
            string[] ids = selection == UiSelectionKind.PlanarFace ? new[] { IdFlange, IdFace, IdCut }
                : selection == UiSelectionKind.Edge ? new[] { IdFlange } : Array.Empty<string>();
            var all = Actions.ToList();
            return ids.Select(id => all.First(a => a.Id == id)).ToArray();
        }

        // ---------------------------------------------------------------- pickers

        private void OpenPicker(string title, IEnumerable<PickerItem> items)
        {
            var list = items.ToList();
            if (list.Count == 0) { SetNotice("Nessun elemento disponibile."); Refresh(); return; }
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

        private bool PickerOpen(Picker picker) => Active && _picker == picker && !Locked;

        private void OpenRulePicker()
        {
            var context = _mode.Context;
            if (context == null) return;
            var items = new List<PickerItem>();
            foreach (var rule in context.AvailableRules)
            {
                var name = rule;
                items.Add(new PickerItem((name == context.Rule ? "● " : "") + name, () => { _ruleName = name; Preview(); }));
            }
            items.Add(new PickerItem("Spessore: " + Fmt(_thicknessMm ?? context.ThicknessMm ?? 0) + " mm", () => AskNumber(FieldThickness)));
            OpenPicker("Regola", items);
        }

        private void OpenSketchPicker()
        {
            var sketches = _designContext?.Sketches.ToArray() ?? Array.Empty<DesignPlane>();
            if (sketches.Length == 0) { SetNotice("Nessuno schizzo nel documento: crealo da Modello 3D / Schizzo."); Refresh(); return; }
            OpenPicker(_mode.Armed == SheetMetalCommand.Cut ? "Taglio da schizzo" : "Faccia da schizzo", sketches.Select(sketch =>
            {
                var name = sketch.Name;
                return new PickerItem(name + " • " + ProfileHint(name), () => ChooseSketch(name));
            }));
        }

        // ---------------------------------------------------------------- availability

        /// <summary>The workspace is open, the CAD context is readable and no CAD request is running.</summary>
        private bool Interactive => Active && _kind == "part" && _online && _session != null && !_busy && _mode.CanWrite && _designContext != null
            && _session.Status != DesignStatus.RefreshRequired;
        private bool Locked => _session != null && (_session.Status == DesignStatus.Committing || _session.Status == DesignStatus.Previewing || _pendingMutations > 0);
        /// <summary>Draft options (flange datum, cut extent...): readable context, editable draft, nothing in flight.</summary>
        private bool OptionsOn => Interactive && !Locked && _session.CanEdit;

        private string CommandReason()
        {
            if (!Active) return "Lamiera non è aperta.";
            if (_kind != "part") return "Lamiera richiede una parte in lamiera attiva in Inventor.";
            if (_session == null) return "PC non collegato.";
            if (!_online) return "Offline: Lamiera è in sola lettura. Nessuna modifica CAD.";
            if (_busy) return "Lettura del contesto lamiera in corso.";
            if (_pendingMutations > 0) return "Attendo la conclusione della richiesta CAD precedente.";
            if (_session.Status == DesignStatus.RefreshRequired)
                return _session.CommitOutcomeUnknown ? "Esito della modifica non confermato: controlla il CAD in Inventor." : "Aggiorna prima il documento.";
            if (!_mode.CanWrite) return _mode.Reason ?? "Lamiera non disponibile.";
            if (!SceneCurrent) return "La scena non è ancora aggiornata all’ultima revisione.";
            if (_designContext == null) return "Riferimenti CAD non disponibili: usa Aggiorna.";
            if (_session.Status == DesignStatus.Previewing || _session.Status == DesignStatus.Committing) return "Operazione CAD in corso.";
            return "Non disponibile ora.";
        }

        private string NeedFlange() => OptionsOn && _screen != "flange" ? "Scegli prima Flangia." : CommandReason();
        private string NeedCut() => OptionsOn && _screen != "cut" ? "Scegli prima Taglio da schizzo e il suo schizzo." : CommandReason();
        private string SketchReason() => IsEnabled(CommandIds.SheetMetalFace) || _designContext == null || _designContext.Sketches.Count > 0
            ? CommandReason() : "Nessuno schizzo nel documento: crealo da Modello 3D / Schizzo.";
        private string FlatReason()
        {
            if (!Active || _session == null || !_online || !_mode.CanWrite || _busy) return CommandReason();
            _mode.CheckFlatPattern(out var message);
            return string.IsNullOrEmpty(message) ? CommandReason() : message;
        }

        private bool FlatVisible => _flat != null && _flat.IsVisible;
        /// <summary>The "Sviluppo" view is on: the pattern is shown on the work plane (the folded part gives way).</summary>
        private bool FlatViewShown => FlatVisible && _flatDisplay != null && _flatDisplay.FlatViewOn;
        private bool FlatPatternExists => _mode.Context?.FlatPattern.Exists == true || FlatVisible;
        public bool FlatViewOn => FlatViewShown;

        private XrAction[] BuildActions()
        {
            string historyReason(string none) => Idle && _designContext != null ? (_session.Status != DesignStatus.Empty ? "Chiudi o annulla prima il comando in corso." : none) : CommandReason();
            var list = new List<XrAction>
            {
                // Lamiera
                new XrAction(IdRule, "Regola / Spessore", TabLamiera, () => IsEnabled(CommandIds.SheetMetalRule), OpenRule, CommandReason),
                new XrAction(IdFlange, "Flangia", TabLamiera, () => IsEnabled(CommandIds.Flange), StartFlange, CommandReason),
                new XrAction(IdFlangeHeight, "Altezza: " + Fmt(_flange.HeightMm) + " mm", TabLamiera,
                    () => _mode.Armed == SheetMetalCommand.Flange && Idle, () => AskNumber(FieldFlangeHeight), NeedFlange, kind: XrActionKind.Numeric),
                new XrAction(IdFlangeAngle, "Angolo: " + Fmt(_flange.AngleDegrees) + " °", TabLamiera,
                    () => _mode.Armed == SheetMetalCommand.Flange && Idle, () => AskNumber(FieldFlangeAngle), NeedFlange, kind: XrActionKind.Numeric),
                new XrAction(IdFlangeDatum, "Riferimento: " + DatumLabel(_flange.Datum), TabLamiera,
                    () => _screen == "flange" && OptionsOn, CycleDatum, NeedFlange),
                new XrAction(IdFlangeClear, "Svuota bordi", TabLamiera,
                    () => _screen == "flange" && OptionsOn && _flange.EdgeIds.Count > 0, () => { _flange.Clear(); Refresh(); },
                    () => _screen == "flange" && OptionsOn ? "Nessun bordo selezionato." : NeedFlange()),
                new XrAction(IdUndo, "Annulla modifica XR", TabLamiera, () => IsEnabled(CommandIds.Undo), () => ApplyHistory(false),
                    () => historyReason("Nessuna modifica XR da annullare."), new[] { "annulla ultima modifica" }),
                new XrAction(IdRedo, "Ripeti modifica XR", TabLamiera, () => IsEnabled(CommandIds.Redo), () => ApplyHistory(true),
                    () => historyReason("Nessuna modifica XR da ripetere."), new[] { "ripeti ultima modifica" }),

                // Schizzo
                new XrAction(IdFace, "Faccia da schizzo", TabSketch, () => IsEnabled(CommandIds.SheetMetalFace), () => OpenSketchPick(SheetMetalCommand.Face),
                    SketchReason, new[] { "crea faccia da schizzo" }),
                new XrAction(IdCut, "Taglio da schizzo", TabSketch, () => IsEnabled(CommandIds.SheetMetalCut), () => OpenSketchPick(SheetMetalCommand.Cut),
                    SketchReason, new[] { "taglia da schizzo" }),
                new XrAction(IdSketchChange, "Cambia schizzo", TabSketch,
                    () => OptionsOn && (_mode.Armed == SheetMetalCommand.Face || _mode.Armed == SheetMetalCommand.Cut), () => { SetScreen("sketches"); OpenSketchPicker(); },
                    () => OptionsOn ? "Scegli prima Faccia o Taglio da schizzo." : CommandReason()),
                new XrAction(IdCutExtent, "Estensione: " + (_extent == "thickness" ? "spessore" : "passante"), TabSketch,
                    () => _screen == "cut" && OptionsOn, () =>
                    { _extent = _extent == "thickness" ? "through_all" : "thickness"; if (_extent == "through_all") _acrossBends = false; Preview(); }, NeedCut),
                new XrAction(IdCutDirection, "Direzione: " + DirectionLabel(_direction), TabSketch,
                    () => _screen == "cut" && OptionsOn, () =>
                    { var options = new[] { "positive", "negative", "symmetric" }; _direction = options[(Array.IndexOf(options, _direction) + 1) % 3]; Preview(); }, NeedCut),
                new XrAction(IdCutAcrossBends, "Attraverso pieghe: " + (_acrossBends ? "sì" : "no"), TabSketch,
                    () => _screen == "cut" && OptionsOn && _extent == "thickness", () => { _acrossBends = !_acrossBends; Preview(); },
                    () => _screen == "cut" && OptionsOn ? "Solo con estensione spessore." : NeedCut(), kind: XrActionKind.Toggle, isOn: () => _acrossBends),
                new XrAction(IdDesign, "Modello 3D / Schizzo", TabSketch, () => Active && !Locked, () => DesignRequested?.Invoke(),
                    () => Active ? "Operazione CAD in corso." : "Lamiera non è aperta."),

                // Sviluppo
                new XrAction(IdFlatCreate, "Crea sviluppo", TabFlat, () => IsEnabled(CommandIds.FlatPatternCreate), StartFlatPattern, FlatReason, new[] { "sviluppa" }),
                new XrAction(IdFlatShow, "Mostra sviluppo", TabFlat,
                    () => Interactive && !FlatVisible && _mode.Context?.FlatPattern.Exists == true && _flat != null && _flat.State != FlatPatternState.Loading,
                    ShowFlat, () => FlatVisible ? "Lo sviluppo è già visibile." : _mode.Context?.FlatPattern.Exists == true ? CommandReason() : "Lo sviluppo piano non esiste: usa Crea sviluppo.",
                    new[] { "visualizza sviluppo" }),
                new XrAction(IdFlatHide, "Nascondi sviluppo", TabFlat, () => Active && FlatVisible, () => _flat.Hide(), () => "Lo sviluppo non è visibile."),
                new XrAction(IdFlatDetach, "Stacca sviluppo", TabFlat, () => Active && FlatVisible && !_flat.Detached, () => _flat.Detach(),
                    () => FlatVisible ? "Lo sviluppo è già staccato." : "Mostra prima lo sviluppo."),
                new XrAction(IdFlatAttach, "Riaggancia sviluppo", TabFlat, () => Active && FlatVisible && _flat.Detached, () => _flat.Attach(),
                    () => FlatVisible ? "Lo sviluppo è già agganciato." : "Mostra prima lo sviluppo."),

                // Vista
                new XrAction(IdFit, "Adatta", TabView, () => Active && _bench != null, FitView, () => "Postazione non disponibile."),
                new XrAction(IdRecenter, "Ricentra", TabView, () => Active && _bench != null, RecenterView, () => "Postazione non disponibile."),
                new XrAction(IdViewFolded, "Vista: piegato", TabView, () => Active && FlatViewShown, () => { FoldedView(); Refresh(); },
                    () => FlatPatternExists ? "La vista Piegato è già attiva." : "Lo sviluppo piano non esiste: usa Crea sviluppo.", new[] { "vista piegato" }),
                new XrAction(IdViewFlat, "Vista: sviluppo", TabView,
                    () => Active && FlatPatternExists && !FlatViewShown && (FlatVisible || (Interactive && _flat != null && _flat.State != FlatPatternState.Loading)),
                    ShowFlat, () => !FlatPatternExists ? "Lo sviluppo piano non esiste: usa Crea sviluppo." : FlatViewShown ? "La vista Sviluppo è già attiva." : CommandReason(),
                    new[] { "vista sviluppo" }),
                new XrAction(IdRefresh, "Aggiorna", TabView,
                    () => Active && _online && _kind == "part" && !_busy && !Locked && _session != null && _session.Status != DesignStatus.RefreshRequired, LoadContext,
                    () => !_online ? "Offline." : _busy ? "Lettura del contesto lamiera in corso." : CommandReason()),

                // Barra di conferma: l'unico percorso verso il CAD
                new XrAction(CommitIds.Preview, "Anteprima", ActionCatalog.CommitTab,
                    () => Interactive && !Locked && InDraftScreen && _session.CanEdit && _mode.Armed != SheetMetalCommand.None, Preview,
                    () => !InDraftScreen ? "Nessun comando in corso." : CommandReason()),
                new XrAction(CommitIds.Apply, "Applica", ActionCatalog.CommitTab,
                    () => InDraftScreen && IsEnabled(CommandIds.Apply), ApplyPressed,
                    () => !InDraftScreen ? "Nessun comando in corso." : "Serve un’anteprima verificata e visualizzata.", voiceInvokes: false),
                new XrAction(CommitIds.Cancel, "Annulla comando", ActionCatalog.CommitTab,
                    () => IsEnabled(CommandIds.CancelDraft), CancelCommand, () => "Nessun comando da annullare."),
                new XrAction(CommitIds.Recover, _session?.CommitOutcomeUnknown == true ? "Ho controllato il CAD" : "Aggiorna documento", ActionCatalog.CommitTab,
                    () => Active && _online && _session?.Status == DesignStatus.RefreshRequired && _pendingMutations == 0, ReviewUnknown,
                    () => _pendingMutations > 0 ? "Attendo la conclusione della richiesta CAD precedente." : "Il documento è aggiornato.",
                    new[] { "aggiorna documento", "ho controllato il cad" }),
            };

            if (_picker != null)
            {
                var picker = _picker;
                for (int i = 0; i < picker.Items.Count; i++)
                {
                    var item = picker.Items[i];
                    list.Add(new XrAction(IdPickPrefix + i, item.Label, PickTabPrefix + i / ActionCatalog.MaxPalette,
                        () => PickerOpen(picker), () => { ClosePicker(); item.Choose(); }, () => "L’elenco è stato chiuso."));
                }
            }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- voice / runner command surface

        private static string CommandActionId(string commandId)
        {
            switch (commandId)
            {
                case CommandIds.Flange: return IdFlange;
                case CommandIds.SheetMetalRule: return IdRule;
                case CommandIds.SheetMetalFace: return IdFace;
                case CommandIds.SheetMetalCut: return IdCut;
                case CommandIds.FlatPatternCreate: return IdFlatCreate;
                case CommandIds.CancelDraft: return CommitIds.Cancel;
                case CommandIds.Apply: return CommitIds.Apply;
                case CommandIds.Undo: return IdUndo;
                case CommandIds.Redo: return IdRedo;
                default: return null;   // design, inspect commands belong to other workspaces
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
