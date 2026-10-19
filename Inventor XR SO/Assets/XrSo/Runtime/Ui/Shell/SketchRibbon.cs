using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Ui;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Catalogo di sviluppo condiviso parte/lamiera. Le capacità mancanti sono rifiutate prima dell'invocazione.</summary>
    public static class SketchRibbon
    {
        public const string LayoutTab = "_sketch.layout";
        public static IReadOnlyList<XrTab> Tabs { get; } = SketchCommandCatalog.Tabs.Concat(new[]
            { new XrTab(LayoutTab, "Tavolozza", PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.PolygonOptionsTab,"Poligono",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.SplineInputTab,"Punti spline",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.SelectionTab,"Scegli geometria",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.FilterTab,"Filtri selezione",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.ConstraintOptionsTab,"Opzioni vincoli",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.TransformOptionsTab,"Opzioni Modifica",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.OffsetTab,"Offset",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.PatternTab,"Opzioni serie",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.NativeTextTab,"Contenuto testo",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.ImportResourcesTab,"Risorse sul server",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.ParametersTab,"Parametri nativi",PaletteProfile.Compact),
              new XrTab(NativeSketchEditorUi.ParameterEditTab,"Espressione e gestione",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.ConstraintObjectsTab,"Vincoli esistenti",PaletteProfile.Compact),
              new XrTab(NativeSketchEditorUi.ConstraintVisibilityTab,"Visualizza vincoli",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.ConstraintEditTab,"Modifica vincolo",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.DimensionTab,"Quote",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.DimensionObjectsTab,"Quote esistenti",PaletteProfile.Compact),
              new XrTab(NativeSketchEditorUi.DimensionEditTab,"Modifica quota",PaletteProfile.Sketch),
              new XrTab(NativeSketchEditorUi.DimensionTextOptionsTab,"Testo nuove quote",PaletteProfile.Sketch) }).ToArray();

        public static IEnumerable<XrAction> Actions(Func<PaletteView> palette, NativeSketchEditorUi editor = null)
        {
            string unavailable() => "Tavolozza non disponibile.";
            yield return new XrAction("sketch.ribbon", "Comandi Schizzo ▸", "schizzo", () => palette() != null,
                () => palette().ShowTabGroup(SketchCommandCatalog.Groups.Select(g => SketchCommandCatalog.TabPrefix + g).ToArray(), true), unavailable);
            yield return new XrAction("sketch.palette.settings", "Regola tavolozza", "schizzo", () => palette() != null,
                () => palette().ShowTabGroup(new[] { LayoutTab }, true), unavailable);
            foreach (var size in new[] { (280, 190), (320, 220), (360, 250) })
            {
                var profile = size;
                yield return new XrAction("sketch.palette.size." + size.Item1, size.Item1 + " × " + size.Item2 + " mm", LayoutTab,
                    () => palette() != null, () => palette().SetSketchSize(profile.Item1, profile.Item2), unavailable);
            }
            yield return new XrAction("sketch.palette.pin", "Fissa al banco", LayoutTab, () => palette() != null && !palette().Pinned,
                () => palette().PinToBench(), () => "La tavolozza è già fissata.");
            yield return new XrAction("sketch.palette.hand", "Riaggancia alla mano", LayoutTab, () => palette() != null && palette().Pinned,
                () => palette().ReattachToHand(), () => "La tavolozza è già in mano.");
            foreach (var angle in new[] { 30, 45, 60 })
            {
                var tilt = angle;
                yield return new XrAction("sketch.palette.tilt." + angle, "Inclinazione " + angle + "°", LayoutTab, () => palette() != null && !palette().Pinned,
                    () => palette().SetHandPose(palette().HandOffset, UnityEngine.Quaternion.Euler(tilt, 0, 0)), () => "Riaggancia prima alla mano.");
            }
            yield return new XrAction("sketch.palette.outward", "Verso esterno", LayoutTab, () => palette() != null && !palette().Pinned,
                () => palette().SetHandPose(new UnityEngine.Vector3(-0.08f, 0.05f, 0.02f), palette().HandRotation), () => "Riaggancia prima alla mano.");
            yield return new XrAction("sketch.palette.center", "Centra sulla mano", LayoutTab, () => palette() != null && !palette().Pinned,
                () => palette().SetHandPose(new UnityEngine.Vector3(0, 0.05f, 0.02f), palette().HandRotation), () => "Riaggancia prima alla mano.");
            foreach (var family in SketchCommandCatalog.CreateFamilies)
            {
                var tab = SketchCommandCatalog.FamilyPrefix + family;
                yield return new XrAction("sketch.family." + family, family + " ▸", SketchCommandCatalog.TabPrefix + "Crea",
                    () => palette() != null, () => palette().ShowTabGroup(new[] { tab }, true), unavailable);
            }
            var native = editor?.Actions().ToArray() ?? Array.Empty<XrAction>();
            foreach (var action in SketchCommandCatalog.UnavailableActions())
                yield return native.FirstOrDefault(a=>a.Id==action.Id) ?? action;
            foreach (var action in native.Where(a=>!a.Id.StartsWith("sketch.native.",StringComparison.Ordinal))) yield return action;
        }
    }
}
