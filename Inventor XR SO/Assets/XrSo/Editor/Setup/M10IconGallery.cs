using System;
using System.Collections.Generic;
using System.IO;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using UnityEditor;
using UnityEngine;

namespace InventorXrSo.Editor
{
    /// <summary>Actual palette/ring components with fixture actions; not a headset readability test.</summary>
    public static class M10IconGallery
    {
        private sealed class Provider : IActionProvider
        {
            public readonly List<XrTab> Pages = new List<XrTab> { new XrTab("sketch", "Schizzo"), new XrTab("feature", "Feature") };
            public IReadOnlyList<XrTab> Tabs => Pages;
            public List<XrAction> Items { get; } = new List<XrAction>();
            public IEnumerable<XrAction> Actions => Items;
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Items;
            public CommitBarState CommitBar => null;
        }
        [Serializable] private sealed class Manifest { public Entry[] icons; }
        [Serializable] private sealed class Entry { public string key; public string label; }

        public static void CaptureBatch()
        {
            try { Capture(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Inventor XR SO/M10/Capture icon gallery")]
        public static void Capture()
        {
            string output = Path.GetFullPath("../artifacts/m10-verification/gallery");
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-m10Output");
            if (index >= 0 && index + 1 < args.Length) output = args[index + 1];
            Directory.CreateDirectory(output);
            var provider = new Provider();
            var keys = new[] { "sketch", "line", "rectangle", "circle", "extrude", "hole", "fillet", "chamfer", "parameters", "undo", "redo" };
            var labels = new[] { "Crea schizzo", "Linea", "Rettangolo", "Cerchio", "Estrusione", "Foro", "Raccordo", "Smusso", "Parametri", "Annulla modifica XR", "Ripeti modifica XR" };
            for (int i = 0; i < keys.Length; i++)
            {
                bool enabled = keys[i] != "redo";
                provider.Items.Add(new XrAction("gallery." + keys[i], labels[i], i < 4 ? "sketch" : "feature",
                    () => enabled, () => { }, () => "Nessuna modifica XR da ripetere.", icon: keys[i]));
            }
            provider.Items.Add(new XrAction("gallery.numeric", "Altezza: 12,5 mm", "sketch", () => true, () => { }, kind: XrActionKind.Numeric));
            var palette = PaletteView.Create(null);
            var ring = RingView.Create(null);
            Canvas tooltipFrame = null;
            try
            {
                palette.Render(new ActionCatalog(provider));
                UiThemeGallery.Render(palette.Canvas, output, "palette-sketch");
                palette.ShowTab("feature");
                UiThemeGallery.Render(palette.Canvas, output, "palette-feature");
                var hints = palette.GetComponentsInChildren<ActionTooltip>();
                hints[hints.Length - 1].OnPointerEnter(null);
                // Frame the tooltip outside the palette without resizing its real canvas/hit targets.
                tooltipFrame = UiFactory.WorldCanvas(null, "M10 tooltip frame", new Vector2(360, 260));
                palette.transform.SetParent(tooltipFrame.transform, false);
                palette.transform.localScale = Vector3.one;
                UiThemeGallery.Render(tooltipFrame, output, "palette-tooltip");
                ring.Show(Vector3.zero, provider.Items.GetRange(4, 4), null);
                UiThemeGallery.Render(ring.Canvas, output, "ring-feature");
                // The definitive local resource set, shown using the actual palette renderer.
                // Rendering the tooltip frame scales its parent canvas: restore a root canvas for the catalogue.
                palette.transform.SetParent(null, true);
                var manifest = JsonUtility.FromJson<Manifest>(Resources.Load<TextAsset>("InventorIconsManifest").text);
                provider.Pages.Clear(); provider.Items.Clear();
                for (int i = 0; i < manifest.icons.Length; i++)
                {
                    string tab = "catalog-" + (i / 8 + 1);
                    if (i % 8 == 0) provider.Pages.Add(new XrTab(tab, "Catalogo " + (i / 8 + 1)));
                    var entry = manifest.icons[i];
                    provider.Items.Add(new XrAction("catalog." + entry.key, entry.label, tab, () => true, () => { }, icon: entry.key));
                }
                palette.Render(new ActionCatalog(provider));
                foreach (var page in provider.Pages)
                {
                    palette.ShowTab(page.Id);
                    UiThemeGallery.Render(palette.Canvas, output, "palette-" + page.Id);
                }
                File.WriteAllText(Path.Combine(output, "capture.txt"), "M10 actual Unity UI components; fixture actions; no device or physical readability evidence.\n");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette.gameObject);
                UnityEngine.Object.DestroyImmediate(ring.gameObject);
                if (tooltipFrame != null) UnityEngine.Object.DestroyImmediate(tooltipFrame.gameObject);
            }
        }
    }
}
