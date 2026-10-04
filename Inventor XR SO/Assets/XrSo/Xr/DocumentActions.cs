using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Scheda fissa "Documento" della tavolozza (M9): percorso di navigazione, Torna, Salva, documenti aperti,
    /// ricentra/calibrazione della postazione, connessione. Percorso e documenti aperti sono elenchi su schede
    /// dinamiche nascoste (prefisso "_"), a pagine da <see cref="PageSize"/>, come i picker dei workspace.
    /// </summary>
    public sealed class DocumentActions : IActionProvider
    {
        public const string IdBack = "doc.back", IdSave = "doc.save", IdRecenter = "doc.recenter", IdCalibrate = "doc.calibrate",
            IdConnection = "doc.connection", IdExit = "doc.exit", IdPath = "doc.path", IdDocuments = "doc.documents",
            IdCrumbPrefix = "doc.crumb.", IdOpenPrefix = "doc.open.", IdListClose = "doc.list.close.";
        public const string CrumbTabPrefix = "_doc_crumbs", OpenTabPrefix = "_doc_open_";
        /// <summary>Voci per pagina: più "Chiudi elenco" restano entro <see cref="ActionCatalog.MaxPalette"/>.</summary>
        public const int PageSize = 6;

        private sealed class ListItem
        {
            public ListItem(string id, string label, Action choose) { Id = id; Label = label; Choose = choose; }
            public string Id { get; }
            public string Label { get; }
            public Action Choose { get; }
        }

        private sealed class OpenList
        {
            public string Title, TabPrefix, ReturnTab;
            public List<ListItem> Items;
            public int Pages => Math.Max(1, (Items.Count + PageSize - 1) / PageSize);
        }

        private static readonly XrTab[] StaticTabs = { new XrTab(ActionCatalog.DocumentTab, "Documento") };

        private readonly NavigationStack _stack;
        private readonly Action _goBack, _save, _recenter, _calibrate, _openConnection, _leaveSession;
        private readonly Func<bool> _inSession, _canNavigate;
        private readonly Func<IReadOnlyList<OpenDocument>> _openDocs;
        private readonly Action<string> _jumpTo;
        private readonly XrAction[] _fixed;
        private OpenList _list;
        private PaletteView _palette;

        public DocumentActions(NavigationStack stack, Action goBack, Action save, Action recenter,
            Action calibrateDesk, Action openConnection, Action leaveSession,
            Func<bool> inSession, Func<bool> canNavigate, Func<IReadOnlyList<OpenDocument>> openDocs,
            Action<string> jumpTo)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
            _goBack = goBack ?? throw new ArgumentNullException(nameof(goBack));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _recenter = recenter ?? throw new ArgumentNullException(nameof(recenter));
            _calibrate = calibrateDesk ?? throw new ArgumentNullException(nameof(calibrateDesk));
            _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
            _leaveSession = leaveSession ?? throw new ArgumentNullException(nameof(leaveSession));
            _inSession = inSession ?? throw new ArgumentNullException(nameof(inSession));
            _canNavigate = canNavigate ?? throw new ArgumentNullException(nameof(canNavigate));
            _openDocs = openDocs ?? throw new ArgumentNullException(nameof(openDocs));
            _jumpTo = jumpTo ?? throw new ArgumentNullException(nameof(jumpTo));

            string Offline() => "Nessuna sessione con Inventor.";
            string NavBlocked() => _inSession() ? "Controlla prima la modifica CAD non confermata." : Offline();
            string tab = ActionCatalog.DocumentTab;
            _fixed = new[]
            {
                new XrAction(IdPath, "Percorso", tab, () => _inSession() && _stack.Levels.Count > 0, OpenPath,
                    () => _inSession() ? "Nessun documento attivo." : Offline(), new[] { "percorso documento" }),
                new XrAction(IdBack, "Torna", tab, () => _inSession() && _stack.CanPop && _canNavigate(), _goBack,
                    () => !_inSession() ? Offline() : !_stack.CanPop ? "Sei già al livello più alto." : NavBlocked(), new[] { "indietro" }),
                new XrAction(IdSave, "Salva", tab, _inSession, _save, Offline),
                new XrAction(IdDocuments, "Documenti aperti", tab, () => _inSession() && _canNavigate(), OpenDocuments,
                    NavBlocked, new[] { "documenti" }),
                new XrAction(IdRecenter, "Ricentra postazione", tab, _inSession, _recenter, Offline),
                new XrAction(IdCalibrate, "Calibra piano", tab, _inSession, _calibrate, Offline, new[] { "calibra" }),
                new XrAction(IdConnection, "Connessione", tab, () => true, _openConnection),
                new XrAction(IdExit, "Esci", tab, () => true, _leaveSession, null, new[] { "esci dalla sessione" }),
            };
        }

        /// <summary>Palette su cui mostrare il gruppo di pagine dell'elenco aperto; opzionale (senza, resta la scheda corrente).</summary>
        public void Attach(PaletteView palette) { _palette = palette; }

        /// <summary>Notifica al catalogo che schede e azioni sono cambiate (da collegare a <see cref="ActionCatalog.NotifyChanged"/>).</summary>
        public Action Changed { get; set; }

        public bool ListOpen => _list != null;

        public IReadOnlyList<XrTab> Tabs
        {
            get
            {
                if (_list == null) return StaticTabs;
                var tabs = new List<XrTab>(StaticTabs);
                for (int page = 0; page < _list.Pages; page++)
                    tabs.Add(new XrTab(_list.TabPrefix + page, _list.Pages == 1 ? _list.Title : _list.Title + " " + (page + 1) + "/" + _list.Pages));
                return tabs;
            }
        }

        public IEnumerable<XrAction> Actions
        {
            get
            {
                var all = new List<XrAction>(_fixed);
                if (_list != null)
                {
                    var list = _list;
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var item = list.Items[i];
                        all.Add(new XrAction(item.Id, item.Label, list.TabPrefix + i / PageSize, () => ReferenceEquals(_list, list),
                            () => { CloseList(); item.Choose(); }, () => "L’elenco è stato chiuso."));
                    }
                    for (int p = 0; p < list.Pages; p++)
                        all.Add(new XrAction(IdListClose + p, "Chiudi elenco", list.TabPrefix + p, () => ReferenceEquals(_list, list),
                            () => CloseList(), () => "L’elenco è stato chiuso."));
                }
                return all;
            }
        }

        public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Enumerable.Empty<XrAction>();
        public CommitBarState CommitBar => null;

        private void OpenPath()
        {
            var items = _stack.Levels.Select((level, i) =>
            {
                string docId = level.DocumentId;
                bool top = i == _stack.Levels.Count - 1;
                return new ListItem(IdCrumbPrefix + i, (top ? "● " : "") + (level.Dirty ? level.Name + " ●" : level.Name),
                    () => { if (!top) _jumpTo(docId); });
            }).ToList();
            ShowList("Percorso", CrumbTabPrefix, items);
        }

        private void OpenDocuments()
        {
            var docs = _openDocs() ?? Array.Empty<OpenDocument>();
            var items = docs.Select((doc, n) =>
            {
                string id = doc.Id;
                return new ListItem(IdOpenPrefix + n, doc.Name, () => _jumpTo(id));
            }).ToList();
            ShowList("Documenti aperti", OpenTabPrefix, items);
        }

        private void ShowList(string title, string tabPrefix, List<ListItem> items)
        {
            if (items.Count == 0) return;
            string back = _list?.ReturnTab ?? _palette?.CurrentTab;
            _list = new OpenList { Title = title, TabPrefix = tabPrefix, Items = items, ReturnTab = back };
            Changed?.Invoke();
            _palette?.ShowTabGroup(Enumerable.Range(0, _list.Pages).Select(p => tabPrefix + p).ToList());
        }

        /// <summary>Chiude l'elenco aperto e torna alla scheda da cui era stato aperto.</summary>
        public void CloseList()
        {
            if (_list == null) return;
            string back = _list.ReturnTab;
            _list = null;
            _palette?.ClearTabGroup();
            Changed?.Invoke();
            if (back != null) _palette?.ShowTab(back);
        }
    }
}
