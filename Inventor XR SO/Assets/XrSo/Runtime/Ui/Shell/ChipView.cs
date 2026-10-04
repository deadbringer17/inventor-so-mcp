using System;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Chip valore: etichetta, valore e passo di un NumericEntry, billboard verso la testa. Un tocco apre il tastierino.</summary>
    public sealed class ChipView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const float CapMm = 14f, TargetMinMm = 25f, BorderMm = 3f;
        public static readonly Vector2 SizeMm = new Vector2(200, 64);
        public static readonly Color Border = UiTheme.Border;
        public static readonly Color ArmedBorder = UiTheme.Signal;
        public static readonly Color Fill = UiTheme.Navy;
        public static readonly Color ArmedFill = UiTheme.Surface;

        private Image _border, _fill;
        private TextMeshProUGUI _value, _step;
        private Transform _head;
        private string _label = "";
        private bool _modified, _armed;

        public Canvas Canvas { get; private set; }
        public Button Button { get; private set; }
        public NumericEntry Entry { get; private set; }
        public event Action Tapped;

        public bool Modified => _modified;
        public bool Armed { get => _armed; set { _armed = value; Restyle(); } }
        public Color BorderColor => _border.color;
        public Color FillColor => _fill.color;
        public string ValueText => _value.text;
        public string StepText => _step.text;

        public static ChipView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Chip", SizeMm);
            var view = canvas.gameObject.AddComponent<ChipView>();
            view.Canvas = canvas;
            var border = UiFactory.Panel(canvas.transform, "Bordo", Border);
            UiFactory.Stretch(border);
            view._border = border.GetComponent<Image>();
            view.Button = border.gameObject.AddComponent<Button>();
            view._border.raycastTarget = true;
            view.Button.targetGraphic = view._border;
            view.Button.transition = Selectable.Transition.None;
            view.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            view.Button.onClick.AddListener(view.Tap);
            var fill = UiFactory.Panel(border, "Sfondo", Fill);
            fill.GetComponent<Image>().raycastTarget = false;
            UiFactory.Stretch(fill);
            fill.offsetMin = new Vector2(BorderMm, BorderMm);
            fill.offsetMax = new Vector2(-BorderMm, -BorderMm);
            view._fill = fill.GetComponent<Image>();
            var column = fill.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(6, 6, 2, 2);
            column.childAlignment = TextAnchor.MiddleCenter;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandHeight = true;
            view._value = UiFactory.Text(fill, "", CapMm, FontStyles.Bold);
            view._value.alignment = TextAlignmentOptions.Center;
            view._value.textWrappingMode = TextWrappingModes.NoWrap;
            view._step = UiFactory.Text(fill, "", CapMm);
            view._step.alignment = TextAlignmentOptions.Center;
            view._step.textWrappingMode = TextWrappingModes.NoWrap;
            return view;
        }

        public void Bind(NumericEntry entry, string label)
        {
            if (Entry != null) Entry.Changed -= Refresh;
            Entry = entry;
            _label = label ?? "";
            if (Entry != null) Entry.Changed += Refresh;
            Refresh();
        }

        /// <summary>Bordo blu: valore cambiato in bozza e non ancora in anteprima.</summary>
        public void SetModified(bool modified) { _modified = modified; Restyle(); }

        public void Place(Vector3 worldPos) { Canvas.transform.position = worldPos; FaceHead(); }

        public void Face(Transform head) { _head = head; FaceHead(); }

        public void Tap() => Tapped?.Invoke();

        public static string StepLabel(NumericEntry entry)
        {
            string unit = entry.Unit == QuantityUnit.Degrees ? "°" : entry.Unit == QuantityUnit.Millimeters ? " mm" : entry.Unit == QuantityUnit.Meters ? " m" : "";
            return "passo " + NumericEntry.Format(entry.Step) + unit;
        }

        private void Refresh()
        {
            if (Entry == null) { _value.text = _step.text = ""; return; }
            _value.text = _label.Length > 0 ? _label + ": " + Entry.Display : Entry.Display;
            _step.text = StepLabel(Entry);
        }

        private void Restyle()
        {
            _border.color = _hovered ? UiTheme.Signal : _modified ? UiTheme.Preview : _armed ? ArmedBorder : Border;
            _fill.color = _armed ? ArmedFill : Fill;
        }

        private bool _hovered;
        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; Restyle(); }
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; Restyle(); }

        private void LateUpdate() { if (_head != null) FaceHead(); }

        private void FaceHead()
        {
            if (_head == null) return;
            var d = Canvas.transform.position - _head.position;
            if (d.sqrMagnitude > 1e-8f) Canvas.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        private void OnDestroy() { if (Entry != null) Entry.Changed -= Refresh; }
    }
}
