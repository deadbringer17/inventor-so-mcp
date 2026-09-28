#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Revision-bound occurrence DOF and proxy geometry, with explicit incomplete-data status.</summary>
public sealed class AssemblyContextXrHandler : ExperimentalHandler
{
    public override string Name => "get_assembly_context_xr";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var doc = (global::Inventor.Document)assembly;
        string id = EntityReferences.DocumentId(doc), revision = X.Str(p, "expected_revision");
        if (X.Str(p, "document_id") != id) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id);
        if (ctx.Events == null || ctx.Events.Revision(id) != revision) throw ConcurrencyFailure.StaleRevision(revision, ctx.Events?.Revision(id));
        var occurrences = new JArray(); var references = new JArray(); bool truncated = false;
        string? selected = (string?)p["occurrence_id"];
        ComponentOccurrence? wanted = selected == null ? null : EntityReferences.ResolveOccurrence(doc, selected);
        var direct = assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().ToArray();
        if (wanted != null && !direct.Any(o => ReferenceEquals(o, wanted)))
            throw new ArgumentException("Select a direct occurrence or activate its subassembly first.");
        foreach (var occurrence in direct)
        {
            X.Deadline(ctx, "reading assembly context");
            if (occurrences.Count >= 2000) { truncated = true; break; }
            if (wanted != null && !ReferenceEquals(occurrence, wanted)) continue;
            string occurrenceId = X.Describe(doc, occurrence) ?? throw new InvalidOperationException("Occurrence reference unavailable.");
            bool suppressed = occurrence.Suppressed;
            bool adaptive = !suppressed && occurrence.Adaptive;
            bool flexible = !suppressed && occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject && occurrence.Flexible;
            bool virtualPart = !suppressed && occurrence.Definition is VirtualComponentDefinition;
            var item = new JObject { ["occurrence_id"] = occurrenceId, ["name"] = occurrence.Name,
                ["grounded"] = occurrence.Grounded, ["suppressed"] = suppressed, ["adaptive"] = adaptive,
                ["flexible"] = flexible, ["editable"] = !suppressed && !adaptive && !flexible && !virtualPart,
                ["unavailable_reason"] = suppressed ? "suppressed" : adaptive ? "adaptive" : flexible ? "flexible" : virtualPart ? "virtual" : null,
                ["matrix_rowmajor_cm"] = X.RowMajor(occurrence.Transformation),
                ["dof_translation"] = null, ["dof_rotation"] = null, ["dof_complete"] = false,
                ["translation_axes"] = new JArray(), ["rotation_axes"] = new JArray(), ["rotation_center_mm"] = null };
            if (!suppressed && !virtualPart)
            {
                item["definition_document_id"] = EntityReferences.DocumentId((global::Inventor.Document)occurrence.Definition.Document);
                item["definition_kind"] = occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject ? "assembly" : "part";
                try
                {
                    occurrence.GetDegreesOfFreedom(out int t, out ObjectsEnumerator tv, out int r, out ObjectsEnumerator rv, out Point center);
                    var translations = Axes(tv); var rotations = Axes(rv);
                    item["dof_translation"] = t; item["dof_rotation"] = r;
                    item["translation_axes"] = translations; item["rotation_axes"] = rotations;
                    item["rotation_center_mm"] = center == null ? null : PointMm(center);
                    item["dof_complete"] = t >= 0 && t <= 3 && r >= 0 && r <= 3 && translations.Count == t && rotations.Count == r && (r == 0 || center != null);
                }
                catch (Exception ex) { item["dof_error"] = ex.Message; }
                if (wanted != null && !adaptive && occurrence.DefinitionDocumentType == DocumentTypeEnum.kPartDocumentObject)
                    ReadReferences(ctx, doc, occurrence, occurrenceId, references, ref truncated);
            }
            occurrences.Add(item);
        }
        if (ctx.Events.Revision(id) != revision) throw ConcurrencyFailure.StaleRevision(revision, ctx.Events.Revision(id));
        return new JObject { ["document_id"] = id, ["revision"] = revision, ["kind"] = "assembly",
            ["occurrences"] = occurrences, ["references"] = references, ["truncated"] = truncated };
    }

    private static JArray Axes(ObjectsEnumerator values)
    {
        var result = new JArray();
        foreach (Vector vector in values)
        {
            double length = vector.Length;
            if (length <= 0 || double.IsNaN(length) || double.IsInfinity(length)) throw new ArgumentException("Invalid native DOF vector.");
            result.Add(new JArray(vector.X / length, vector.Y / length, vector.Z / length));
        }
        return result;
    }
    private static JArray PointMm(Point point) => new(point.X * 10, point.Y * 10, point.Z * 10);

    private static void ReadReferences(InventorCommandContext ctx, global::Inventor.Document doc,
        ComponentOccurrence occurrence, string occurrenceId, JArray output, ref bool truncated)
    {
        int bodyIndex = 0, points = 0;
        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        {
            bodyIndex++; int ordinal = 0;
            foreach (Face face in body.Faces)
            {
                X.Deadline(ctx, "reading assembly faces"); ordinal++;
                if (output.Count >= 5000) { truncated = true; return; }
                object raw = face;
                if (raw is not FaceProxy) occurrence.CreateGeometryProxy(face, out raw);
                var proxy = (FaceProxy)raw;
                var item = new JObject { ["id"] = X.Describe(doc, proxy), ["occurrence_id"] = occurrenceId,
                    ["kind"] = "face", ["name"] = "Faccia " + ordinal, ["geometry"] = proxy.SurfaceType.ToString(),
                    ["body_index"] = bodyIndex, ["face_ordinal"] = ordinal, ["point_mm"] = PointMm(proxy.PointOnFace) };
                if (proxy.Geometry is Plane plane)
                    item["axis"] = new JArray(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);
                else if (proxy.Geometry is Cylinder cylinder)
                { item["axis"] = new JArray(cylinder.AxisVector.X, cylinder.AxisVector.Y, cylinder.AxisVector.Z); item["origin_mm"] = PointMm(cylinder.BasePoint); }
                output.Add(item);
            }
            ordinal = 0;
            foreach (Edge edge in body.Edges)
            {
                X.Deadline(ctx, "reading assembly edges"); ordinal++;
                if (output.Count >= 5000) { truncated = true; return; }
                object raw = edge;
                if (raw is not EdgeProxy) occurrence.CreateGeometryProxy(edge, out raw);
                var proxy = (EdgeProxy)raw;
                var item = new JObject { ["id"] = X.Describe(doc, proxy), ["occurrence_id"] = occurrenceId,
                    ["kind"] = "edge", ["name"] = "Spigolo " + ordinal, ["geometry"] = proxy.GeometryType.ToString() };
                try
                {
                    var evaluator = proxy.Evaluator;
                    evaluator.GetParamExtents(out double min, out double max);
                    dynamic source = evaluator; int count = 0; double[] coordinates = new double[0];
                    source.GetStrokes(min, max, 0.01, out count, out coordinates);
                    if (count > 4096 || (points += count) > 100000) throw new ArgumentException("Edge stroke limit exceeded.");
                    var stroke = new JArray();
                    for (int i = 0; i < count; i++) stroke.Add(new JArray(coordinates[i * 3] * 10, coordinates[i * 3 + 1] * 10, coordinates[i * 3 + 2] * 10));
                    item["polyline_mm"] = stroke;
                    if (stroke.Count > 0) item["point_mm"] = stroke[0].DeepClone();
                    if (proxy.Geometry is Circle circle)
                    { item["point_mm"] = PointMm(circle.Center); item["axis"] = new JArray(circle.Normal.X, circle.Normal.Y, circle.Normal.Z); }
                }
                catch (Exception ex) { item["unavailable_reason"] = ex.Message; }
                output.Add(item);
            }
        }
    }
}
#endif
