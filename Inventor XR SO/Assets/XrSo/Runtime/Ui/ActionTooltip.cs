using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Names and disabled reasons remain discoverable, including on non-interactable buttons.</summary>
    [ExecuteAlways]
    public sealed class ActionTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private XrAction _action;
        private float _textMm;
        private RectTransform _panel;
        private TextMeshProUGUI _text;
        public bool Visible => _panel != null && _panel.gameObject.activeSelf;
        public string Text => _text != null ? _text.text : "";

        public void Initialize(XrAction action, float textMm) { _action = action; _textMm = textMm; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_action == null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            if (_panel == null)
            {
                _panel = UiFactory.Panel(canvas.transform, "Suggerimento azione", UiTheme.Navy);
                _text = UiFactory.Text(_panel, "", _textMm);
                UiFactory.Stretch(_text.rectTransform);
                _text.margin = new Vector4(6, 6, 6, 6);
            }
            string reason = _action.DisabledReason;
            _text.text = _action.Label + (string.IsNullOrEmpty(reason) ? "" : "\n" + reason);
            float width = _textMm * 30f;
            float height = _text.GetPreferredValues(_text.text, width - 12, Mathf.Infinity).y + 12;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.sizeDelta = new Vector2(width, height);
            var button = (RectTransform)transform;
            _panel.position = button.TransformPoint(new Vector3(0, -button.rect.height / 2f - 3f, 0));
            _panel.SetAsLastSibling();
            _panel.gameObject.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData) => Hide();
        private void OnDisable() => Hide();
        private void Hide() { if (_panel != null) _panel.gameObject.SetActive(false); }

        private void OnDestroy()
        {
            if (_panel == null) return;
            if (Application.isPlaying) Destroy(_panel.gameObject);
            else DestroyImmediate(_panel.gameObject);
        }
    }
}
