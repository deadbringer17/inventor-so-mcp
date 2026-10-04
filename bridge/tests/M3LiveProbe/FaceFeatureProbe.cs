using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// M9 gate M9-08, live level: <c>face_feature</c> against real Inventor 2027 on a temporary fixture part
/// (extrude, fillet, chamfer, hole). Read-only commands; the only thing created is the temporary part,
/// which is closed unsaved and the user's previous document is re-activated. NOT RUN in the repository
/// session that added it (needs Inventor 2027 and the add-in built with -p:SoExperimental=true, see
/// README.md); until it passes the gate stays open and the tool stays in the experimental tier.
/// Not covered by this fixture (add a case per type before promoting): revolve, rectangular and circular
/// pattern, sheet-metal flange, and the suppressed / unhealthy state (a suppressed feature owns no face to
/// pick, so that branch is only unit-tested).
/// Run: M3LiveProbe --face-feature
/// </summary>
internal static class FaceFeatureProbe
{
    internal static int Run(global::Inventor.Application app, IReadOnlyDictionary<string, IInventorCommand> commands,
        InventorCommandContext context, CadEventJournal journal)
    {
        var original = app.ActiveDocument;
        PartDocument? part = null;
        try
        {
            part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject,
                app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject), true);
            var doc = (global::Inventor.Document)part;
            string docId = "doc_" + doc.InternalName;
            var def = part.ComponentDefinition;
            var tg = app.TransientGeometry;

