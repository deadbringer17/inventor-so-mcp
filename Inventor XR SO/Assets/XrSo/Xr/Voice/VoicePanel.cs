using TMPro;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Xr.Voice
{
    /// <summary>
    /// Pannello vocale world-space che segue lentamente la testa: Ascolto / Elaborazione / trascrizione /
    /// comando proposto / motivo del rifiuto / valore dettato, con Conferma e Annulla fisici (puntatore).
    /// Nascosto quando non c'e nulla da dire. Testo italiano, corpo grande per il visore.
    /// </summary>
    public sealed class VoicePanel : MonoBehaviour
    {
        private static readonly Vector3 Offset = new Vector3(0f, -0.26f, 0.85f);
        private static readonly Color ErrorColor = UiTheme.Error;
        private static readonly Color ListeningColor = UiTheme.Success;

        private Transform _head;
        private Canvas _canvas;
        private TextMeshProUGUI _title, _transcript, _command, _detail, _error, _confirmLabel;
        private GameObject _buttons;
        private VoiceCommandBridge _bridge;
        private float _noticeUntil;
        private string _notice = "";

        public Canvas Canvas => _canvas;
        public bool IsShown => _canvas != null && _canvas.enabled;
        public string TitleText => _title.text;
        public string TranscriptText => _transcript.text;
        public string CommandText => _command.text;
        public string DetailText => _detail.text;
        public string ErrorText => _error.text;
        public bool ConfirmVisible => _buttons.activeSelf;

        public static VoicePanel Create(Transform parent, Transform head)
        {
            var canvas = UiFactory.WorldCanvas(parent, "VoicePanel", new Vector2(560, 330));
            var panel = canvas.gameObject.AddComponent<VoicePanel>();
            panel._canvas = canvas;
            panel._head = head;
            panel.Build();
            canvas.enabled = false;
            return panel;
        }

        private void Build()
        {
            var background = UiFactory.Panel(transform, "Background", UiFactory.Background);
            UiFactory.Stretch(background);
            var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 18, 18);
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            _title = UiFactory.Label(background, "", 36, FontStyle.Bold);
            _transcript = UiFactory.Label(background, "", 30);
            _command = UiFactory.Label(background, "", 30, FontStyle.Bold);
            _detail = UiFactory.Label(background, "", 26);
            _error = UiFactory.Label(background, "", 26);
            _error.color = ErrorColor;

            var row = new GameObject("Buttons", typeof(RectTransform));
            row.transform.SetParent(background, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 68;
            var confirm = UiFactory.Button(row.transform, "Conferma", UiFactory.Accent, 28, () => _bridge?.ConfirmPendingAny());
            _confirmLabel = confirm.GetComponentInChildren<TextMeshProUGUI>();
            UiFactory.Button(row.transform, "Annulla", UiFactory.Key, 28, () => _bridge?.CancelPendingAny());
            _buttons = row;
            _buttons.SetActive(false);
        }

        public void Bind(VoiceCommandBridge bridge) { _bridge = bridge; }

        /// <summary>Messaggio temporaneo (es. microfono non disponibile).</summary>
        public void ShowNotice(string text, float seconds = 4f)
        {
            _notice = text ?? "";
            _noticeUntil = Time.unscaledTime + seconds;
        }

        private void Update()
        {
            if (_bridge == null) return;
            Apply(VoiceViewModel.Build(_bridge));
        }

        /// <summary>Applica il view-model ai testi. Pubblico per i test.</summary>
        public void Apply(VoiceViewModel m)
        {
            bool notice = _notice.Length > 0 && Time.unscaledTime <= _noticeUntil;
            if (!notice) _notice = "";
            bool visible = m.Visible || notice;
            _canvas.enabled = visible;
            if (!visible) return;
            _title.text = m.Title.Length > 0 ? m.Title : (notice ? "Voce non disponibile" : "");
            _title.color = m.Listening ? ListeningColor : Color.white;
            _transcript.text = m.Transcript;
            _command.text = m.Command;
            _detail.text = m.Detail;
            _error.text = m.Error.Length > 0 ? m.Error : (notice ? _notice : "");
            if (_confirmLabel != null) _confirmLabel.text = m.ConfirmLabel;
            _buttons.SetActive(m.ShowConfirm);
            SetActive(_transcript, m.Transcript);
            SetActive(_command, m.Command);
            SetActive(_detail, m.Detail);
            SetActive(_error, _error.text);
        }

        private static void SetActive(TextMeshProUGUI t, string value) { t.gameObject.SetActive(!string.IsNullOrEmpty(value)); }

        private void LateUpdate()
        {
            if (_head == null || !_canvas.enabled) return;
            var target = _head.position + _head.rotation * Offset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }
    }
}
