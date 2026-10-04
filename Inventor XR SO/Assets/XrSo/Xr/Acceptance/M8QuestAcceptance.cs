#if XR_SO_ACCEPTANCE
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Unity.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>M8 uses the dedicated M6 fixture and its native four-workspace regression, plus local theme checks.</summary>
    internal sealed class M8QuestAcceptance : M6QuestAcceptance
    {
        internal new static readonly string[] ReflectedMembers = { "AppController._shell" };
        protected override string Milestone => "m8";
        protected override string FixtureMilestone => "m6";
        protected override int TimeoutSeconds => 840;
        protected override string CompletionNote => "SYNTHETIC input and dedicated M6 Inventor fixture only; physical M8-06/M8-08 and performance comparison remain open";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M8QuestAcceptance>("xr_m8_acceptance");

        protected override async Task Run(CancellationToken ct)
        {
            await WaitForFixture(ct);
            RequireFixture();
            var assets = UiThemeAssets.Current;
            Check(assets != null && assets.FontLicense != null && assets.RoundedPanel != null, "local theme and official license load offline");
            foreach (var font in new[] { assets.Medium, assets.Bold })
            {
                Check(font != null && font.atlasPopulationMode == AtlasPopulationMode.Static, "static primary Satoshi atlas");
                Check(UiTypography.RequiredGlyphs.All(c => font.HasCharacter(c)), "Italian and CAD glyphs in primary atlas");
            }
            foreach (var background in new[] { UiTheme.Navy, UiTheme.Surface, UiTheme.Teal, UiTheme.Disabled })
                Check(UiTheme.Contrast(UiTheme.Text, background) >= 4.5f, "opaque essential-text contrast");
            Pass("M8-01/02", "device-local fonts, license, required glyphs and opaque semantic contrast");

            // Reuses the complete native preview/apply/undo and voice/input checks, with M8 logs/screenshots.
            // Those checks retain their M6 subcase ids so coverage is auditable, including NOT COVERED cases.
            await base.Run(ct);
            Pass("M8-04/05-regression", "M6 four-workspace native regression completed with the M8 theme; see individual M6 PASS/NOT COVERED entries");

            var shell = Read<UiShell>(App, "_shell");
            Check(shell != null, "application owns the themed shell");
            shell.Refresh();
            await Task.Delay(100, ct);
            Canvas.ForceUpdateCanvases();
            CheckPalette(shell.Palette);
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            int textures = Resources.FindObjectsOfTypeAll<Texture>().Length;
            for (int i = 0; i < 100; i++) { shell.Refresh(); await Task.Delay(20, ct); }
            Canvas.ForceUpdateCanvases();
            CheckPalette(shell.Palette);
            Check(Resources.FindObjectsOfTypeAll<Material>().Length <= materials, "100 shell rebuilds do not grow material count");
            Check(Resources.FindObjectsOfTypeAll<Texture>().Length <= textures, "100 shell rebuilds do not grow texture count");
            Pass("M8-03/07-rebuild", "Satoshi cap metrics, untruncated palette and 100 rebuilds without material/texture growth; synthetic UI checks");
            await CaptureScreenshot("theme-after-regression", ct);
            NotCovered("M8-05-stale-native", "inherited M6 stale table is local; live rejection requires the separate M3/M5 stale regressions");
            NotCovered("M8-06", "Windows real-monitor DPI and QR scanning need the desktop and a person wearing Quest");
            NotCovered("M8-07-performance", "resource counts do not certify GC/frame p95, GPU time or memory delta against an equivalent device baseline");
            NotCovered("M8-08", "30 minute physical MR/Studio trial, real controller pointing, comfort and readability have not been exercised");
        }

        private static void CheckPalette(PaletteView palette)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)palette.Canvas.transform);
            foreach (var text in palette.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                text.ForceMeshUpdate();
                Check(text.font.name.StartsWith("Satoshi-"), "palette uses the local Satoshi face");
                Check(UiTypography.CapHeight(text) >= PaletteView.TextMm - 0.01f, "palette cap height remains at least 7 mm");
                Check(!text.enableAutoSizing && !text.isTextTruncated, "operational labels are not shrunk or truncated");
                Check(text.textBounds.size.x <= text.rectTransform.rect.width + 0.1f && text.textBounds.size.y <= text.rectTransform.rect.height + 0.1f,
                    "palette glyph bounds fit: " + text.text);
            }
        }
    }
}
#endif
