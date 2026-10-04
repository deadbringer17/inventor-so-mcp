using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Small, lazily head-following status (spec §5.5: offline visible but not invasive).</summary>
    public sealed class StatusBadge : MonoBehaviour
    {
        private static readonly Vector3 Offset = new Vector3(-0.28f, -0.22f, 0.9f);
        private Transform _head;
        private TextMeshProUGUI _status, _flash;
        private float _flashUntil;

        public static StatusBadge Create(Transform head)
        {
            var canvas = UiFactory.WorldCanvas(null, "StatusBadge", new Vector2(320, 110));
            var badge = canvas.gameObject.AddComponent<StatusBadge>();
            badge._head = head;
            var background = UiFactory.Panel(canvas.transform, "Background", UiFactory.Background);
            UiFactory.Stretch(background);
            var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            badge._status = UiFactory.Label(background, "", 22, FontStyle.Bold);
            badge._flash = UiFactory.Label(background, "", 18);
            return badge;
        }

        public void SetStatus(string text) => _status.text = text;

        public void Flash(string text)
        {
            _flash.text = text;
            _flashUntil = Time.unscaledTime + 3f;
        }

        private void LateUpdate()
        {
            if (_flash.text.Length > 0 && Time.unscaledTime > _flashUntil) _flash.text = "";
            if (_head == null) return;
            var target = _head.position + _head.rotation * Offset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }
    }
}
