using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Technical Home (spec §5.1): status text, action buttons and a keypad for addresses and codes.</summary>
    public sealed class HomePanel : MonoBehaviour
    {
        private static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
            "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t",
            "u", "v", "w", "x", "y", "z", ".", ":", "-", "[", "]", UiText.KeyBack, UiText.KeyOk, UiText.Cancel };
        private Text _title, _body, _entryText;
        private RectTransform _actions, _keypad;
        private string _entry = "";
        private Action<string> _submit;
        private Action _cancel;
        private string _voiceNumericUnit;
        private double _voiceNumericMin, _voiceNumericMax;

        public Canvas Canvas { get; private set; }
        public bool HasVoiceNumericPrompt => _submit != null && _voiceNumericUnit != null && gameObject.activeInHierarchy;
        public string VoiceNumericId => HasVoiceNumericPrompt ? _title.text : null;
        public string VoiceNumericUnit => _voiceNumericUnit;
        public double VoiceNumericMin => _voiceNumericMin;
        public double VoiceNumericMax => _voiceNumericMax;

        public bool SubmitVoiceNumber(double value)
        {
            if (!HasVoiceNumericPrompt || double.IsNaN(value) || double.IsInfinity(value)
                || value < _voiceNumericMin || value > _voiceNumericMax) return false;
            _entry = value.ToString("R", CultureInfo.InvariantCulture);
            _entryText.text = _entry;
            Press(UiText.KeyOk);
            return true;
        }

        /// <summary>Azioni visibili del pannello, lette sul thread UI dal mirror vocale.</summary>
        public IEnumerable<(string label, bool enabled)> VoiceActions
        {
            get
            {
                if (_actions == null || !gameObject.activeInHierarchy) yield break;
                foreach (Transform child in _actions)
                {
                    var button = child.GetComponent<Button>();
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    var label = button.GetComponentInChildren<Text>()?.text;
                    if (!string.IsNullOrWhiteSpace(label)) yield return (label, button.interactable);
                }
            }
        }

        public bool InvokeVoiceAction(string label)
        {
            foreach (Transform child in _actions)
            {
                var button = child.GetComponent<Button>();
                if (button != null && button.gameObject.activeInHierarchy && button.interactable
                    && button.GetComponentInChildren<Text>()?.text == label)
                { button.onClick.Invoke(); return true; }
            }
            return false;
        }

        public static HomePanel Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Home", new Vector2(820, 620));
            var panel = canvas.gameObject.AddComponent<HomePanel>();
            panel.Canvas = canvas;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            var background = UiFactory.Panel(transform, "Background", UiFactory.Background);
            UiFactory.Stretch(background);
            var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 28, 28);
            layout.spacing = 16;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            _title = UiFactory.Label(background, UiText.AppTitle, 40, FontStyle.Bold);
            _body = UiFactory.Label(background, "", 26);
            _entryText = UiFactory.Label(background, "", 44, FontStyle.Bold);
            _actions = Grid(background, "Actions", new Vector2(360, 72), 2);
            _keypad = Grid(background, "Keypad", new Vector2(64, 50), 10);
            foreach (var key in Keys)
            {
                var k = key;
                UiFactory.Button(_keypad, key, key == UiText.KeyOk ? UiFactory.Accent : UiFactory.Key, key == UiText.Cancel ? 16 : 26, () => Press(k));
            }
            HideEntry();
        }

        private static RectTransform Grid(Transform parent, string name, Vector2 cell, int columns)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = new Vector2(12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            return (RectTransform)go.transform;
        }

        public void ShowMessage(string title, string body)
        {
            _title.text = title;
            _body.text = body;
            HideEntry();
        }

        public void SetActions(params (string label, Action action)[] actions)
        {
            var old = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in _actions) old.Add(child);   // not while iterating: DestroyImmediate edits the list
            foreach (var child in old)
            {
                child.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            foreach (var (label, action) in actions) UiFactory.Button(_actions, label, UiFactory.Accent, 28, action);
        }

        public void PromptText(string title, string hint, string initial, Action<string> onSubmit, Action onCancel,
            string voiceNumericUnit = null, double voiceNumericMin = -1000000, double voiceNumericMax = 1000000)
        {
            _title.text = title;
            _body.text = hint;
            _entry = initial ?? "";
            _entryText.text = _entry;
            _submit = onSubmit;
            _cancel = onCancel;
            _voiceNumericUnit = voiceNumericUnit;
            _voiceNumericMin = voiceNumericMin;
            _voiceNumericMax = voiceNumericMax;
            SetActions();
            _entryText.gameObject.SetActive(true);
            _keypad.gameObject.SetActive(true);
        }

        public void Press(string key)
        {
            if (key == UiText.KeyBack)
            {
                if (_entry.Length > 0) _entry = _entry.Substring(0, _entry.Length - 1);
            }
            else if (key == UiText.KeyOk)
            {
                var submit = _submit;
                var value = _entry;
                HideEntry();
                submit?.Invoke(value);
                return;
            }
            else if (key == UiText.Cancel)
            {
                var cancel = _cancel;
                HideEntry();
                cancel?.Invoke();
                return;
            }
            else if (_entry.Length < 64) _entry += key;
            _entryText.text = _entry;
        }

        private void HideEntry()
        {
            _submit = null;
            _voiceNumericUnit = null;
            _cancel = null;
            _entryText.gameObject.SetActive(false);
            _keypad.gameObject.SetActive(false);
        }
    }
}
