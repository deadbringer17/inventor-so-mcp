using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Tavolozza sul controller sinistro: scheda corrente in griglia 2×4 (160×110 mm), oppure tastierino.</summary>
    public sealed class PaletteView : MonoBehaviour
    {
        public const string OptionsTab = "opzioni";
        public const float TextMm = 7f;
        public const float LineSpacing = -52f;
        private static readonly string[] Keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "-", "0", ",", "←", "Annulla", "OK" };

        private ActionCatalog _catalog;
        private IActionProvider _lastProvider;
        private RectTransform _header, _grid;
        private TextMeshProUGUI _title, _chip;
        private NumericEntry _entry;
        private string _keypadLabel;
        private IReadOnlyList<string> _group;
        private bool _groupBackable;
        private readonly List<string> _tabs = new List<string>();
        private readonly List<string> _previousMain = new List<string>();

        public Canvas Canvas { get; private set; }
        public string CurrentTab { get; private set; }
        public bool KeypadVisible => _entry != null;

        public static PaletteView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Tavolozza", new Vector2(160, 110));
            // Sopra la faccia del controller, inclinata verso chi guarda.
            canvas.transform.localPosition = new Vector3(0, 0.05f, 0.02f);
            canvas.transform.localRotation = Quaternion.Euler(45, 0, 0);
            var view = canvas.gameObject.AddComponent<PaletteView>();
            view.Canvas = canvas;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            var column = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(3, 3, 3, 3);
            column.spacing = 3;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandHeight = false;
            view._header = UiFactory.Panel(bg, "Schede", Color.clear);
            view._header.gameObject.AddComponent<LayoutElement>().preferredHeight = 12;
            view._title = UiFactory.Text(view._header, "", TextMm, FontStyles.Bold);
            view._title.alignment = TextAlignmentOptions.Center;
            UiFactory.Stretch(view._title.rectTransform);
            view._chip = UiFactory.Text(bg, "", TextMm);
            view._chip.alignment = TextAlignmentOptions.Center;
            view._chip.gameObject.AddComponent<LayoutElement>().preferredHeight = 12;
            view._grid = UiFactory.Panel(bg, "Griglia", Color.clear);
            view._grid.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            return view;
        }

        public void Render(ActionCatalog catalog)
        {
            _catalog = catalog;
            // Un workspace appena attivato parte dalla sua prima scheda, non da quella del precedente.
            if (catalog.Active != _lastProvider) { _lastProvider = catalog.Active; CurrentTab = null; _group = null; _groupBackable = false; _previousMain.Clear(); }
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
            Rebuild();
        }

        /// <summary>Mostra una scheda per id, anche nascosta (scheda di elenco dinamica del workspace).</summary>
        public void ShowTab(string tabId)
        {
            if (tabId == null || !_tabs.Contains(tabId)) return;
            CurrentTab = tabId;
            Rebuild();
        }

        /// <summary>
        /// Limita la rotazione delle schede (stick sinistro) a un gruppo, p. es. le pagine di un elenco lungo, e mostra la prima.
        /// Le schede possono essere nascoste: si risolvono dal catalogo.
        /// </summary>
        public void ShowTabGroup(IReadOnlyList<string> tabIds, bool backable = false)
        {
            if (tabIds == null || tabIds.Count == 0) { ClearTabGroup(); return; }
            _group = tabIds.ToList();
            _groupBackable = backable;
            _tabs.Clear();
            _tabs.AddRange(_group);
            CurrentTab = _group[0];
            Rebuild();
        }

        /// <summary>Torna alle schede visibili del catalogo.</summary>
        public void ClearTabGroup()
        {
            if (_group == null) return;
            _group = null; _groupBackable = false;
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
            if (_entry == null) return;
            var entry = _entry;
            Detach();
            entry.CancelEdit();
            Rebuild();
        }

        private void Detach()
        {
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

        private void Rebuild()
        {
            UiFactory.ClearChildren(_grid);
            var grid = _grid.GetComponent<GridLayoutGroup>() ?? _grid.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(3, 3);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            if (KeypadVisible)
            {
                grid.constraintCount = 3;
                grid.cellSize = new Vector2(49, 15);
                _header.gameObject.SetActive(false);
                _chip.gameObject.SetActive(true);
                _chip.text = (_keypadLabel ?? "Valore") + ": " + _entry.Display;
                foreach (var key in Keys) { var k = key; FitLabel(UiFactory.TextButton(_grid, k, k == "OK" ? UiFactory.Accent : UiFactory.Key, TextMm, () => Press(k))); }
                return;
            }
            grid.constraintCount = 2;
            grid.cellSize = new Vector2(75.5f, 20);
            _header.gameObject.SetActive(true);
            _chip.gameObject.SetActive(false);
            var tab = _catalog?.FindTab(CurrentTab);
            _title.text = tab == null ? "" : "‹ " + tab.Label + " ›";
            if (_catalog == null || CurrentTab == null) return;
            foreach (var action in _catalog.Palette(CurrentTab))
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
    }
}
