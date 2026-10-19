using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Ui
{
    public sealed class SketchCommand
    {
        internal SketchCommand(string nativeId, string label, string group, string family, string phase, bool advanced)
        { NativeId = nativeId; Label = label; Group = group; Family = family; Phase = phase; Advanced = advanced; }
        public string NativeId { get; }
        public string Id => "sketch.native." + NativeId;
        public string Label { get; }
        public string Group { get; }
        public string Family { get; }
        public string Phase { get; }
        public bool Advanced { get; }
        public string Tab => SketchCommandCatalog.TabPrefix + Group;
        public string FamilyTab => SketchCommandCatalog.FamilyPrefix + Family;
        public string UnavailableReason => "S1 " + Phase + ": percorso XR nativo da implementare e collaudare.";
    }

    /// <summary>Baseline 2027: censimento, non dichiarazione di capacità o equivalenza.</summary>
    public static class SketchCommandCatalog
    {
        public const string TabPrefix = "_sketch.group.", FamilyPrefix = "_sketch.family.";
        public static IReadOnlyList<SketchCommand> Commands { get; } = Array.AsReadOnly(new[]
        {
            new SketchCommand("AppSketch2DWrapperCmd", "Nuovo schizzo", "Schizzo", "Schizzo", "F2", false),
            new SketchCommand("AppSketch3DWrapperCmd", "Nuovo schizzo", "Schizzo", "Schizzo", "F6", true),
            new SketchCommand("SketchLineCmd", "Linea", "Crea", "Linea", "F3", false),
            new SketchCommand("SketchMidpointLineWrapperCmd", "Linea punto medio", "Crea", "Linea", "F3", false),
            new SketchCommand("SketchCPSplineCmd", "Spline vertice di controllo", "Crea", "Linea", "F3", false),
            new SketchCommand("SketchSplineCmd", "Spline", "Crea", "Linea", "F3", false),
            new SketchCommand("EquationCurveCmd", "Curva da equazione", "Crea", "Spline", "F3", false),
            new SketchCommand("MoldBridgeCurve", "Curva ponte", "Crea", "Spline", "F6", true),
            new SketchCommand("SketchCenterPointCircleCmd", "Cerchio mediante centro e punto", "Crea", "Cerchio", "F3", false),
            new SketchCommand("SketchTangentCircleCmd", "Cerchio tangente", "Crea", "Cerchio", "F3", false),
            new SketchCommand("SketchEllipseCmd", "Ellisse", "Crea", "Ellisse", "F3", false),
            new SketchCommand("SketchThreePointArcCmd", "Arco a tre punti", "Crea", "Arco", "F3", false),
            new SketchCommand("SketchTangentArcCmd", "Arco tangente", "Crea", "Arco", "F3", false),
            new SketchCommand("SketchCenterPointArcCmd", "Arco mediante centro e punto", "Crea", "Arco", "F3", false),
            new SketchCommand("SketchTwoPointRectangleCmd", "Rettangolo mediante due punti", "Crea", "Rettangolo", "F3", false),
            new SketchCommand("SketchThreePointRectangleCmd", "Rettangolo mediante tre punti", "Crea", "Rettangolo", "F3", false),
            new SketchCommand("SketchTwoPointCenRectangleCmd", "Rettangolo centro a due punti", "Crea", "Rettangolo", "F3", false),
            new SketchCommand("SketchThreePointCenRectangleCmd", "Rettangolo centro a tre punti", "Crea", "Rettangolo", "F3", false),
            new SketchCommand("SketchCentertoCenterSlotCmd", "Asola da centro a centro", "Crea", "Asola", "F3", false),
            new SketchCommand("SketchOverallLengthSlotCmd", "Asola sull'intera lunghezza", "Crea", "Asola", "F3", false),
            new SketchCommand("SketchCenterPointSlotCmd", "Asola tramite punto centrale", "Crea", "Asola", "F3", false),
            new SketchCommand("SketchThreePointArcSlotCmd", "Asola tramite arco mediante tre punti", "Crea", "Asola", "F3", false),
            new SketchCommand("SketchCenterPointArcSlotCmd", "Asola tramite arco mediante centro e punto", "Crea", "Asola", "F3", false),
            new SketchCommand("SketchPolygonCmd", "Poligono", "Crea", "Poligono", "F3", false),
            new SketchCommand("SketchFilletCmd", "Raccordo", "Crea", "Raccordo", "F3", false),
            new SketchCommand("SketchChamferCmd", "Smusso", "Crea", "Smusso", "F3", false),
            new SketchCommand("SketchCreateTextCmd", "Crea testo", "Crea", "Testo", "F3", false),
            new SketchCommand("SketchCreateGeometryTextCmd", "Crea testo allineato alla geometria", "Crea", "Testo", "F3", false),
            new SketchCommand("SketchHoleCenterPointCmd", "Punto, Punto centrale", "Crea", "Punto", "F3", false),
            new SketchCommand("SketchProjectGeometryCmd", "Proietta geometria", "Crea", "Proietta", "F5", false),
            new SketchCommand("SketchProjectCutEdgesCmd", "Proietta spigoli di taglio", "Crea", "Proietta", "F3", false),
            new SketchCommand("SketchProjectFlatPatternCmd", "Proietta modello piatto", "Crea", "Proietta", "F5", false),
            new SketchCommand("SketchProjectTo3dSketchCmd", "Proietta verso schizzo 3D", "Crea", "Proietta", "F6", true),
            new SketchCommand("SketchProjectDwgGeomCmd", "Proietta geometria DWG", "Crea", "Proietta", "F3", false),
            new SketchCommand("SketchRectangularPatternCmd", "Serie rettangolare", "Serie", "Serie", "F5", false),
            new SketchCommand("SketchCircularPatternCmd", "Serie circolare", "Serie", "Serie", "F5", false),
            new SketchCommand("SketchMirrorCmd", "Specchio", "Serie", "Serie", "F5", false),
            new SketchCommand("SketchMoveCmd", "Sposta", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchCopyCmd", "Copia", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchRotateCmd", "Ruota", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchTrimCmd", "Taglia", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchExtendCmd", "Estendi", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchSplitCmd", "Dividi", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchScaleCmd", "Scala", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchStretchCmd", "Allunga", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchOffsetCmd", "Offset", "Modifica", "Modifica", "F5", false),
            new SketchCommand("SketchGeneralDimensionCmd", "Quota generica", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchAutoDimensionCmd", "Quota automatica", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchShowAllConstraintsCmd", "Mostra vincoli", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchConstraintSettingsCmd", "Opzioni vincolo", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchCoincidentConstraintCmd", "Coincidente", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchParallelConstraintCmd", "Parallelo", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchTangentConstraintCmd", "Tangente", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchColinearConstraintCmd", "Collineare", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchPerpendicularConstraintCmd", "Perpendicolare", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchCurvatureConstraintCmd", "Levigato", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchConcentricConstraintCmd", "Concentrico", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchHorizontalConstraintCmd", "Orizzontale", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchSymmetricConstraintCmd", "Simmetrico", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchFixConstraintCmd", "Fissaggio", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchVerticalConstraintCmd", "Verticale", "Vincola", "Vincola", "F4", false),
            new SketchCommand("SketchEqualConstraintCmd", "Uguale", "Vincola", "Vincola", "F4", false),
            new SketchCommand("PartMakePartCmd", "Crea parte", "Scheletro", "Scheletro", "F6", true),
            new SketchCommand("PartMakeComponentsCmd", "Crea componenti", "Scheletro", "Scheletro", "F6", true),
            new SketchCommand("PartCreateSketchBlockCmd", "Crea blocco", "Scheletro", "Scheletro", "F6", true),
            new SketchCommand("AppInsertImageWrapperCmd", "Inserisci immagine", "Inserisci", "Inserisci", "F6", false),
            new SketchCommand("SCxImportPointsCmd", "Importa punti...", "Inserisci", "Inserisci", "F6", false),
            new SketchCommand("SketchInsertAutoCADFileCmd", "Inserisci file di AutoCAD", "Inserisci", "Inserisci", "F6", false),
            new SketchCommand("SketchConstructionCmd", "Costruzione", "Formato", "Formato", "F4", false),
            new SketchCommand("SketchCenterlineCmd", "Linea d'asse", "Formato", "Formato", "F4", false),
            new SketchCommand("AppLinePropertiesToggleCmd", "Commutazione formattazione", "Formato", "Formato", "F4", false),
            new SketchCommand("SketchDimensionToggleCmd", "Quota non parametrica", "Formato", "Formato", "F4", false),
            new SketchCommand("SketchHoleCenterCmd", "Punto centrale", "Formato", "Formato", "F4", false),
            new SketchCommand("AppParametersCmd", "Parametri", "Parametri", "Parametri", "F4", false),
            new SketchCommand("ReturnToFeatureCmd", "Torna alla lavorazione", "Esci", "Esci", "F2", false),
            new SketchCommand("Finish2dSketchCmd", "Concludi schizzo", "Esci", "Esci", "F2", false),
            new SketchCommand("Cancel2dSketchCmd", "Annulla schizzo", "Esci", "Esci", "F2", false),
        });
        public static IReadOnlyList<string> Groups { get; } = Array.AsReadOnly(new[]
            { "Schizzo", "Crea", "Serie", "Modifica", "Vincola", "Inserisci", "Formato", "Parametri", "Esci", "Scheletro" });
        public static IReadOnlyList<string> CreateFamilies { get; } = Array.AsReadOnly(new[]
            { "Linea", "Cerchio", "Arco", "Rettangolo", "Asola", "Spline", "Ellisse", "Poligono", "Raccordo", "Smusso", "Testo", "Punto", "Proietta" });
        public static IReadOnlyList<XrTab> Tabs { get; } = Groups.Select(g => new XrTab(TabPrefix + g, g == "Esci" ? "Fine" : g, PaletteProfile.Sketch))
            .Concat(CreateFamilies.Select(f => new XrTab(FamilyPrefix + f, f, PaletteProfile.Sketch))).ToArray();

        public static IEnumerable<XrAction> UnavailableActions()
            => Commands.Select(c => new XrAction(c.Id, c.Label, c.Group == "Crea" ? c.FamilyTab : c.Tab,
                () => false, () => throw new InvalidOperationException(c.UnavailableReason), () => c.UnavailableReason));
    }
}