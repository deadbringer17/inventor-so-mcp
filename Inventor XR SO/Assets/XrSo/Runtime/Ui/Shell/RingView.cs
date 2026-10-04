using System;
using System.Collections.Generic;
using InventorXrSo.Core.Ui;
using TMPro;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Anello contestuale: al massimo 6 azioni a 172 mm da un punto del modello, rivolto verso la testa.</summary>
    public sealed class RingView : MonoBehaviour
    {
        public const float RadiusMm = 172f, TextMm = 14f;
        public static readonly Vector2 ButtonMm = new Vector2(144, 50);
        private Transform _head;

        public Canvas Canvas { get; private set; }
        public bool Visible => Canvas != null && Canvas.gameObject.activeSelf;

        public static RingView Create(Transform parent)
        {
            var canvas = UiFactory.WorldCanvas(parent, "Anello", new Vector2(2 * RadiusMm + ButtonMm.x + 8, 2 * RadiusMm + ButtonMm.y + 8));
            var view = canvas.gameObject.AddComponent<RingView>();
            view.Canvas = canvas;
            canvas.gameObject.SetActive(false);
            return view;
        }

        public void Show(Vector3 worldPoint, IReadOnlyList<XrAction> actions, Transform head)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            if (actions.Count > ActionCatalog.MaxContext)
                throw new ArgumentException($"Anello: {actions.Count} azioni, massimo {ActionCatalog.MaxContext}.");
            UiFactory.ClearChildren(Canvas.transform);
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                double angle = (90 - 360.0 * i / actions.Count) * Math.PI / 180;
                var b = UiFactory.TextButton(Canvas.transform, action.Label, action.IsOn ? UiFactory.Accent : UiFactory.Key, TextMm,
                    () => { action.TryInvoke(); Hide(); });
                var rect = (RectTransform)b.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = ButtonMm;
                rect.anchoredPosition = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * RadiusMm;
                b.interactable = action.Enabled;
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.overflowMode = TextOverflowModes.Overflow;
                t.margin = new Vector4(2, 0, 2, 0);
                t.lineSpacing = PaletteView.LineSpacing;
            }
            _head = head;
            Canvas.transform.position = worldPoint;
            Canvas.gameObject.SetActive(actions.Count > 0);
            Face();
        }

        public void Hide() { if (Canvas != null) Canvas.gameObject.SetActive(false); }

        private void LateUpdate() { if (Visible) Face(); }

        private void Face()
        {
            if (_head == null) return;
            var d = Canvas.transform.position - _head.position;
            if (d.sqrMagnitude > 1e-8f) Canvas.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }
}
