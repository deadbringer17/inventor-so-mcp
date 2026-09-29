using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Plain uGUI built in code: no prefabs to keep in sync.</summary>
    public static class UiFactory
    {
        public static readonly Color Background = new Color(0.08f, 0.09f, 0.11f, 0.92f);
        public static readonly Color Accent = new Color(0.20f, 0.45f, 0.85f, 1f);
        public static readonly Color Key = new Color(0.22f, 0.24f, 0.28f, 1f);

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>World-space canvas; 1 canvas unit = 1 mm.</summary>
        public static Canvas WorldCanvas(Transform parent, string name, Vector2 sizeMm)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = sizeMm;
            go.transform.localScale = Vector3.one * 0.001f;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Text Label(Transform parent, string text, int size, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = Color.white;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button Button(Transform parent, string text, Color color, int fontSize, Action onClick)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = new Color(0.35f, 0.65f, 0.95f);
            colors.pressedColor = new Color(0.12f, 0.35f, 0.65f);
            colors.selectedColor = color;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            var label = Label(go.transform, text, fontSize, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            return button;
        }

        /// <summary>Rapporto tra altezza delle maiuscole e fontSize per il font SDF di default.</summary>
        public const float CapHeightRatio = 0.7f;

        /// <summary>Testo TextMeshPro; capHeightMm e l'altezza delle maiuscole (canvas: 1 unita = 1 mm).</summary>
        public static TextMeshProUGUI Text(Transform parent, string text, float capHeightMm, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = capHeightMm / CapHeightRatio;
            label.fontStyle = style;
            label.color = Color.white;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Button TextButton(Transform parent, string text, Color color, float capHeightMm, Action onClick)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = UiStyle.Hover;
            colors.pressedColor = new Color(0.12f, 0.35f, 0.65f);
            colors.selectedColor = color;
            colors.disabledColor = UiStyle.Disabled;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            var label = Text(go.transform, text, capHeightMm, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return button;
        }

        /// <summary>Svuota un contenitore: all'indietro, perche distruggere mentre si itera salta figli.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }
}
