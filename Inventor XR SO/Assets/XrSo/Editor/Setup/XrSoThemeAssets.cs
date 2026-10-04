using System;
using System.IO;
using System.Linq;
using InventorXrSo.Unity.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace InventorXrSo.Editor
{
    public static class XrSoThemeAssets
    {
        private const string ResourceRoot = "Assets/XrSo/Ui/Resources";

        public static void GenerateBatch()
        {
            try { Generate(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Inventor XR SO/M8/Generate theme assets")]
        public static void Generate()
        {
            Directory.CreateDirectory(ResourceRoot);
            AssetDatabase.Refresh();
            var theme = AssetDatabase.LoadAssetAtPath<UiThemeAssets>(ResourceRoot + "/XrSoTheme.asset");
            if (theme == null) { theme = ScriptableObject.CreateInstance<UiThemeAssets>(); AssetDatabase.CreateAsset(theme, ResourceRoot + "/XrSoTheme.asset"); }
            theme.Medium = BuildFont("Medium");
            theme.Bold = BuildFont("Bold");
            theme.RoundedPanel = BuildPanel();
            theme.FontLicense = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/XrSo/Ui/Fonts/FFL.txt");
            theme.FallbackFontLicense = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt");
            if (theme.FontLicense == null) throw new InvalidOperationException("Official font license is required in the distribution.");
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../artifacts/m8-verification");
            File.WriteAllText("../artifacts/m8-verification/font-report.txt", "Official Satoshi static fonts; unchanged binaries.\n" +
                "Medium cap ratio: " + UiTypography.CapRatio(theme.Medium) + "\nBold cap ratio: " + UiTypography.CapRatio(theme.Bold) +
                "\nGlyphs: " + UiTypography.RequiredGlyphs + "\nSDF render atlases: 1024 x 1024, 60pt, SDFAA. Local dynamic fallback for extended CAD names.\n");
        }

        private static TMP_FontAsset BuildFont(string weight)
        {
            string path = ResourceRoot + "/Satoshi-" + weight + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) { AddUnicodeFallback(existing); return existing; }
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/XrSo/Ui/Fonts/Satoshi-" + weight + ".ttf");
            if (font == null) throw new InvalidOperationException("Run scripts/prepare-m8-fonts.py first.");
            var asset = TMP_FontAsset.CreateFontAsset(font, 60, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            asset.name = "Satoshi-" + weight + " SDF";
            string characters = new string(Enumerable.Range(32, 224).Select(x => (char)x).ToArray()) + UiTypography.RequiredGlyphs;
            asset.TryAddCharacters(characters, out var missing, true);
            if (UiTypography.RequiredGlyphs.Any(c => !asset.HasCharacter(c))) throw new InvalidOperationException("Required glyphs missing: " + missing);
            // Atlas is a rendering resource. The embedded official font stays byte-for-byte unchanged.
            var fallback = TMP_FontAsset.CreateFontAsset(font, 60, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            fallback.name = "Satoshi-" + weight + " CAD fallback";
            asset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(asset, path);
            SaveFontParts(asset, asset);
            AssetDatabase.AddObjectToAsset(fallback, asset);
            SaveFontParts(fallback, asset);
            AddUnicodeFallback(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void AddUnicodeFallback(TMP_FontAsset asset)
        {
            if (asset.fallbackFontAssetTable.Any(f => f != null && f.name == "CAD Unicode fallback")) return;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            if (source == null) throw new InvalidOperationException("Bundled Liberation font is required for Greek/Cyrillic CAD names.");
            var fallback = TMP_FontAsset.CreateFontAsset(source, 60, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            fallback.name = "CAD Unicode fallback";
            asset.fallbackFontAssetTable.Add(fallback);
            AssetDatabase.AddObjectToAsset(fallback, asset);
            SaveFontParts(fallback, asset);
            EditorUtility.SetDirty(asset);
        }

        private static void SaveFontParts(TMP_FontAsset font, TMP_FontAsset owner)
        {
            AssetDatabase.AddObjectToAsset(font.material, owner);
            foreach (var texture in font.atlasTextures) { texture.name = font.name + " atlas"; AssetDatabase.AddObjectToAsset(texture, owner); }
        }

        private static Sprite BuildPanel()
        {
            string path = ResourceRoot + "/RoundedPanel.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float dx = Mathf.Max(16 - (x + 0.5f), x + 0.5f - 48, 0);
                    float dy = Mathf.Max(16 - (y + 0.5f), y + 0.5f - 48, 0);
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(16.5f - Mathf.Sqrt(dx * dx + dy * dy))));
                }
                texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 4;
            importer.spriteBorder = new Vector4(16, 16, 16, 16);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
