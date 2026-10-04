using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Declaration of the Ispeziona actions for the palette, the ring and voice: same ids, same enablement and same Invoke for
    /// every path. Local tools (Misura, Sezione, Scala, ambiente) stay available offline; the backend ones (Esplora, Proprietà,
    /// Documenti aperti) are disabled with a reason. Long lists (components, documents, scale) are dynamic picker tabs of at
    /// most eight entries, reached with the palette stick.
    /// </summary>
    public sealed partial class InspectWorkspace : IActionProvider
    {
        public const string TabMeasure = "misura", TabSection = "sezione", TabView = "vista";
        public const string TabVisibility = "visibilita", TabVerify = "verifica";
        public const string PickTabPrefix = "_pick.";

        public const string IdMeasure = "inspect.measure", IdMeasurePin = "inspect.measure.pin", IdMeasureCancel = "inspect.measure.cancel",
            IdMeasureClear = "inspect.measure.clear",
            IdSection = "inspect.section", IdSectionOffset = "inspect.section.offset", IdSectionAngle = "inspect.section.angle",
            IdSectionReset = "inspect.section.reset",
            IdBrowse = "inspect.browse", IdProperties = "inspect.properties", IdDocuments = "inspect.documents",
            IdScale = "inspect.scale", IdEnvironment = "inspect.environment",
            IdEnter = "inspect.context.enter", IdBack = "inspect.context.back",
            IdScaleOne = "inspect.scale.one", IdScaleFit = "inspect.scale.fit", IdScaleTable = "inspect.scale.table",
            IdScaleRoom = "inspect.scale.room", IdScaleRecenter = "inspect.scale.recenter",
            IdPickPrefix = "inspect.pick.";
        public const string IdXRay = "inspect.visibility.xray", IdIsolate = "inspect.visibility.isolate", IdHide = "inspect.visibility.hide",
            IdShowAll = "inspect.visibility.showall",
            IdInterference = "inspect.verify.interference", IdScope = "inspect.verify.scope", IdDistance = "inspect.verify.distance",
            IdHealth = "inspect.verify.health", IdResults = "inspect.verify.results", IdIgnore = "inspect.verify.ignore";

        private static readonly XrTab[] StaticTabs =
        {
            new XrTab(TabMeasure, "Misura"), new XrTab(TabSection, "Sezione"), new XrTab(TabView, "Vista"),
            new XrTab(TabVisibility, "Visibilità"), new XrTab(TabVerify, "Verifica"),
        };

        private sealed class PickerItem
        {
            public PickerItem(string label, Action choose, string id = null, bool backend = false)
            { Label = label; Choose = choose; Id = id; Backend = backend; }
            public string Label { get; }
            public Action Choose { get; }
            public string Id { get; }
            public bool Backend { get; }
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

        /// <summary>Ispeziona never changes the CAD: it has no commit bar.</summary>
        public CommitBarState CommitBar => null;

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

        /// <summary>Ring on a component: Proprietà, Apri contesto, Misura (same actions as the palette).</summary>
        public IEnumerable<XrAction> ContextActions(SelectionKind selection)
        {
            if (selection != SelectionKind.Component) return Array.Empty<XrAction>();
            var all = Actions.ToList();
            return new[] { IdProperties, IdEnter, IdMeasure }.Select(id => all.First(a => a.Id == id)).ToArray();
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

        private bool PickerOpen(Picker picker) => Active && _picker == picker;

        private void OpenBrowserPicker()
        {
            var children = _context.Current?.Children;
            if (children == null || children.Count == 0) { SetNotice("Nessun componente in questo contesto."); Refresh(); return; }
            SetNotice("Contesto: " + ContextPath());
            OpenPicker("Esplora", children.Select(node =>
            {
                var item = node;
                return new PickerItem((item == _selected ? "● " : "") + (item.Suppressed ? "[Soppresso] " : "") + item.Name,
                    () => SelectNode(item), backend: true);
            }));
        }

        private void OpenDocumentsPicker()
        {
            if (_documents.Count == 0) { SetNotice("Nessun documento aperto."); Refresh(); return; }
            SetNotice("Attivazione in Inventor, senza salvare o modificare il CAD.");
            OpenPicker("Documenti aperti", _documents.Select(doc =>
            {
                var item = doc;
                return new PickerItem(item.Name, () => Activate(item), backend: true);
            }));
        }

        private void OpenScalePicker()
        {
            OpenPicker("Scala", new[]
            {
                new PickerItem("Mantieni 1:1", () => SetScale(ModelScaleMode.OneToOne), IdScaleOne),
                new PickerItem("Adatta alla stanza", () => SetScale(ModelScaleMode.FitToRoom), IdScaleFit),
                new PickerItem("Scala da tavolo • 60 cm", () => SetScale(ModelScaleMode.Table), IdScaleTable),
                new PickerItem("Spazio disponibile: " + _roomExtent.ToString("0.##", _culture) + " m", () => AskNumber(IdScaleRoom,
                    "Spazio disponibile (m)", QuantityUnit.Meters, 0.2, 20, _roomExtent, v => _roomExtent = (float)v), IdScaleRoom),
                new PickerItem("Porta davanti a me", () => { Recenter(); SetNotice(""); Refresh(); }, IdScaleRecenter),
            });
        }

        // ---------------------------------------------------------------- availability

        private bool Local => Active && _ask == null;
        private bool Backend => Active && _online && !_busy && _backend != null && _scene != null;

        private string LocalReason() => !Active ? "Ispeziona non è aperto." : "Chiudi prima il tastierino.";
        private string BackendReason() => !Active ? "Ispeziona non è aperto." : !_online ? "Offline — strumenti locali disponibili."
            : _busy ? "Lettura Inventor in corso." : "Nessun documento disponibile.";

        private bool CanEnter => Active && _selected != null && _context.Current != null && _selected != _context.Current
            && _context.Current.Children.Contains(_selected) && !_selected.Suppressed;

        private XrAction[] BuildActions()
        {
            var env = _environment?.Mode == EnvironmentMode.MixedReality ? "Studio virtuale" : "Realtà mista";
            var list = new List<XrAction>
            {
                // Misura
                new XrAction(IdMeasure, "Punto-punto (locale)", TabMeasure, () => Local, BeginMeasure, LocalReason, new[] { "misurazione" }),
                new XrAction(IdMeasurePin, "Fissa misura", TabMeasure, () => Local && _measure != null && _measure.DistanceMm.HasValue, PinMeasure,
                    () => !Active ? LocalReason() : "Completa prima una misura tra due punti."),
                new XrAction(IdMeasureCancel, "Annulla misura", TabMeasure, () => Active && Measuring,
                    () => { _measure.Cancel(); SetNotice("Misura annullata."); Refresh(); }, () => "Nessuna misura in corso."),
                new XrAction(IdMeasureClear, "Rimuovi tutte", TabMeasure, () => Active && _measure != null && _measure.PinnedCount > 0,
                    () => { _measure.ClearAll(); SetNotice(""); Refresh(); }, () => "Nessuna misura fissata."),

                // Sezione
                new XrAction(IdSection, "Sezione", TabSection, () => Local, () => { _section.SetActive(!_section.Active); Refresh(); },
                    LocalReason, new[] { "seziona", "attiva sezione", "disattiva sezione" }, XrActionKind.Toggle, isOn: () => _section != null && _section.Active),
                new XrAction(IdSectionOffset, "Scostamento: " + Format(_section?.OffsetMm, "mm"), TabSection, () => Local,
                    () => AskNumber(IdSectionOffset, "Scostamento (mm)", QuantityUnit.Millimeters, -1000000, 1000000, _section.OffsetMm, v => _section.SetOffset((float)v)),
                    LocalReason, kind: XrActionKind.Numeric),
                new XrAction(IdSectionAngle, "Angolo Y: " + Format(_section?.AngleDegrees, "°"), TabSection, () => Local,
                    () => AskNumber(IdSectionAngle, "Angolo Y (gradi)", QuantityUnit.Degrees, -360, 360, _section.AngleDegrees, v => _section.SetAngle((float)v)),
                    LocalReason, kind: XrActionKind.Numeric),
                new XrAction(IdSectionReset, "Ripristina piano", TabSection, () => Local && _view != null,
                    () => { _section.ResetPlane(ScenePlacement.LocalBounds(_view.transform)); _section.SetActive(true); Refresh(); }, LocalReason),

                // Vista
                new XrAction(IdBrowse, "Esplora", TabView, () => Backend && _context.Current != null, OpenBrowserPicker, BackendReason, new[] { "browser" }),
                new XrAction(IdProperties, "Proprietà", TabView, () => Backend, () => { _showInfo = true; LoadInfo(); }, BackendReason),
                new XrAction(IdDocuments, "Documenti aperti", TabView, () => Backend, LoadDocuments, BackendReason, new[] { "documenti" }),
                new XrAction(IdScale, "Scala", TabView, () => Local && _view != null, OpenScalePicker, LocalReason),
                new XrAction(IdEnvironment, env, TabView, () => Local && _environment != null, () =>
                {
                    _environment.Set(_environment.Mode == EnvironmentMode.MixedReality ? EnvironmentMode.StudioVr : EnvironmentMode.MixedReality);
                    Refresh();
                }, LocalReason),
                new XrAction(IdEnter, "Apri contesto", TabView, () => CanEnter, () => Enter(_selected),
                    () => !Active ? LocalReason() : "Seleziona prima un componente con Esplora o toccandolo."),
                new XrAction(IdBack, "Indietro", TabView, () => Active && _context.Path.Count > 1, ContextBack,
                    () => !Active ? LocalReason() : "Sei già alla radice del documento."),

                // Visibilità (local, view only)
                new XrAction(IdXRay, "X-Ray", TabVisibility, () => Local && _selected != null,
                    () => VisibilityOnSelection(_visibility.XRay, "X-Ray"), () => !Active ? LocalReason() : "Seleziona prima un componente.",
                    voiceInvokes: false),
                new XrAction(IdIsolate, "Isola", TabVisibility, () => Local && _selected != null,
                    () => VisibilityOnSelection(_visibility.Isolate, "Isolato"), () => !Active ? LocalReason() : "Seleziona prima un componente.",
                    voiceInvokes: false),
                new XrAction(IdHide, "Nascondi", TabVisibility, () => Local && _selected != null,
                    () => VisibilityOnSelection(_visibility.Hide, "Nascosto"), () => !Active ? LocalReason() : "Seleziona prima un componente.",
                    voiceInvokes: false),
                new XrAction(IdShowAll, "Mostra tutto", TabVisibility, () => Local && (_visibility.AnyChanged || _focusSnapshot != null), ShowAllComponents,
                    () => !Active ? LocalReason() : "Tutti i componenti sono già visibili.", voiceInvokes: false),

                // Verifica (Inventor, read-only)
                new XrAction(IdInterference, _scopeSelection && _selected != null ? "Interferenze di " + _selected.Name : "Interferenze", TabVerify,
                    () => VerifyReady && (!_scopeSelection || DirectSelection), RunInterference,
                    () => VerifyReady ? "Seleziona un componente di primo livello o togli Solo selezione." : VerifyReason(), voiceInvokes: false),
                new XrAction(IdScope, "Solo selezione", TabVerify, () => Local && (_scopeSelection || DirectSelection),
                    () => { _scopeSelection = !_scopeSelection; Refresh(); },
                    () => !Active ? LocalReason() : "Seleziona un componente di primo livello.", kind: XrActionKind.Toggle,
                    isOn: () => _scopeSelection, voiceInvokes: false),
                new XrAction(IdDistance, _distanceA == null ? "Distanza minima" : "Distanza minima da " + _distanceA.Name, TabVerify,
                    () => VerifyReady && DirectSelection, Distance,
                    () => VerifyReady ? "Seleziona un componente di primo livello." : VerifyReason(), voiceInvokes: false),
                new XrAction(IdHealth, "Salute assieme", TabVerify, () => VerifyReady, RunHealth, VerifyReason, voiceInvokes: false),
                new XrAction(IdResults, "Risultati (" + _findings.Count + ")", TabVerify, () => Active && _findings.Count > 0, OpenResults,
                    () => !Active ? LocalReason() : "Nessun risultato da mostrare.", voiceInvokes: false),
                new XrAction(IdIgnore, "Ignora risultato", TabVerify, () => Active && _verifySession.Running != null, IgnoreRunning,
                    () => "Nessuna verifica in corso.", voiceInvokes: false),
            };

            if (_picker != null)
            {
                var picker = _picker;
                for (int i = 0; i < picker.Items.Count; i++)
                {
                    var item = picker.Items[i];
                    list.Add(new XrAction(item.Id ?? IdPickPrefix + i, item.Label, PickTabPrefix + i / ActionCatalog.MaxPalette,
                        () => PickerOpen(picker) && (!item.Backend || Backend), () => { ClosePicker(); item.Choose(); },
                        () => PickerOpen(picker) ? BackendReason() : "L’elenco è stato chiuso."));
                }
            }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- voice / runner command surface

        /// <summary>Misura is the only fixed-vocabulary command Ispeziona offers: same enablement as its action.</summary>
        public bool IsEnabled(string commandId) => commandId == CommandIds.Measure && Actions.First(a => a.Id == IdMeasure).Enabled;

        public bool Invoke(string commandId) => commandId == CommandIds.Measure && Actions.First(a => a.Id == IdMeasure).TryInvoke();
    }
}
