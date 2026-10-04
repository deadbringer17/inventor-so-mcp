using TMPro;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    public static class UiTypography
    {
        public const string RequiredGlyphs = "àèéìòùÀÈÉÌÒÙ°²³≈±×−←‹›…";

        public static TMP_FontAsset Font(FontStyles style)
        {
            var assets = UiThemeAssets.Current;
            var font = assets == null ? null : ((style & FontStyles.Bold) != 0 ? assets.Bold : assets.Medium);
            if (font != null) return font;
            // Keeps setup/tests usable before assets are imported; builds validate the theme assets.
            return TMP_Settings.defaultFontAsset;
        }

        public static float CapRatio(TMP_FontAsset font)
        {
            if (font == null || font.faceInfo.pointSize <= 0 || font.faceInfo.capLine <= 0)
                throw new System.InvalidOperationException("UI font must have valid cap height metrics.");
            return font.faceInfo.capLine / font.faceInfo.pointSize;
        }

        public static float CapHeight(TMP_Text text) => text.fontSize * CapRatio(text.font);
    }
}
