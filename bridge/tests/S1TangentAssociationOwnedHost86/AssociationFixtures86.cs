using System.Globalization;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private const string Parameter86 = "S1TangentLength86";
    private const string Sketch86 = "S1_Association86";
    private const double Tol86 = 1e-5;
    private static bool Persistence86Done;
    private static int AssociationReadIndex86;
    private static readonly Dictionary<bool, JObject> AssociationKeys86 = new();

    private static void Require86(bool value, string assertion)
    { if (!value) throw new HostFailure73(assertion); }

    private static void CreateAssociationFixture86(bool sheet)
    {
        Phase = sheet ? "association_fixture_sheet" : "association_fixture_part";
        GuardDocuments73();
        var app = Application!;
        string template = sheet ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
            SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
            "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}") : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
        string name = "XR_S1_Quest_Acceptance_" + (sheet ? "Sheet" : "Part") + ".ipt";
        string path = Path.GetFullPath(Path.Combine(FixtureDirectory, name));
        Require86(Path.GetDirectoryName(path) == Path.GetFullPath(FixtureDirectory) && !File.Exists(path), "New exact fixture path required.");
        var doc = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
        var record = new OwnedDocument73(doc, sheet, doc.InternalName, path);
        Documents.Add(record); PersistOwnership73(); GuardDocuments73();
        Require86((doc.ComponentDefinition is SheetMetalComponentDefinition) == sheet, "Fixture subtype differs.");
        doc.DisplayName = name;
        var def = doc.ComponentDefinition; var g = app.TransientGeometry;
        Require86(def.Sketches.Count == 0 && def.SurfaceBodies.Count == 0 && doc.ReferencedOLEFileDescriptors.Count == 0,
            "Template is not empty; do not delete template geometry to force a baseline.");
        var parameter = def.Parameters.UserParameters.AddByExpression(Parameter86, "20 mm", "mm");
        var sketch = def.Sketches.Add(def.WorkPlanes[3]); sketch.Name = Sketch86;
        Require86(sketch.SketchEntities.Count == 0 && sketch.GeometricConstraints.Count == 0 && sketch.DimensionConstraints.Count == 0,
            "New native sketch is not empty.");
        var source = sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(0, 0), g.CreatePoint2d(2, 0));
        var startGround = sketch.GeometricConstraints.AddGround((SketchEntity)source.StartSketchPoint);
        var horizontal = sketch.GeometricConstraints.AddHorizontal((SketchEntity)source, true);
        var dimension = sketch.DimensionConstraints.AddTwoPointDistance(source.StartSketchPoint, source.EndSketchPoint,
            DimensionOrientationEnum.kHorizontalDim, g.CreatePoint2d(1, -.5), false);
        dimension.Parameter.Expression = Parameter86;
        var target = sketch.SketchPoints.Add(g.CreatePoint2d(5, 1), false);
        var targetGround = sketch.GeometricConstraints.AddGround((SketchEntity)target);
        sketch.Visible = true;
        AssociationKeys86.Add(sheet, new JObject {
            ["sketch_key"] = Key73(sketch), ["source_key"] = Key73(source), ["source_start_key"] = Key73(source.StartSketchPoint),
            ["source_end_key"] = Key73(source.EndSketchPoint), ["target_key"] = Key73(target), ["dimension_key"] = Key73(dimension),
            ["parameter_key"] = Key73(parameter), ["start_ground_key"] = Key73(startGround), ["horizontal_key"] = Key73(horizontal),
            ["target_ground_key"] = Key73(targetGround), ["dimension_parameter_name"] = dimension.Parameter.Name });
        // Deliberately no Update/Rebuild/display repair/transaction oracle.
        _ = ReadAssociation86(record, 20, false);
        GuardDocuments73(); doc.SaveAs(path, false);
        Require86(doc.InternalName == record.InternalName && SamePath73(doc.FullFileName, path), "Fixture SaveAs moved identity/path.");
        record.SavedPath = path; record.DocumentId = DocumentIdComposer.Compose(doc.InternalName, doc.FullFileName, false);
        record.BaselineSha = HashFile73(path); GuardDocuments73(); PersistOwnership73();
        WritePublic73("fixture-" + (sheet ? "sheet" : "part") + "86.json", new JObject {
            ["schema"] = 1, ["run_id"] = Settings.RunId, ["keys"] = AssociationKeys86[sheet],
            ["initial"] = ReadAssociation86(record, 20, false), ["initial_rebuild_or_repair"] = false });
    }

    // Coordinator invokes this fixed read-only command only after the HTTP client
    // emits its CHECKPOINT marker (first snapshot and all analytic assertions done).
    private static void InspectAssociationCheckpoint86()
    {
        Require86(AssociationReadIndex86 < 6, "All association checkpoints are already consumed.");
        RequireIdle86();
        bool sheet = AssociationReadIndex86 >= 3; int length = new[] { 20, 25, 30 }[AssociationReadIndex86 % 3];
        var record = Documents.Single(d => d.Sheet == sheet);
        Require86(Application!.ActiveDocument != null && SameCom73(Application.ActiveDocument, record.Document),
            "Checkpoint active document differs from the exact expected fixture.");
        string name = "association-" + (sheet ? "sheet" : "part") + "-" + length + "86.json";
        Require86(!File.Exists(Path.Combine(Settings.OutputDirectory, name)), "Checkpoint evidence already exists.");
        var row = ReadAssociation86(record, length, true);
        row["schema"] = 1; row["run_id"] = Settings.RunId; row["checkpoint_index"] = AssociationReadIndex86;
        row["first_http_snapshot_precedes_host_read"] = "Coordinator ordering required; client marker emitted after first snapshot assertions.";
        WritePublic73(name, row); AssociationReadIndex86++; PersistOwnership73();
    }

    // Pure reads on the owned STA. This is an independent native assertion layer,
    // invoked only before serving HTTP or after HTTP has fully stopped.
    private static JObject ReadAssociation86(OwnedDocument73 record, double length, bool hasArc)
    {
        GuardDocuments73(); var doc = record.Document;
        string revision = doc.DatabaseRevisionId; bool dirty = doc.Dirty;
        var keys = AssociationKeys86[record.Sheet];
        var def = doc.ComponentDefinition;
        var sketch = def.Sketches.Cast<PlanarSketch>().Single(s => Key73(s) == (string)keys["sketch_key"]!);
        Require86(sketch.Name == Sketch86 && def.Sketches.Count == 1 && def.SurfaceBodies.Count == 0
            && sketch.TextBoxes.Count == 0 && sketch.SketchImages.Count == 0 && doc.ReferencedOLEFileDescriptors.Count == 0,
            "Association fixture acquired unexpected objects.");
        var source = sketch.SketchLines.Cast<SketchLine>().Single(l => Key73(l) == (string)keys["source_key"]!);
        var target = sketch.SketchPoints.Cast<SketchPoint>().Single(p => Key73(p) == (string)keys["target_key"]!);
        var dimension = sketch.DimensionConstraints.Cast<DimensionConstraint>().Single(d => Key73(d) == (string)keys["dimension_key"]!);
        var parameter = def.Parameters.UserParameters[Parameter86];
        void Point(Point2d actual, double x, double y) => Require86(Math.Abs(actual.X * 10 - x) <= Tol86
            && Math.Abs(actual.Y * 10 - y) <= Tol86, "Native association anchor differs.");
        Point(source.StartSketchPoint.Geometry, 0, 0); Point(source.EndSketchPoint.Geometry, length, 0); Point(target.Geometry, 50, 10);
        Require86(Key73(source.StartSketchPoint) == (string)keys["source_start_key"]! && Key73(source.EndSketchPoint) == (string)keys["source_end_key"]!
            && Key73(parameter) == (string)keys["parameter_key"]! && dimension.Parameter.Name == (string)keys["dimension_parameter_name"]!
            && dimension.Parameter.Expression == Parameter86 && !dimension.Driven
            && Math.Abs(Convert.ToDouble(parameter.Value, CultureInfo.InvariantCulture) * 10 - length) <= Tol86
            && Math.Abs(Convert.ToDouble(dimension.Parameter.Value, CultureInfo.InvariantCulture) * 10 - length) <= Tol86,
            "Native driving parameter/dimension or persistent source keys differ.");
        var constraints = sketch.GeometricConstraints.Cast<GeometricConstraint>().ToArray();
        foreach (string key in new[] { "start_ground_key", "horizontal_key", "target_ground_key" })
            Require86(constraints.Count(c => Key73(c) == (string)keys[key]!) == 1, "Original constraint identity was lost.");
        Require86(sketch.SketchLines.Count == 1 && sketch.SketchArcs.Count == (hasArc ? 1 : 0)
            && sketch.DimensionConstraints.Count == 1, "Fixture geometry/dimension count differs.");
        var row = new JObject { ["context"] = record.Sheet ? "sheetmetal" : "part", ["length_mm"] = length,
            ["internal_name"] = doc.InternalName, ["document_id"] = record.DocumentId, ["database_revision_id"] = revision,
            ["dirty"] = dirty, ["keys"] = keys.DeepClone(), ["source"] = Entity73((SketchEntity)source), ["target"] = Entity73((SketchEntity)target),
            ["constraints"] = new JArray(constraints.Select(Constraint83)), ["tolerance_mm"] = Tol86 };
        if (hasArc)
        {
            var arc = sketch.SketchArcs[1]; double radius = ((50 - length) * (50 - length) + 100) / 20;
            Point(arc.CenterSketchPoint.Geometry, length, radius);
            Require86(Math.Abs(arc.Radius * 10 - radius) <= Tol86, "Native arc radius differs from analytic dependency.");
            Require86(Key73(arc.StartSketchPoint) == Key73(source.EndSketchPoint) && Key73(arc.EndSketchPoint) == Key73(target),
                "Native arc must retain both shared sketch points with its forward endpoint orientation.");
            Point(arc.StartSketchPoint.Geometry, length, 0); Point(arc.EndSketchPoint.Geometry, 50, 10);
            double sweep = Math.Atan2(50 - length, radius - 10);
            Require86(sweep > 0 && sweep < Math.PI && Math.Abs(arc.Geometry.SweepAngle - sweep) <= 1e-7,
                "Native arc is not the requested CCW minor locus.");
            var tangent = constraints.OfType<TangentSketchConstraint>().Single(t =>
                new[] { Key73(t.EntityOne), Key73(t.EntityTwo) }.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(new[] { Key73(source), Key73(arc) }));
            if (keys["arc_key"] == null) { keys["arc_key"] = Key73(arc); keys["tangent_key"] = Key73(tangent); }
            Require86(Key73(arc) == (string)keys["arc_key"]! && Key73(tangent) == (string)keys["tangent_key"]!,
                "Arc/tangent persistent key changed across save/reopen.");
            row["keys"] = keys.DeepClone(); row["arc"] = Entity73((SketchEntity)arc); row["tangent"] = Constraint83((GeometricConstraint)tangent);
            row["analytic_center_mm"] = new JArray(length, radius); row["analytic_radius_mm"] = radius;
            row["native_sweep_rad"] = arc.Geometry.SweepAngle; row["expected_sweep_rad"] = sweep;
            row["shared_source_key"] = Key73(arc.StartSketchPoint); row["shared_target_key"] = Key73(arc.EndSketchPoint);
        }
        Require86(doc.DatabaseRevisionId == revision && doc.Dirty == dirty, "Native association read changed revision/Dirty.");
        GuardDocuments73(); return row;
    }
}
