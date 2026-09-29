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
            var bg = UiFactory.Panel(canvas.transform, "Sfondo", UiFactory.Background);
            UiFactory.Stretch(bg);
            var row = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 4, 4);
            row.spacing = 8;
            row.childControlWidth = row.childControlHeight = true;
            hud._status = UiFactory.Text(bg, "", TextMm, FontStyles.Bold);
            hud._flash = UiFactory.Text(bg, "", TextMm);
            return hud;
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
            float headYaw = _head.eulerAngles.y;
            if (!_placed || Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > FollowBeyondDeg) { _yaw = headYaw; _placed = true; }
            var direction = Quaternion.Euler(-ElevationDeg, _yaw, 0) * Vector3.forward;
            var target = _head.position + direction * Distance;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }
    }
}
