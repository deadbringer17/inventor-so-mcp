using TMPro;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Explicit local references; does not mutate global TMP settings.</summary>
    public sealed class UiThemeAssets : ScriptableObject
    {
        public TMP_FontAsset Medium;
        public TMP_FontAsset Bold;
        public Sprite RoundedPanel;
        public TextAsset FontLicense;
        public TextAsset FallbackFontLicense;
        private static UiThemeAssets _loaded;
        public static UiThemeAssets Current => _loaded != null ? _loaded : (_loaded = Resources.Load<UiThemeAssets>("XrSoTheme"));
    }
}
