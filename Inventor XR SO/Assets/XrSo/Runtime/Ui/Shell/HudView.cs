using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>HUD di stato in periferia; segue lo yaw della testa solo oltre ±25° (spec M6).</summary>
    public sealed class HudView : MonoBehaviour
    {
        public const float TextMm = 14f, Distance = 1.2f, ElevationDeg = 15f, FollowBeyondDeg = 25f;
        private Transform _head;
        private TextMeshProUGUI _status, _flash;
        private float _flashUntil, _yaw;
        private bool _placed;

        public Canvas Canvas { get; private set; }

        public static HudView Create(Transform head)
        {
            var canvas = UiFactory.WorldCanvas(null, "HUD", new Vector2(300, 40));
            var hud = canvas.gameObject.AddComponent<HudView>();
            hud.Canvas = canvas;
            hud._head = head;
            // La canvas cresce con il contenuto: larghezza fissa, altezza dal layout (stato sopra, flash sotto).
            var outer = canvas.gameObject.AddComponent<VerticalLayoutGroup>();
            outer.childControlWidth = outer.childControlHeight = true;
            outer.childForceExpandWidth = outer.childForceExpandHeight = false;
            var fitter = canvas.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            var col = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(8, 8, 4, 4);
            col.spacing = 2;
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = col.childForceExpandHeight = false;
            hud._status = UiFactory.Text(bg, "", TextMm, FontStyles.Bold);
            hud._flash = UiFactory.Text(bg, "", TextMm);
            hud._status.overflowMode = hud._flash.overflowMode = TextOverflowModes.Overflow;
            hud._flash.gameObject.SetActive(false);
            return hud;
        }

        public void SetStatus(string text) => _status.text = text;

        public void Flash(string text)
        {
            SetFlash(text);
            _flashUntil = Time.unscaledTime + 3f;
        }

        private void SetFlash(string text)
        {
            _flash.text = text;
            _flash.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        private void LateUpdate()
        {
            if (_flash.gameObject.activeSelf && Time.unscaledTime > _flashUntil) SetFlash("");
            if (_head == null) return;
            float headYaw = _head.eulerAngles.y;
            bool first = !_placed;
            if (first || Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > FollowBeyondDeg) { _yaw = headYaw; _placed = true; }
            var direction = Quaternion.Euler(-ElevationDeg, _yaw, 0) * Vector3.forward;
            var target = _head.position + direction * Distance;
            transform.position = first ? target : Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }
    }
}
