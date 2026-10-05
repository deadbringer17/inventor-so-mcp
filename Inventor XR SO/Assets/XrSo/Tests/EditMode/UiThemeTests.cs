using System;
using System.Linq;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InventorXrSo.Tests
{
    public class UiThemeTests
    {
        private GameObject _root;
        [TearDown] public void Cleanup() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }

        [Test]
        public void EssentialTextAndFocusHaveEnoughContrastOnOpaqueSurfaces()
        {
            foreach (var background in new[] { UiTheme.Navy, UiTheme.Surface, UiTheme.Teal, UiTheme.TealHover, UiTheme.Disabled })
            {
                Assert.AreEqual(1, background.a, "Passthrough must not reduce contrast");
                Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.Text, background), 4.5f);
                Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.Signal, background), 3f);
            }
            Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.Ink, UiTheme.Signal), 4.5f);
            Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.SecondaryText, UiTheme.Navy), 4.5f);
            foreach (var pair in new[] { (UiTheme.Ink, UiTheme.Paper), (UiTheme.MutedInk, UiTheme.Paper) })
                Assert.GreaterOrEqual(UiTheme.Contrast(pair.Item1, pair.Item2), 4.5f);
            Assert.Less(Vector4.Distance(UiTheme.Navy, UiTheme.Navy.linear.gamma), 0.001f, "sRGB/Linear round trip");
        }

        [Test]
        public void SelectionColoursReadOnDarkAndLightBackgrounds()
        {
            // Non-text contrast (3:1) of the rim and outline over an opaque dark MR room, the studio grey and a bright wall.
            var backgrounds = new[] { Color.black, UiTheme.Navy, new Color(0.16f, 0.17f, 0.19f), UiTheme.Paper, Color.white };
            foreach (var background in backgrounds)
            {
                Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.SelectionOutline, background), 3f, "outline on " + background);
                Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.Selection, background), 3f, "fill on " + background);
            }
            // Hover is the lighter, thinner cue: visible on dark rooms, lighter than the selection, never wider.
            Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.SelectionHover, Color.black), 3f);
            Assert.GreaterOrEqual(UiTheme.Contrast(UiTheme.SelectionHover, UiTheme.Navy), 3f);
            Assert.Greater(UiTheme.Contrast(UiTheme.SelectionHover, Color.black), UiTheme.Contrast(UiTheme.SelectionOutline, Color.black));
            Assert.Less(UiTheme.SelectionHoverRimWidth, UiTheme.SelectionRimWidth);
            Assert.Greater(UiTheme.SelectionRimWidth, 0f);
            Assert.Less(UiTheme.SelectionFaceAlpha, 1f);
            Assert.Greater(UiTheme.SelectionFaceAlpha, 0.5f);
        }

        [Test]
        public void SelectionHueIsNotUsedByAnyOtherOverlay()
        {
            // Preview blue, interference red, reference/DOF cyan-yellow-green, review orange and sketch teal must not be mistaken for it.
            var others = new[]
            {
                UiTheme.Preview, UiTheme.Error, UiTheme.Success, UiTheme.Signal, new Color(0.25f, 0.55f, 1f), Color.cyan, Color.yellow, Color.green,
                new Color(0.95f, 0.2f, 0.15f), new Color(1f, 0.42f, 0.08f), new Color(0.15f, 0.95f, 0.75f), new Color(0.2f, 0.76f, 0.9f),
            };
            Color.RGBToHSV(UiTheme.Selection, out float h, out float sat, out _);
            Assert.Greater(sat, 0.5f);
            foreach (var other in others)
            {
                Color.RGBToHSV(other, out float oh, out float os, out _);
                if (os < 0.2f) continue;
                float delta = Mathf.Abs(h - oh); delta = Mathf.Min(delta, 1f - delta);
                Assert.Greater(delta, 0.06f, "hue too close to " + other);
            }
            Color.RGBToHSV(UiTheme.SelectionOutline, out float outlineHue, out _, out _);
            Assert.AreEqual(h, outlineHue, 0.05f, "outline keeps the selection hue");
        }

        [Test]
        public void OfficialFacesLoadOfflineWithAllRequiredGlyphsAndMeasuredCapHeight()
        {
            var assets = UiThemeAssets.Current;
            Assert.IsNotNull(assets);
            Assert.IsNotNull(assets.FontLicense);
            Assert.IsNotNull(assets.FallbackFontLicense);
            Assert.Greater(assets.RoundedPanel.border.x, 0, "nine-slice borders must exist on the imported sprite, not only on its importer");
            foreach (var font in new[] { assets.Medium, assets.Bold })
            {
                StringAssert.Contains("Satoshi", font.name);
                Assert.AreEqual(AtlasPopulationMode.Static, font.atlasPopulationMode);
                foreach (char c in UiTypography.RequiredGlyphs) Assert.IsTrue(font.HasCharacter(c), "missing " + c);
                Assert.IsNotEmpty(font.fallbackFontAssetTable);
                foreach (char c in "ΔαβЖąŁ") Assert.IsTrue(font.HasCharacter(c, true, true), "extended CAD-name fallback: " + c);
                Assert.Greater(UiTypography.CapRatio(font), 0.6f);
                Assert.Less(UiTypography.CapRatio(font), 0.9f);
            }
            _root = UiFactory.WorldCanvas(null, "Fonts", new Vector2(500, 100)).gameObject;
            foreach (var style in new[] { FontStyles.Normal, FontStyles.Bold })
            {
                var text = UiFactory.Text(_root.transform, "Quota ≈ 12,5 mm ± 1° · 2000 mm³", 14, style);
                Assert.AreEqual(14, UiTypography.CapHeight(text), 0.01);
                Assert.IsFalse(text.enableAutoSizing);
                Assert.IsFalse(text.raycastTarget);
            }
        }

        [Test]
        public void HomeUsesTmpAndItsVoiceMirrorStillInvokesOnlyEnabledActions()
        {
            _root = new GameObject("Home fixture");
            var home = HomePanel.Create(_root.transform);
            int clicks = 0;
            home.SetActions(("Connetti", () => clicks++), ("Offline", () => clicks++));
            var off = home.GetComponentsInChildren<Button>().Single(b => b.name == "Offline");
            off.interactable = false;
            Assert.IsEmpty(home.GetComponentsInChildren<UnityEngine.UI.Text>(true));
            Assert.IsTrue(home.VoiceActions.Any(a => a.label == "Offline" && !a.enabled));
            Assert.IsFalse(home.InvokeVoiceAction("Offline"));
            Assert.IsTrue(home.InvokeVoiceAction("Connetti"));
            Assert.AreEqual(1, clicks);
            string submitted = null;
            home.PromptText("Quota", "mm", "", s => submitted = s, () => { }, "mm", 0, 100);
            Assert.IsTrue(home.SubmitVoiceNumber(12.5));
            Assert.AreEqual("12.5", submitted);
        }

        [Test]
        public void PairingKeyboardAndEssentialTextStayInsideHomeCanvas()
        {
            _root = new GameObject("Pairing layout fixture");
            var home = HomePanel.Create(_root.transform);
            home.PromptText("Associa PC", "Inserisci indirizzo e porta.\nImpronta della fixture: " + new string('A', 64),
                "192.168.1.20:8443", _ => { }, () => { });
            Canvas.ForceUpdateCanvases();
            var root = (RectTransform)home.Canvas.transform;
            foreach (var text in home.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                text.ForceMeshUpdate();
                foreach (char character in text.text.Where(c => !char.IsWhiteSpace(c)))
                    Assert.IsTrue(text.font.HasCharacter(character, true, true), "Missing operational glyph: " + character);
                Assert.IsFalse(text.enableAutoSizing);
                Assert.LessOrEqual(text.textBounds.size.x, text.rectTransform.rect.width + 0.01f, text.text);
                Assert.LessOrEqual(text.textBounds.size.y, text.rectTransform.rect.height + 0.01f, text.text);
                var corners = new Vector3[4];
                text.rectTransform.GetWorldCorners(corners);
                foreach (var point in corners) Assert.IsTrue(root.rect.Contains((Vector2)root.InverseTransformPoint(point)), text.text);
            }
        }

        [Test]
        public void DecorativeGraphicsDoNotStealInputAndDisabledPrimaryRemainsReadable()
        {
            _root = new GameObject("Controls", typeof(EventSystem));
            var button = UiFactory.TextButton(_root.transform, "Applica", UiFactory.Accent, 14, () => { });
            foreach (var graphic in button.GetComponentsInChildren<Graphic>())
                Assert.AreEqual(graphic.gameObject == button.gameObject, graphic.raycastTarget, graphic.name);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            button.interactable = false;
            Assert.AreEqual(UiTheme.Text, label.color);
            button.interactable = true;
            Assert.AreEqual(UiTheme.Ink, label.color);
            Assert.AreEqual(Image.Type.Sliced, button.GetComponent<Image>().type);
        }
    }
}
