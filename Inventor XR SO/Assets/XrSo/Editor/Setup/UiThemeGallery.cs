using System;
using System.Collections.Generic;
using System.IO;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Editor
{
    /// <summary>Renders actual UI components with fixture data, without modifying Main.unity.</summary>
    public static class UiThemeGallery
    {
        private sealed class Provider : IActionProvider
        {
            public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("design", "Progettazione") };
            public List<XrAction> Items { get; } = new List<XrAction>();
            public IEnumerable<XrAction> Actions => Items;
            public CommitBarState CommitBar { get; } = new CommitBarState();
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Items;
        }

        public static void CaptureBatch()
        {
            try { Capture(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Inventor XR SO/M8/Capture UI gallery")]
        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-m8Output");
            var output = index >= 0 ? args[index + 1] : Path.GetFullPath("../artifacts/m8-verification/gallery");
            Directory.CreateDirectory(output);
            var provider = new Provider();
            var catalog = new ActionCatalog(provider);
            foreach (var label in new[] { "Progettazione", "Crea schizzo", "Estrusione", "Raccordo", "Smusso", "Parametri", "Lamiera", "Ispeziona" })
                provider.Items.Add(new XrAction(label, label, "design", () => true, () => { }));
            foreach (var pair in new[] { (CommitIds.Preview, "Anteprima"), (CommitIds.Apply, "Applica"),
                (CommitIds.Cancel, "Annulla"), (CommitIds.Recover, "Aggiorna documento") })
                provider.Items.Add(new XrAction(pair.Item1, pair.Item2, "_commit", () => true, () => { }, voiceInvokes: false));
            var home = HomePanel.Create(null);
            home.ShowMessage("Inventor XR SO", "PC di collaudo · Inventor 2027\nFixture M8 · Parte di prova\nConnessione pronta");
            home.SetActions(("Ispeziona", () => { }), ("Progettazione", () => { }), ("Lamiera", () => { }), ("Assieme", () => { }));
            Render(home.Canvas, output, "home");
            home.PromptText("Associa PC", "Inserisci indirizzo e porta della fixture.\nConfronta l'impronta del certificato sul PC.", "192.168.1.20:8443", _ => { }, () => { });
            Render(home.Canvas, output, "home-pairing-keyboard");
            var palette = PaletteView.Create(null);
            palette.Render(catalog);
            Render(palette.Canvas, output, "palette");
            palette.ShowKeypad(new NumericEntry("gallery", QuantityUnit.Millimeters, 12.5, 0, 1000), "Lunghezza");
            Render(palette.Canvas, output, "keypad");
            var ring = RingView.Create(null);
            ring.Show(Vector3.zero, provider.Items.GetRange(0, 6), null);
            Render(ring.Canvas, output, "ring");
            var chip = ChipView.Create(null);
            chip.Bind(new NumericEntry("gallery", QuantityUnit.Millimeters, 12.5, 0, 1000), "Quota");
            chip.Armed = true;
            Render(chip.Canvas, output, "chip");
            var bar = CommitBarView.Create(null);
            foreach (CommitBarPhase phase in Enum.GetValues(typeof(CommitBarPhase)))
            {
                if (phase == CommitBarPhase.Empty) continue;
                if (phase == CommitBarPhase.Applied) provider.CommitBar.MarkApplied(0);
                else provider.CommitBar.Update(new CommitBarInputs(phase != CommitBarPhase.Offline,
                    phase == CommitBarPhase.Uncertain, phase == CommitBarPhase.Stale,
                    phase == CommitBarPhase.Previewing, phase == CommitBarPhase.Ready,
                    phase == CommitBarPhase.Draft, phase == CommitBarPhase.Error ? "Operazione rifiutata" : ""), 10);
                bar.Render(provider.CommitBar, catalog);
                Render(bar.Canvas, output, "bar-" + phase.ToString().ToLowerInvariant());
            }
            UnityEngine.Object.DestroyImmediate(home.gameObject);
            UnityEngine.Object.DestroyImmediate(palette.gameObject);
            UnityEngine.Object.DestroyImmediate(ring.gameObject);
            UnityEngine.Object.DestroyImmediate(chip.gameObject);
            UnityEngine.Object.DestroyImmediate(bar.gameObject);
            File.WriteAllText(Path.Combine(output, "capture.txt"), "Unity " + Application.unityVersion +
                "\nActual world-space components; fixture UI; no headset or physical readability evidence.\n");
        }

        private static void Render(Canvas canvas, string output, string name)
        {
            canvas.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            canvas.transform.localScale = Vector3.one * 0.001f;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
            Canvas.ForceUpdateCanvases();
            var size = ((RectTransform)canvas.transform).rect.size;
            var cameraObject = new GameObject("M8 gallery camera");
            var camera = cameraObject.AddComponent<Camera>();
            var transforms = canvas.GetComponentsInChildren<Transform>(true);
            var layers = new int[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) { layers[i] = transforms[i].gameObject.layer; transforms[i].gameObject.layer = 30; }
            camera.cullingMask = 1 << 30;
            camera.orthographic = true;
            camera.orthographicSize = size.y * 0.00055f;
            camera.transform.position = new Vector3(0, 0, -2);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            int width = Mathf.CeilToInt(size.x * 3), height = Mathf.CeilToInt(size.y * 3);
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                for (int i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = layers[i];
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
