using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Plain uGUI built in code: no prefabs to keep in sync.</summary>
    public static class UiFactory
    {
        public static readonly Color Background = UiTheme.Navy;
        public static readonly Color Accent = UiTheme.Signal;
        public static readonly Color Key = UiTheme.Teal;

        /// <summary>World-space canvas; 1 canvas unit = 1 mm.</summary>
        public static Canvas WorldCanvas(Transform parent, string name, Vector2 sizeMm)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.referencePixelsPerUnit = 1;
            ((RectTransform)go.transform).sizeDelta = sizeMm;
            go.transform.localScale = Vector3.one * 0.001f;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3;
            scaler.referencePixelsPerUnit = 1;
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (color.a > 0 && UiThemeAssets.Current != null)
            {
                image.sprite = UiThemeAssets.Current.RoundedPanel;
                image.type = Image.Type.Sliced;
            }
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, int size, FontStyle style = FontStyle.Normal)
        {
            var fontStyle = style == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            return Text(parent, text, Mathf.Max(14, size * UiTypography.CapRatio(UiTypography.Font(fontStyle))), fontStyle);
        }

        public static Button Button(Transform parent, string text, Color color, int fontSize, Action onClick)
        {
            return TextButton(parent, text, color, Mathf.Max(14, fontSize * UiTypography.CapRatio(UiTypography.Font(FontStyles.Bold))), onClick);
        }

        /// <summary>Testo TextMeshPro; capHeightMm e l'altezza delle maiuscole (canvas: 1 unita = 1 mm).</summary>
        public static TextMeshProUGUI Text(Transform parent, string text, float capHeightMm, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = UiTypography.Font(style);
            label.fontSize = capHeightMm / UiTypography.CapRatio(label.font);
            // Bold is a real local face; do not apply synthetic emboldening a second time.
            label.fontStyle = style & ~FontStyles.Bold;
            label.color = UiTheme.Text;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = false;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }

        public static Button TextButton(Transform parent, string text, Color color, float capHeightMm, Action onClick)
        {
            var rect = Panel(parent, text, Color.white);
            var go = rect.gameObject;
            var border = go.GetComponent<Image>();
            border.raycastTarget = true;
            var fill = Panel(rect, "Fill", Color.white);
            Stretch(fill);
            fill.offsetMin = Vector2.one;
            fill.offsetMax = -Vector2.one;
            var image = fill.GetComponent<Image>();
            image.color = Color.white;
            var button = go.AddComponent<ThemedButton>();
            button.targetGraphic = image;
            button.Border = border;
            button.Primary = color == Accent;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = button.Primary ? UiTheme.Signal : UiTheme.TealHover;
            colors.pressedColor = button.Primary ? UiTheme.Paper : UiTheme.Surface;
            colors.selectedColor = color;
            colors.disabledColor = UiStyle.Disabled;
            colors.fadeDuration = UiTheme.MicroSeconds;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            var label = Text(go.transform, text, capHeightMm, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            label.margin = new Vector4(2, 0, 2, 0);
            label.color = button.Primary ? UiTheme.Ink : UiTheme.Text;
            button.Label = label;
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
