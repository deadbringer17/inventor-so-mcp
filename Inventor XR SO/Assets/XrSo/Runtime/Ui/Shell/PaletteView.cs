using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Tavolozza: profilo compatto M6 oppure Schizzo S1, pagine, gruppi annidati e tastierino.</summary>
    public sealed class PaletteView : MonoBehaviour
    {
        public const string OptionsTab = "opzioni";
        public const float TextMm = 7f;
        public const float LineSpacing = -52f;
        private static readonly string[] Keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "-", "0", ",", "←", "Annulla", "OK" };

        private ActionCatalog _catalog;
        private IActionProvider _lastProvider;
        private RectTransform _header, _grid, _groups, _status;
        private VerticalLayoutGroup _column;
        private TextMeshProUGUI _title, _chip;
        private NumericEntry _entry;
        private TextEntry _textEntry;
        private int _textPage;
        private const string TextCharacters="abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_+-*/^().,=[]<>%\"'°;:#";
        private string _keypadLabel;
        private IReadOnlyList<string> _group;
        private bool _groupBackable;
        private readonly List<string> _tabs = new List<string>();
        private readonly List<string> _previousMain = new List<string>();
        private readonly Stack<(IReadOnlyList<string> group, bool backable, string tab, int page)> _parents = new Stack<(IReadOnlyList<string>, bool, string, int)>();
        private int _page;
        private Transform _hand;
        private bool _pinned;
        private bool _handTracked = true, _trackingFallback;
        private WorkbenchFrame _fallbackFrame;
        private Vector3 _handOffset = new Vector3(0, 0.05f, 0.02f);
        private Quaternion _handRotation = Quaternion.Euler(45, 0, 0);
        private PaletteLayout _sketchLayout = PaletteLayout.Sketch;
        private string _renderSignature;

        public PaletteLayout Layout { get; private set; } = PaletteLayout.Compact;
        public int Page => _page;
        public int PageCount => Layout.PageCount(_catalog?.Palette(CurrentTab).Count ?? 0);
        public bool Pinned => _pinned;
        public bool HandTracked => _handTracked;
        public bool TrackingFallback => _trackingFallback;
        public Vector3 HandOffset => _handOffset;
        public Quaternion HandRotation => _handRotation;
        public int RebuildCount { get; private set; }

        public Canvas Canvas { get; private set; }
        public string CurrentTab { get; private set; }
        public bool KeypadVisible => _entry != null || _textEntry!=null;

        public static PaletteView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Tavolozza", new Vector2(160, 110));
            // Sopra la faccia del controller, inclinata verso chi guarda.
            canvas.transform.localPosition = new Vector3(0, 0.05f, 0.02f);
            canvas.transform.localRotation = Quaternion.Euler(45, 0, 0);
            var view = canvas.gameObject.AddComponent<PaletteView>();
            view.Canvas = canvas;
            view._hand = parent;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            var column = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            view._column = column;
            column.padding = new RectOffset(3, 3, 3, 3);
            column.spacing = 3;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandHeight = false;
            view._header = UiFactory.Panel(bg, "Schede", Color.clear);
            view._header.gameObject.AddComponent<LayoutElement>().preferredHeight = 12;
            view._title = UiFactory.Text(view._header, "", TextMm, FontStyles.Bold);
            view._title.alignment = TextAlignmentOptions.Center;
            UiFactory.Stretch(view._title.rectTransform);
            view._groups = UiFactory.Panel(bg, "Gruppi", Color.clear);
            view._groups.gameObject.AddComponent<LayoutElement>();
            view._chip = UiFactory.Text(bg, "", TextMm);
            view._chip.alignment = TextAlignmentOptions.Center;
            view._chip.gameObject.AddComponent<LayoutElement>().preferredHeight = 12;
            view._grid = UiFactory.Panel(bg, "Griglia", Color.clear);
            view._grid.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            view._status = UiFactory.Panel(bg, "Navigazione", Color.clear);
            view._status.gameObject.AddComponent<LayoutElement>();
            return view;
        }

        public void Render(ActionCatalog catalog)
        {
            _catalog = catalog;
            // Un workspace appena attivato parte dalla sua prima scheda, non da quella del precedente.
            if (catalog.Active != _lastProvider) { _lastProvider = catalog.Active; CurrentTab = null; _group = null; _groupBackable = false; _previousMain.Clear(); _parents.Clear(); _page = 0; }
            _tabs.Clear();
            var main = catalog.Tabs.Select(t => t.Id).ToList();
            _tabs.AddRange(_group ?? main);
            if (CurrentTab == null || !_tabs.Contains(CurrentTab)) CurrentTab = _tabs.FirstOrDefault();
            // «Opzioni» compare da sola (feature in costruzione, spostamento in bozza) e diventa la scheda attiva (M9 §2).
            if (_group == null && _previousMain.Count > 0 && main.Contains(OptionsTab) && !_previousMain.Contains(OptionsTab)) CurrentTab = OptionsTab;
            _previousMain.Clear();
            _previousMain.AddRange(main);
            Rebuild();
        }

        public void SelectTab(int delta)
        {
            if (_tabs.Count == 0 || KeypadVisible) return;
            int i = (_tabs.IndexOf(CurrentTab) + delta + _tabs.Count) % _tabs.Count;
            CurrentTab = _tabs[i];
            _page = 0;
            Rebuild();
        }

        /// <summary>Mostra una scheda per id, anche nascosta (scheda di elenco dinamica del workspace).</summary>
        public void ShowTab(string tabId)
        {
            if (tabId == null || !_tabs.Contains(tabId)) return;
            CurrentTab = tabId;
            _page = 0;
            Rebuild();
        }

        /// <summary>
        /// Limita la rotazione delle schede (stick sinistro) a un gruppo, p. es. le pagine di un elenco lungo, e mostra la prima.
        /// Le schede possono essere nascoste: si risolvono dal catalogo.
        /// </summary>
        public void ShowTabGroup(IReadOnlyList<string> tabIds, bool backable = false)
        {
            if (tabIds == null || tabIds.Count == 0) { ClearTabGroup(); return; }
            _parents.Push((_group, _groupBackable, CurrentTab, _page));
            _group = tabIds.ToList();
            _groupBackable = backable;
            _tabs.Clear();
            _tabs.AddRange(_group);
            CurrentTab = _group[0];
            _page = 0;
            Rebuild();
        }

        /// <summary>Torna alle schede visibili del catalogo.</summary>
        public void ClearTabGroup()
        {
            if (_group == null) return;
            if (_parents.Count > 0)
            {
                var previous = _parents.Pop();
                _group = previous.group; _groupBackable = previous.backable;
                CurrentTab = previous.tab; _page = previous.page;
            }
            else { _group = null; _groupBackable = false; _page = 0; }
            if (_catalog != null) Render(_catalog);
        }

        public bool InTabGroup => _group != null;

        /// <summary>
        /// X dentro un gruppo di schede «uscibile» (Ispeziona ▸): torna alle schede principali e mostra quella che l'ha aperto.
        /// False se non c'e un gruppo uscibile (un elenco di pagine si chiude dal suo workspace).
        /// </summary>
        public bool TryLeaveGroup(string returnTab = null)
        {
            if (_group == null || !_groupBackable) return false;
            ClearTabGroup();
            if (returnTab != null) ShowTab(returnTab);
            return true;
        }

        public void SelectPage(int delta)
        {
            if (KeypadVisible || PageCount <= 1) return;
            _page = ((_page + delta) % PageCount + PageCount) % PageCount;
            Rebuild();
        }

        public void SetSketchSize(double widthMm, double heightMm)
        {
            _sketchLayout = PaletteLayout.ForSketch(widthMm, heightMm);
            _page = 0;
            Rebuild();
        }

        public void SetHandPose(Vector3 offset, Quaternion rotation)
        {
            _handOffset = offset; _handRotation = rotation;
            if (!_pinned && !_trackingFallback) ApplyHandPose();
            Rebuild();
        }

        private void ApplyHandPose()
        {
            Canvas.transform.SetParent(_hand, false);
            Canvas.transform.localPosition = _handOffset;
            Canvas.transform.localRotation = _handRotation;
            Canvas.transform.localScale = Vector3.one * 0.001f;
        }

        public void PinToBench()
        {
            if (_pinned) return;
            Canvas.transform.SetParent(null, true);
            _pinned = true;
            _trackingFallback = false;
            Rebuild();
        }

        public void ReattachToHand()
        {
            _pinned = false;
            _trackingFallback = !_handTracked;
            if (_trackingFallback) ApplyFallbackPose(); else ApplyHandPose();
            Rebuild();
        }

        /// <summary>Fallback M6/S1 al bordo sinistro del banco; non cambia bozza, input o preferenze della mano.</summary>
        public void SetHandTracking(bool tracked, WorkbenchFrame frame)
        {
            if (!tracked && frame == null) throw new ArgumentNullException(nameof(frame));
            bool changed = _handTracked != tracked;
            _handTracked = tracked;
            bool frameChanged = !ReferenceEquals(_fallbackFrame, frame);
            if (!tracked) _fallbackFrame = frame;
            if (_pinned) return; // Una posa fissata dall'utente non segue il fallback.
            if (!tracked)
            {
                _trackingFallback = true;
                if (changed || frameChanged) ApplyFallbackPose();
            }
            else if (_trackingFallback)
            {
                _trackingFallback = false;
                ApplyHandPose();
            }
            if (changed) Rebuild();
        }

        private void ApplyFallbackPose()
        {
            var frame = _fallbackFrame;
            if (frame == null) return;
            var center = frame.Origin + frame.Forward * WorkbenchLayout.PartDistance
                - frame.Right * (WorkbenchLayout.SheetWidth / 2 + Layout.Width / 2000 + 0.02);
            Canvas.transform.SetParent(null, true);
            // Il bordo inferiore resta 10 mm sopra il piano anche al formato massimo.
            float lift = 0.01f + (float)Layout.Height * 0.0005f * Mathf.Cos(60 * Mathf.Deg2Rad);
            Canvas.transform.SetPositionAndRotation(new Vector3((float)center.X, (float)frame.DeskY + lift, (float)center.Z),
                Quaternion.Euler(60, (float)frame.YawDegrees, 0));
            Canvas.transform.localScale = Vector3.one * 0.001f;
        }

        public void ShowKeypad(NumericEntry entry, string label = null)
        {
            Detach();
            _entry = entry;
            _keypadLabel = label;
            _entry.Changed += Rebuild;
            _entry.Committed += OnCommitted;
            _entry.BeginEdit();
            Rebuild();
        }

        public void HideKeypad()
        {
            if(_textEntry!=null) {var text=_textEntry;Detach();text.CancelEdit();Rebuild();return;}
            if (_entry == null) return;
            var entry = _entry;
            Detach();
            entry.CancelEdit();
            Rebuild();
        }

        private void Detach()
        {
            if(_textEntry!=null) {_textEntry.Changed-=Rebuild;_textEntry.Committed-=OnTextCommitted;_textEntry=null;}
            if (_entry == null) return;
            _entry.Changed -= Rebuild;
            _entry.Committed -= OnCommitted;
            _entry = null;
        }

        // Il workspace puo aprire subito un nuovo tastierino dal suo gestore: ci si stacca solo se e ancora il nostro.
        private void OnCommitted()
        {
            if (_entry == null || _entry.Editing) return;
            Detach();
            Rebuild();
        }
        public void ShowTextKeypad(TextEntry entry,string label)
        {
            Detach();_textEntry=entry;_textPage=0;_keypadLabel=label;
            entry.Changed+=Rebuild;entry.Committed+=OnTextCommitted;entry.BeginEdit();Rebuild();
        }
        private void OnTextCommitted()
        {if(_textEntry==null || _textEntry.Editing) return;Detach();Rebuild();}

        private void Rebuild()
        {
            var previousLayout = Layout;
            var tab = _catalog?.FindTab(CurrentTab);
            Layout = _textEntry!=null || tab?.Profile == PaletteProfile.Sketch || (_catalog?.Palette(CurrentTab).Count ?? 0) > PaletteLayout.Compact.Capacity
                ? _sketchLayout : PaletteLayout.Compact;
            if (_trackingFallback && !ReferenceEquals(previousLayout, Layout)) ApplyFallbackPose();
            _page = Math.Min(_page, PageCount - 1);
            var signature = new StringBuilder().Append(CurrentTab).Append('|').Append(_page).Append('|').Append(_parents.Count)
                .Append('|').Append(_groupBackable).Append('|').Append(Layout.Width).Append('|').Append(Layout.Height)
                .Append('|').Append(_pinned).Append('|').Append(_trackingFallback).Append('|').Append(_entry?.Display).Append('|').Append(_keypadLabel)
                .Append('|').Append(KeypadVisible).Append('|').Append(_textEntry?.Display).Append('|').Append(_textEntry?.Error).Append('|').Append(_textPage);
            foreach (var id in _tabs) signature.Append('|').Append(id).Append(':').Append(_catalog?.FindTab(id)?.Label);
            if (!KeypadVisible && _catalog != null && CurrentTab != null)
                foreach (var a in _catalog.PalettePage(CurrentTab, Layout, _page))
                    signature.Append('|').Append(RuntimeHelpers.GetHashCode(a)).Append(':').Append(a.Label).Append(':')
                        .Append(a.Enabled).Append(':').Append(a.IsOn).Append(':').Append(a.DisabledReason);
            string nextSignature = signature.ToString();
            if (nextSignature == _renderSignature)
            {
                foreach (var tooltip in GetComponentsInChildren<ActionTooltip>()) tooltip.OnPointerExit(null);
                return;
            }
            _renderSignature = nextSignature;
            RebuildCount++;
            ((RectTransform)Canvas.transform).sizeDelta = new Vector2((float)Layout.Width, (float)Layout.Height);
            _column.padding = new RectOffset((int)Layout.MarginX, (int)Layout.MarginX, (int)Layout.MarginY, (int)Layout.MarginY);
            _column.spacing = (float)Layout.Gap;
            _header.GetComponent<LayoutElement>().preferredHeight = (float)Layout.HeaderHeight;
            UiFactory.ClearChildren(_groups);
            UiFactory.ClearChildren(_status);
            bool expanded = Layout.Profile == PaletteProfile.Sketch && !KeypadVisible;
            _groups.gameObject.SetActive(expanded);
            _groups.GetComponent<LayoutElement>().preferredHeight = (float)Layout.GroupHeight;
            _status.gameObject.SetActive(!KeypadVisible && (expanded || PageCount > 1));
            _status.GetComponent<LayoutElement>().preferredHeight = (float)Layout.StatusHeight;
            for (int i = _header.childCount - 1; i >= 0; i--)
                if (_header.GetChild(i) != _title.transform)
                {
                    var child = _header.GetChild(i).gameObject;
                    child.SetActive(false);
                    if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
                }
            UiFactory.ClearChildren(_grid);
            var grid = _grid.GetComponent<GridLayoutGroup>() ?? _grid.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(3, 3);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _chip.GetComponent<LayoutElement>().preferredHeight=_textEntry!=null ? 36 : 12;
            if(_textEntry!=null)
            {
                grid.constraintCount=4;grid.cellSize=new Vector2((float)((Layout.Width-2*Layout.MarginX-9)/4),(float)((Layout.Height-2*Layout.MarginY-48)/4));
                _header.gameObject.SetActive(false);_chip.gameObject.SetActive(true);
                _chip.text=_keypadLabel+" • "+(_textPage+1)+"/"+((TextCharacters.Length+7)/8)+"\n"+_textEntry.Display+(_textEntry.Error.Length==0 ? "" : "\n"+_textEntry.Error);
                var textKeys=TextCharacters.Skip(_textPage*8).Take(8).Select(c=>c.ToString()).Concat(new[] {"‹ tasti","tasti ›","Spazio","◀","▶","←","Annulla","OK"});
                foreach(var key in textKeys) {var k=key;FitLabel(UiFactory.TextButton(_grid,k,k=="OK" ? UiFactory.Accent : UiFactory.Key,TextMm,()=>PressText(k)));}
                return;
            }
            if (KeypadVisible)
            {
                grid.constraintCount = 3;
                grid.cellSize = new Vector2((float)((Layout.Width - 2 * Layout.MarginX - 6) / 3), 15);
                _header.gameObject.SetActive(false);
                _chip.gameObject.SetActive(true);
                _chip.text = (_keypadLabel ?? "Valore") + ": " + _entry.Display;
                foreach (var key in Keys) { var k = key; FitLabel(UiFactory.TextButton(_grid, k, k == "OK" ? UiFactory.Accent : UiFactory.Key, TextMm, () => Press(k))); }
                return;
            }
            grid.constraintCount = Layout.Columns;
            grid.spacing = Vector2.one * (float)Layout.Gap;
            grid.cellSize = new Vector2((float)Layout.CellWidth, (float)Layout.CellHeight);
            _header.gameObject.SetActive(true);
            _chip.gameObject.SetActive(false);
            _title.text = tab == null ? "" : (_parents.Count > 0 ? string.Join(" › ", _parents.Reverse().Select(p => _catalog.FindTab(p.tab)?.Label).Where(l => !string.IsNullOrEmpty(l))) + " › " : "‹ ") + tab.Label + " ›";
            if (_catalog == null || CurrentTab == null) return;
            _page = Math.Min(_page, PageCount - 1);
            if (expanded)
            {
                var groups = _groups.GetComponent<GridLayoutGroup>() ?? _groups.gameObject.AddComponent<GridLayoutGroup>();
                groups.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                groups.constraintCount = 4;
                groups.spacing = new Vector2(2, 2);
                groups.cellSize = new Vector2((float)((Layout.Width - 2 * Layout.MarginX - 6) / 4), 15);
                int groupCapacity = _tabs.Count > 8 ? 7 : 8;
                int groupPage = Math.Max(0, _tabs.IndexOf(CurrentTab)) / groupCapacity;
                foreach (var id in _tabs.Skip(groupPage * groupCapacity).Take(groupCapacity))
                {
                    var next = id;
                    FitLabel(UiFactory.TextButton(_groups, _catalog.FindTab(id)?.Label ?? id, id == CurrentTab ? UiFactory.Accent : UiFactory.Key, TextMm, () => ShowTab(next)));
                }
                if (_tabs.Count > 8)
                {
                    int pages = (_tabs.Count + groupCapacity - 1) / groupCapacity;
                    string next = _tabs[((groupPage + 1) % pages) * groupCapacity];
                    FitLabel(UiFactory.TextButton(_groups, "Altri ›", UiFactory.Key, TextMm, () => ShowTab(next)));
                }
            }
            var headerRow = _header.GetComponent<HorizontalLayoutGroup>() ?? _header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerRow.enabled = expanded || PageCount > 1;
            headerRow.spacing = 2; headerRow.childControlWidth = headerRow.childControlHeight = true;
            headerRow.childForceExpandWidth = false;
            var titleSizing = _title.gameObject.GetComponent<LayoutElement>() ?? _title.gameObject.AddComponent<LayoutElement>();
            titleSizing.flexibleWidth = 1;
            if (headerRow.enabled)
            {
                void HeaderButton(string label, Action invoke, bool first = false)
                {
                    var b = UiFactory.TextButton(_header, label, UiFactory.Key, TextMm, invoke);
                    var sizing = b.gameObject.AddComponent<LayoutElement>();
                    sizing.preferredWidth = label == "Torna" ? 42 : 18;
                    if (first) b.transform.SetAsFirstSibling();
                    FitLabel(b);
                }
                if (_groupBackable) HeaderButton("Torna", () => TryLeaveGroup(), true);
                if (PageCount > 1)
                {
                    HeaderButton("‹", () => SelectPage(-1), true);
                    HeaderButton("›", () => SelectPage(1));
                }
            }
            else UiFactory.Stretch(_title.rectTransform);
            if (_status.gameObject.activeSelf)
            {
                var status = UiFactory.Text(_status, (_trackingFallback ? "Banco · controller sinistro perso · " : _pinned ? "Banco · " : "") + "Pagina " + (_page + 1) + "/" + PageCount, TextMm);
                UiFactory.Stretch(status.rectTransform);
                status.alignment = TextAlignmentOptions.Center;
            }
            foreach (var action in _catalog.PalettePage(CurrentTab, Layout, _page))
            {
                var a = action;
                var b = UiFactory.ActionButton(_grid, a, TextMm, 16f, () => a.TryInvoke());
                FitLabel(b);
                b.interactable = a.Enabled;
            }
        }

        // Le etichette vanno a capo (max 2 righe) e non vengono mai troncate.
        private static void FitLabel(Button b)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>(true);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = new Vector4(2, 0, 2, 0);
            t.lineSpacing = LineSpacing;
        }

        private void Press(string key)
        {
            if (_entry == null) return;
            if (key == "OK") { _entry.Commit(out _); return; }
            if (key == "Annulla") { HideKeypad(); return; }
            if (key == "←") { _entry.Backspace(); return; }
            _entry.Type(key[0]);
        }
        private void PressText(string key)
        {
            if(_textEntry==null) return;
            switch(key)
            {
                case "OK":_textEntry.Commit(out _);return;case "Annulla":HideKeypad();return;
                case "←":_textEntry.Backspace();return;case "◀":_textEntry.MoveCursor(-1);return;case "▶":_textEntry.MoveCursor(1);return;
                case "Spazio":_textEntry.Type(' ');return;
                case "‹ tasti":_textPage=(_textPage+(TextCharacters.Length+7)/8-1)%((TextCharacters.Length+7)/8);Rebuild();return;
                case "tasti ›":_textPage=(_textPage+1)%((TextCharacters.Length+7)/8);Rebuild();return;
                default:_textEntry.Type(key[0]);return;
            }
        }
    }
}