            // Base: cylinder r=10 mm, h=20 mm.
            var sketch = def.Sketches.Add(def.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(tg.CreatePoint2d(0, 0), 1.0);
            var extrudeDef = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
            extrudeDef.SetDistanceExtent(2.0, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            var extrude = def.Features.ExtrudeFeatures.Add(extrudeDef);

            // Fillet the top outer edge, chamfer the bottom outer edge (1 mm each).
            Edge CircleAt(double z) => def.SurfaceBodies[1].Edges.Cast<Edge>().First(e =>
                e.GeometryType == CurveTypeEnum.kCircleCurve && Math.Abs(((Circle)e.Geometry).Center.Z - z) < 1e-6
                && Math.Abs(((Circle)e.Geometry).Radius - 1.0) < 1e-6);
            var edges = app.TransientObjects.CreateEdgeCollection();
            edges.Add(CircleAt(2.0));
            var fillet = def.Features.FilletFeatures.AddSimple(edges, 0.1);
            var chamferEdges = app.TransientObjects.CreateEdgeCollection();
            chamferEdges.Add(CircleAt(0.0));
            var chamfer = def.Features.ChamferFeatures.AddUsingDistance(chamferEdges, 0.1);

            // Hole d=5 mm, depth 10 mm from the top planar face.
            var top = def.SurfaceBodies[1].Faces.Cast<Face>().First(f => f.SurfaceType == SurfaceTypeEnum.kPlaneSurface
                && Math.Abs(((Plane)f.Geometry).Normal.Z - 1) < 1e-6 && Math.Abs(((Plane)f.Geometry).RootPoint.Z - 2.0) < 1e-6);
            var holeSketch = def.Sketches.Add(top);
            var center = holeSketch.SketchPoints.Add(tg.CreatePoint2d(0, 0), false);
            var points = app.TransientObjects.CreateObjectCollection();
            points.Add(center);
            var placement = def.Features.HoleFeatures.CreateSketchPlacementDefinition(points);
            var hole = def.Features.HoleFeatures.AddDrilledByDistanceExtent(placement, "5 mm", "10 mm", PartFeatureExtentDirectionEnum.kNegativeExtentDirection);
            doc.Update();

            string[] expected = { extrude.Name, fillet.Name, chamfer.Name, hole.Name };
            var expectedTypes = new Dictionary<string, (string Type, string[] Roles)>
            {
                [extrude.Name] = ("extrude", new[] { "distance" }),
                [fillet.Name] = ("fillet", new[] { "radius" }),
                [chamfer.Name] = ("chamfer", new[] { "distance" }),
                [hole.Name] = ("hole", new[] { "diameter", "depth" }),
            };
            var features = def.Features.Cast<PartFeature>().Select(f => f.Name).ToList();
            string revisionBefore = journal.Revision(docId);

            JObject Call(string faceId)
            {
                var response = commands["face_feature"].Execute(context, new JObject
                {
                    ["document_id"] = docId, ["expected_revision"] = journal.Revision(docId), ["face_id"] = faceId,
                });
                if (!response.Ok) throw new InvalidOperationException("face_feature: " + JsonConvert.SerializeObject(response.Error));
                return (JObject)response.Data!;
            }

            var resolver = typeof(Bimwright.Ipt.Shared.Plugin.InventorCommandRegistry).Assembly
                .GetType("Bimwright.Ipt.Shared.Handlers.Core.EntityReferences", true)!;
            var describe = resolver.GetMethod("Describe", new[] { typeof(global::Inventor.Document), typeof(object) })!;
            int passed = 0;
            foreach (var featureName in expected)
            {
                Face? face = null;
                foreach (Face candidate in def.SurfaceBodies[1].Faces)
                {
                    PartFeature? owner = null;
                    try { owner = ((dynamic)candidate).CreatedByFeature as PartFeature; } catch { }
                    if (owner?.Name == featureName) { face = candidate; break; }
                }
                if (face == null) throw new InvalidOperationException("No face owned by " + featureName + " in the fixture.");
                string faceId = (string)((JObject)describe.Invoke(null, new object[] { doc, face })!)["id"]!;
                var result = Call(faceId);
                var (type, roles) = expectedTypes[featureName];
                Check((string?)result["feature"]!["name"] == featureName, featureName + " name");
                Check((string?)result["feature"]!["type"] == type, featureName + " type " + result["feature"]!["type"]);
                Check((bool)result["feature"]!["healthy"]! && !(bool)result["feature"]!["suppressed"]!, featureName + " healthy, not suppressed");
                var returnedRoles = ((JArray)result["parameters"]!).Select(p => (string)p["role"]!).Distinct().ToArray();
                foreach (var role in roles)
                    Check(returnedRoles.Contains(role), featureName + " has a " + role + " parameter (got " + string.Join(",", returnedRoles) + ")");
                foreach (var parameter in (JArray)result["parameters"]!)
                    Check((bool)parameter["editable"]! == FaceFeaturePlainValue((string?)parameter["expression"]),
                        featureName + "." + parameter["name"] + " editable matches the expression " + parameter["expression"]);
                int index = features.IndexOf(featureName);
                Check((string?)result["previous_feature"] == (index <= 0 ? null : features[index - 1]), featureName + " previous_feature");
                Console.WriteLine("PASS face_feature " + featureName + " (" + type + "): " + result.ToString(Formatting.None));
                passed++;
            }

            Check(journal.Revision(docId) == revisionBefore, "face_feature is read-only: revision unchanged");

            // Values in mm: fillet 1 mm, hole d 5 mm / depth 10 mm, extrude 20 mm.
            var extrudeFace = def.SurfaceBodies[1].Faces.Cast<Face>().First(f => OwnerName(f) == extrude.Name);
            var extrudeResult = Call((string)((JObject)describe.Invoke(null, new object[] { doc, extrudeFace })!)["id"]!);
            Check(Math.Abs((double)extrudeResult["parameters"]![0]!["value"]! - 20.0) < 1e-6, "extrude distance is 20 mm");

            // Expression-driven parameter: not editable, expression echoed.
            def.Parameters.UserParameters.AddByExpression("probe_len", "10 mm", UnitsTypeEnum.kMillimeterLengthUnits);
            ((DistanceExtent)extrude.Definition.Extent).Distance.Expression = "probe_len * 2";
            doc.Update();
            var drivenFace = def.SurfaceBodies[1].Faces.Cast<Face>().First(f => OwnerName(f) == extrude.Name);
            var driven = Call((string)((JObject)describe.Invoke(null, new object[] { doc, drivenFace })!)["id"]!);
            var drivenParameter = driven["parameters"]![0]!;
            Check(!(bool)drivenParameter["editable"]! && ((string?)drivenParameter["expression"] ?? "").Contains("probe_len"),
                "expression-driven distance is not editable and shows its expression");

            // Read-only: no revision change, no extra features.
            Check(def.Features.Count == features.Count, "feature count unchanged by face_feature");
            Console.WriteLine("PASS face_feature live probe: " + passed + " types + expression case; revision before " + revisionBefore);
            return 0;
        }
        finally
        {
            try { part?.Close(true); } catch { }
            if (original != null) try { original.Activate(); } catch { }
        }
    }

    private static string? OwnerName(Face face)
    {
        try { return (((dynamic)face).CreatedByFeature as PartFeature)?.Name; }
        catch { return null; }
    }

    private static bool FaceFeaturePlainValue(string? expression)
        => Bimwright.Ipt.Shared.Handlers.Core.FaceFeatureModel.IsSimpleExpression(expression);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
