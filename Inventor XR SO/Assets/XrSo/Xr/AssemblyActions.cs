using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Declaration of the Assieme actions for the palette, the ring, the commit bar and voice: same ids, same enablement and
    /// same Invoke for every path. Long lists (components, references, constraint and joint types, axes) are dynamic picker tabs
    /// of at most eight entries each, reached with the palette stick; nothing is paginated or dropped.
    /// </summary>
    public sealed partial class AssemblyWorkspace
    {
        public const string TabComponents = "componenti", TabConstraints = "vincoli", TabOptions = "opzioni", TabView = "vista";
        public const string PickTabPrefix = "_pick.";

        public const string IdComponents = "assembly.components", IdIsolate = "assembly.isolate", IdRelease = "assembly.release",
            IdMove = "assembly.move", IdOpen = "assembly.open", IdOpenDesign = "assembly.open.design", IdOpenLamiera = "assembly.open.lamiera",
            IdActivate = "assembly.activate",
            IdConstrain = "assembly.constrain", IdJoint = "assembly.joint", IdReferences = "assembly.references",
            IdRayMode = "assembly.references.mode", IdRelationValue = "assembly.relation.value", IdFlip = "assembly.relation.flip",
            IdAlign = "assembly.relation.align", IdClearance = "assembly.clearance",
            IdRefresh = "assembly.refresh",
            IdUndo = "assembly.history.undo", IdRedo = "assembly.history.redo",
            IdMoveMode = "assembly.move.mode", IdMoveAxis = "assembly.move.axis", IdMoveValue = "assembly.move.value",
            IdPickPrefix = "assembly.pick.", IdConstraintPrefix = "assembly.constraint.", IdJointPrefix = "assembly.joint.",
            IdAxisPrefix = "assembly.move.axis.";

        private static readonly XrTab[] StaticTabs =
        {
            new XrTab(TabComponents, "Componenti"), new XrTab(TabConstraints, "Vincoli"), new XrTab(TabOptions, "Opzioni"),
        };

        private sealed class PickerItem
        {
            public PickerItem(string label, Action choose, string id = null) { Label = label; Choose = choose; Id = id; }
            public string Label { get; }
            public Action Choose { get; }
            public string Id { get; }
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

        /// <summary>Ring on a component: Isola, Sposta, Vincola, Apri (same actions as the palette).</summary>
        public IEnumerable<XrAction> ContextActions(SelectionKind selection)
        {
            if (selection != SelectionKind.Component) return Array.Empty<XrAction>();
            var all = Actions.ToList();
            return new[] { IdIsolate, IdMove, IdConstrain, IdOpen }.Select(id => all.First(a => a.Id == id)).ToArray();
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

        private void OpenComponentsPicker()
        {
            if (_context == null) return;
            OpenPicker("Componenti", _context.Occurrences.Select(occurrence =>
            {
                var item = occurrence;
                return new PickerItem((item.Id == _occurrence?.Id ? "● " : "") + item.Name, async () =>
                {
                    try { await SelectOccurrenceAsync(item.Id); }
                    catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
                });
            }));
        }

        private async void OpenReferencesPicker()
        {
            try
            {
                if (_occurrence == null) return;
                string id = _occurrence.Id;
                await SelectOccurrenceAsync(id);
                if (_occurrence?.Id != id || _context == null) return;
                OpenPicker("Facce / spigoli", _context.References.Where(r => r.Available).Select(reference =>
                {
                    var item = reference;
                    return new PickerItem(item.Name + " • " + item.Geometry.Replace("k", ""), () => ChooseReference(item));
                }));
            }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        /// <summary>Compatible constraints of the chosen A/B references, or the six constraint types while none is chosen.</summary>
        private void OpenConstraintPicker()
        {
            var types = _a != null && _b != null ? AssemblyOperations.CompatibleConstraints(_a, _b)
                : new[] { "mate", "flush", "mate_axis", "insert", "angle", "tangent" };
            if (_a == null) SetNotice("Scegli prima una faccia o uno spigolo: tocca il componente o usa Facce / spigoli.");
            OpenPicker("Vincolo", types.Select(type =>
            {
                var item = type;
                return new PickerItem(RelationLabel(item), () => ChooseConstraint(item), IdConstraintPrefix + item);
            }));
        }

        private void OpenJointPicker()
        {
            if (_a == null) SetNotice("Scegli prima le origini A e B: tocca il componente o usa Facce / spigoli.");
            OpenPicker("Giunto", AssemblyOperations.JointTypes.Select(type =>
            {
                var item = type;
                return new PickerItem(RelationLabel(item), () => ChooseJoint(item), IdJointPrefix + item);
            }));
        }

        private void OpenAxisPicker()
        {
            OpenPicker("Asse", Enumerable.Range(0, Axes.Count).Select(i =>
            {
                int index = i;
                return new PickerItem("Asse " + (index + 1), () =>
                {
                    _axisIndex = index; _angle = 0; _translation = default; InvalidateDraft(); Draw(); SyncEntries(); Refresh();
                }, IdAxisPrefix + (index + 1));
            }));
        }

        // ---------------------------------------------------------------- availability

        private string CommandReason()
        {
            if (!Active) return "Assieme non è aperto.";
            if (_kind != "assembly") return "Attiva un assieme dal Browser.";
            if (_session == null) return "PC non collegato.";
            if (!_online) return "Offline — Applica non disponibile.";
            if (_busy) return "Lettura Inventor in corso.";
            if (_mutations > 0) return "Attendo la conclusione della richiesta CAD precedente.";
            if (_session.Status == DesignStatus.RefreshRequired)
                return _session.CommitOutcomeUnknown ? "Esito della modifica non confermato: controlla il CAD in Inventor." : "Aggiorna prima il documento.";
            if (!SceneCurrent) return "La scena non è ancora aggiornata all’ultima revisione.";
            if (_context == null) return "Contesto Assieme non disponibile: usa Aggiorna.";
            if (_session.Status == DesignStatus.Previewing || _session.Status == DesignStatus.Committing) return "Operazione CAD in corso.";
            return "Non disponibile ora.";
        }

        /// <summary>The selected occurrence cannot be edited as a unit (flexible, adaptive, suppressed, virtual): no draft starts on it. Entering it is unaffected.</summary>
        private bool SelectionBlocksEdit => _command == null && _occurrence != null && !_occurrence.Editable;

        private string EditBlockedReason(string command)
        {
            if (_occurrence == null) return CommandReason();
            string what = _occurrence.Kind == "assembly" ? "Sottoassieme " : "Componente ";
            string tail = _occurrence.Kind == "assembly" ? " Doppio Trigger o Apri per entrare e modificarne i componenti." : "";
            return what + ReasonLabel(_occurrence.UnavailableReason ?? "non modificabile") + ": " + command + " non è disponibile." + tail;
        }

        private string NeedOccurrence() => Idle && _occurrence == null ? "Seleziona prima un componente: tocca il pezzo o usa Componenti." : CommandReason();
        private string NeedMove() => !Idle ? CommandReason() : !IsMove ? "Scegli prima Sposta." : "Il componente non ha assi liberi in questo modo.";
        private string NeedRelation() => !Idle ? CommandReason() : (_command == null || IsMove) ? "Scegli prima Vincola o Giunto." : "Scegli prima il tipo di vincolo.";
        private string NeedInspection() => !(_backend is IInspectionBackend) ? "Apertura documenti non disponibile." : NeedOccurrence();
        private bool IsPart => _occurrence != null && _occurrence.Kind != "assembly";
        private bool CanOpenDefinition => Idle && !RequiresCadReview && _occurrence != null && _backend is IInspectionBackend
            && !string.IsNullOrEmpty(_occurrence.DefinitionId);

        private XrAction[] BuildActions()
        {
            string historyReason(string none) => Idle && _context != null ? (_session.Status != DesignStatus.Empty || InDraft ? "Chiudi o annulla prima il comando in corso." : none) : CommandReason();
            var field = _type == "angle" ? "Angolo: " + Fmt(_value) + " °" : "Distanza: " + Fmt(_value) + " mm";
            var list = new List<XrAction>
            {
                // Componenti
                new XrAction(IdComponents, "Componenti", TabComponents, () => Idle && !RequiresCadReview, OpenComponentsPicker, CommandReason),
                new XrAction(IdIsolate, "Isola", TabComponents,
                    () => Active && _occurrence != null && _occurrence.Kind != "assembly" && _isolation != null && !(_isolation.Active && _isolation.OccurrenceId == _occurrence.Id), () => Isolate(),
                    () => _occurrence?.Kind == "assembly" ? "Isola vale per un singolo pezzo: per un sottoassieme usa Apri."
                        : _isolation?.Active == true && _occurrence != null ? "Il componente è già isolato." : NeedOccurrence(), new[] { "isola componente" }, icon: "isolate"),
                new XrAction(IdRelease, "Rilascia", TabComponents, () => Active && _isolation != null && _isolation.Active,
                    () => { ReleaseIsolation(); Refresh(); }, () => "Nessun componente isolato.", new[] { "rilascia componente" }, icon: "release-isolation"),
                new XrAction(IdMove, "Sposta", TabComponents, () => Idle && !RequiresCadReview && _occurrence?.CanMove == true, BeginMove,
                    () => Idle && _occurrence != null ? (!string.IsNullOrEmpty(_occurrence.UnavailableReason) ? EditBlockedReason("Sposta")
                        : _occurrence.Grounded ? "Il componente è fissato." : "Libertà non disponibili per questo componente.") : NeedOccurrence(),
                    new[] { "sposta componente" }, icon: "move"),
                new XrAction(IdOpen, "Apri", TabComponents, () => Active && _occurrence != null && (_occurrence.Kind != "assembly" || CanOpenDefinition),
                    OpenSelected, () => _occurrence?.Kind == "assembly" ? NeedInspection() : NeedOccurrence(), icon: "open-component"),
                new XrAction(IdOpenDesign, "Apri in Progettazione", TabComponents,
                    () => CanOpenDefinition && IsPart && _isolation?.Active == true, () => OpenIsolated(false),
                    () => _isolation?.Active != true ? "Isola prima il componente." : !IsPart ? "Solo le parti si aprono in Progettazione." : NeedInspection()),
                new XrAction(IdOpenLamiera, "Apri in Lamiera", TabComponents,
                    () => CanOpenDefinition && IsPart && _isolation?.Active == true, () => OpenIsolated(true),
                    () => _isolation?.Active != true ? "Isola prima il componente." : !IsPart ? "Solo le parti in lamiera si aprono in Lamiera." : NeedInspection()),
                new XrAction(IdActivate, "Attiva questo assieme", TabComponents,
                    () => CanOpenDefinition && _occurrence.Kind == "assembly", ActivateSubassembly,
                    () => _occurrence != null && _occurrence.Kind != "assembly" ? "Il componente non è un sottoassieme." : NeedInspection()),

                // Vincoli
                new XrAction(IdConstrain, "Vincola", TabConstraints, () => Idle && !RequiresCadReview && !SelectionBlocksEdit, OpenConstraintPicker,
                    () => SelectionBlocksEdit ? EditBlockedReason("Vincola") : CommandReason(), new[] { "vincolo" }, icon: "constrain"),
                new XrAction(IdJoint, "Giunto", TabConstraints, () => Idle && !RequiresCadReview && !SelectionBlocksEdit, OpenJointPicker,
                    () => SelectionBlocksEdit ? EditBlockedReason("Giunto") : CommandReason(), icon: "joint"),
                new XrAction(IdReferences, "Facce / spigoli", TabConstraints, () => Idle && !RequiresCadReview && _occurrence != null, OpenReferencesPicker, NeedOccurrence),
                new XrAction(IdRayMode, "Raggio su spigoli", TabConstraints, () => Idle && !RequiresCadReview, () => { _pickEdges = !_pickEdges; Refresh(); },
                    CommandReason, kind: XrActionKind.Toggle, isOn: () => _pickEdges),
                new XrAction(IdRelationValue, field, TabConstraints, () => Idle && _command != null && !IsMove && _type != null,
                    () => AskNumber(ValueField), NeedRelation, kind: XrActionKind.Numeric),
                new XrAction(IdFlip, "Inverti direzione", TabConstraints, () => Idle && _command != null && !IsMove && _type != null,
                    () => { _opposed = !_opposed; _flipOrigin = !_flipOrigin; InvalidateDraft(); if (_b != null) Preview(); else Refresh(); }, NeedRelation),
                new XrAction(IdAlign, _command == "assembly_joint" ? "Inverti allineamento" : "Tangente interna/esterna", TabConstraints,
                    () => Idle && _command != null && !IsMove && _type != null,
                    () => { _flipAlignment = !_flipAlignment; _inside = !_inside; InvalidateDraft(); if (_b != null) Preview(); else Refresh(); }, NeedRelation),
                new XrAction(IdClearance, "Gioco minimo: " + Fmt(_clearance) + " mm", TabConstraints, () => Idle && !RequiresCadReview,
                    () => AskNumber(FieldClearance), CommandReason, kind: XrActionKind.Numeric),

                // Vista (Adatta, scala, ambiente e legenda sono nella scheda comune ViewActions; qui solo cio che e dell'assieme)
                new XrAction(IdRefresh, "Aggiorna", TabView,
                    () => Active && _online && _kind == "assembly" && !_busy && !Locked && _session != null && _session.Status != DesignStatus.RefreshRequired,
                    () => { CancelReads(); Load(); }, () => !_online ? "Offline." : _busy ? "Lettura Inventor in corso." : CommandReason(), icon: "refresh"),
                new XrAction(IdUndo, "Annulla modifica XR", TabView, () => IsEnabled(CommandIds.Undo), () => History(false),
                    () => historyReason("Nessuna modifica XR da annullare."), new[] { "annulla ultima modifica" }, icon: "undo"),
                new XrAction(IdRedo, "Ripeti modifica XR", TabView, () => IsEnabled(CommandIds.Redo), () => History(true),
                    () => historyReason("Nessuna modifica XR da ripetere."), new[] { "ripeti ultima modifica" }, icon: "redo"),
                new XrAction(IdMoveMode, _rotating ? "Rotazione → Traslazione" : "Traslazione → Rotazione", TabOptions,
                    () => Idle && IsMove && (_rotating ? _occurrence.TranslationAxes.Count > 0 : _occurrence.RotationAxes.Count > 0),
                    () => { _rotating = !_rotating; _axisIndex = 0; _angle = 0; _translation = default; InvalidateDraft(); Draw(); SyncEntries(); Refresh(); }, NeedMove),
                new XrAction(IdMoveAxis, "Asse", TabOptions, () => Idle && IsMove && Axes.Count > 0, OpenAxisPicker, NeedMove),
                new XrAction(IdMoveValue, _rotating ? "Angolo preciso" : "Spostamento preciso", TabOptions, () => Idle && IsMove && Axes.Count > _axisIndex,
                    () => AskNumber(MoveField), NeedMove, kind: XrActionKind.Numeric),

                // Barra di conferma: l'unico percorso verso il CAD
                new XrAction(CommitIds.Preview, "Anteprima", ActionCatalog.CommitTab,
                    () => Idle && !RequiresCadReview && InDraft && PreviewWanted, Preview,
                    () => !InDraft ? "Nessun comando in corso." : IsMove ? NeedMove() : "Scegli i riferimenti A e B e il tipo di relazione."),
                new XrAction(CommitIds.Apply, "Applica", ActionCatalog.CommitTab, () => InDraft && IsEnabled(CommandIds.Apply), ApplyPressed,
                    () => !InDraft ? "Nessun comando in corso." : "Serve un’anteprima verificata e visualizzata.", voiceInvokes: false),
                new XrAction(CommitIds.Cancel, "Annulla comando", ActionCatalog.CommitTab, () => IsEnabled(CommandIds.CancelDraft), CancelCommand,
                    () => "Nessun comando da annullare."),
                new XrAction(CommitIds.Recover, _session?.CommitOutcomeUnknown == true ? "Ho controllato il CAD" : "Aggiorna documento", ActionCatalog.CommitTab,
                    () => Active && _online && _session?.Status == DesignStatus.RefreshRequired && _mutations == 0, Review,
                    () => _mutations > 0 ? "Attendo la conclusione della richiesta CAD precedente." : "Il documento è aggiornato.",
                    new[] { "aggiorna documento", "ho controllato il cad" }),
            };

            if (_picker != null)
            {
                var picker = _picker;
                for (int i = 0; i < picker.Items.Count; i++)
                {
                    var item = picker.Items[i];
                    list.Add(new XrAction(item.Id ?? IdPickPrefix + i, item.Label, PickTabPrefix + i / ActionCatalog.MaxPalette,
                        () => PickerOpen(picker), () => { ClosePicker(); item.Choose(); }, () => "L’elenco è stato chiuso."));
                }
            }
            return list.ToArray();
        }

        /// <summary>Apri from the ring: a sub-assembly is activated in Inventor, a part is isolated to choose where to open it.</summary>
        private void OpenSelected()
        {
            if (_occurrence == null) return;
            HideRing();
            // Apri on a sub-assembly enters it exactly like the double Trigger (a new Assieme level); flexible or not.
            if (_occurrence.Kind == "assembly") { ActivateSubassemblyCore(true); return; }
            if (!Isolate()) return;
            SetNotice("Isolato: scegli Apri in Progettazione o Apri in Lamiera nella scheda Componenti.");
            _shell?.Palette.ShowTab(TabComponents);
        }

        // ---------------------------------------------------------------- voice / runner command surface

        /// <summary>Same enablement as the commit bar actions and the palette: voice never has a path of its own.</summary>
        public bool IsEnabled(string commandId)
        {
            if (!Active || _session == null) return false;
            var status = _session.Status;
            bool working = status == DesignStatus.Committing || status == DesignStatus.RefreshRequired || _mutations > 0;
            switch (commandId)
            {
                case CommandIds.CancelDraft: return Editable && !RequiresCadReview && !working;
                case CommandIds.Apply:
                    return Editable && !RequiresCadReview && !working && _session.CanApply
                        && _preview?.IsShowing == true && _preview.PlanId == _session.Preview?.PlanId;
                case CommandIds.Undo: return Idle && !RequiresCadReview && !InDraft && status == DesignStatus.Empty && _history?.CanUndo == true;
                case CommandIds.Redo: return Idle && !RequiresCadReview && !InDraft && status == DesignStatus.Empty && _history?.CanRedo == true;
                case CommandIds.Isolate: return Actions.First(a => a.Id == IdIsolate).Enabled;
                default: return false;   // design, sheet metal and inspect commands belong to other workspaces
            }
        }

        private static string CommandActionId(string commandId)
        {
            switch (commandId)
            {
                case CommandIds.CancelDraft: return CommitIds.Cancel;
                case CommandIds.Apply: return CommitIds.Apply;
                case CommandIds.Undo: return IdUndo;
                case CommandIds.Redo: return IdRedo;
                case CommandIds.Isolate: return IdIsolate;
                default: return null;
            }
        }

        /// <summary>
        /// Runs the command exactly as its action would. Apply never commits from here: it only shows what will be
        /// applied; the commit bar is the single commit path (M5-11).
        /// </summary>
        public bool Invoke(string commandId)
        {
            var id = CommandActionId(commandId);
            if (id == null || !IsEnabled(commandId)) return false;
            if (commandId == CommandIds.Apply)
            {
                SetNotice("Piano pronto: " + PlanSummary() + ". Conferma premendo Applica sulla barra di conferma.");
                Refresh();
                return true;
            }
            return Actions.First(a => a.Id == id).TryInvoke();
        }

        private string PlanSummary()
        {
            if (IsMove) return "spostamento di " + (_occurrence?.Name ?? "componente");
            if (_command == "assembly_constraint") return "vincolo " + RelationLabel(_type);
            if (_command == "assembly_joint") return "giunto " + RelationLabel(_type);
            return "modifica CAD";
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
