using System;
using System.Collections.Generic;

namespace InventorXrSo.Core.Voice
{
    /// <summary>
    /// Catalogo stabile dei command ID, condiviso dai pulsanti manuali e dal router vocale.
    /// Un comando vocale non ha una via propria: richiama lo stesso ID del pulsante e ne eredita
    /// abilitazione, modalita, selezione e validazione.
    /// </summary>
    public static class CommandIds
    {
        public const string Flange = "sheetmetal.flange";
        public const string FlatPatternCreate = "sheetmetal.flat_pattern";
        public const string SheetMetalFace = "sheetmetal.face";
        public const string SheetMetalCut = "sheetmetal.cut";
        public const string SheetMetalRule = "sheetmetal.rule";
        public const string Chamfer = "feature.chamfer";
        public const string Fillet = "feature.fillet";
        public const string Measure = "inspect.measure";
        public const string Isolate = "inspect.isolate";
        public const string CancelDraft = "draft.cancel";
        public const string Apply = "draft.apply";
        public const string Undo = "history.undo";
        public const string Redo = "history.redo";
        public const string CreateSketch = "sketch.create";

        /// <summary>Tutti gli ID, in ordine stabile.</summary>
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
        {
            Flange, FlatPatternCreate, SheetMetalFace, SheetMetalCut, SheetMetalRule,
            Chamfer, Fillet, Measure, Isolate, CancelDraft, Apply, Undo, Redo, CreateSketch,
        });

        public static bool IsKnown(string commandId)
        {
            if (commandId == null) return false;
            for (int i = 0; i < All.Count; i++) if (All[i] == commandId) return true;
            return false;
        }

        /// <summary>Comandi che la voce non esegue mai da sola: serve la pressione fisica del pulsante.</summary>
        public static bool RequiresPhysicalConfirmation(string commandId) => commandId == Apply;
    }
}
