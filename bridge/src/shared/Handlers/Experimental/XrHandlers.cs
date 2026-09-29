#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// <c>get_display_mesh</c>: per-face tessellation of one open part (plan §6). Faces are tessellated
/// one by one so every triangle run maps to exactly one face; the result is the compact
/// <see cref="MeshPayload"/> the server turns into a GLB. Read-only.
/// </summary>
public sealed class GetDisplayMeshHandler : ExperimentalHandler
{
    /// <summary>Above this many faces the per-face reference keys are skipped (ordinals remain).</summary>
    public const int MaxFaceIds = 5000;
    public override string Name => "get_display_mesh";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Document(app, (string?)p["document_id"]);
        if (doc is not PartDocument part)
            throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "Meshes are per part definition; for an assembly use the scene graph with include_meshes.");
        double toleranceMm = X.Num(p, "tolerance_mm", 0.1);
        if (toleranceMm < 0.01 || toleranceMm > 5) throw new ArgumentException("tolerance_mm must be between 0.01 and 5.");
        int maxTriangles = p["max_triangles"]?.Type == JTokenType.Integer ? (int)p["max_triangles"]! : 500_000;
        bool faceIds = X.Bool(p, "include_face_ids", true);
        double toleranceCm = UnitConvert.MmToCm(toleranceMm);

        var def = part.ComponentDefinition;
        int totalFaces = 0;
        foreach (SurfaceBody body in def.SurfaceBodies) totalFaces += body.Faces.Count;
        bool withIds = faceIds && totalFaces <= MaxFaceIds;

        var bodies = new JArray();
        var colorCache = new Dictionary<string, string?>();
        int triangles = 0;
        int bodyIndex = 0;
        foreach (SurfaceBody body in def.SurfaceBodies)
        {
            bodyIndex++;
            var positions = new List<float>();
            var normals = new List<float>();
            var indices = new List<uint>();
            var mesh = new MeshPayload.Body { Index = bodyIndex, Name = body.Name, Visible = body.Visible };
            int ordinal = 0;
            string? bodyColor = AppearanceColor(body, colorCache);
            foreach (Face face in body.Faces)
            {
                ordinal++;
                X.Deadline(ctx, "while tessellating");
                // The typed call passes null SAFEARRAYs and Inventor answers DISP_E_TYPEMISMATCH
                // (verified live on 2027); late binding with pre-sized empty arrays works.
                dynamic facetSource = face;
                int vertexCount = 0, facetCount = 0;
                double[] coordinates = new double[0], normalVectors = new double[0];
                int[] vertexIndices = new int[0];
                facetSource.CalculateFacets(toleranceCm, out vertexCount, out facetCount,
                    out coordinates, out normalVectors, out vertexIndices);
                if (facetCount <= 0 || vertexCount <= 0) continue;
                triangles += facetCount;
                if (triangles > maxTriangles)
                    throw new CodedFailureException(InventorErrorCodes.MESH_TOO_LARGE,
                        "The mesh exceeds " + maxTriangles + " triangles; raise tolerance_mm or max_triangles.",
                        new JObject { ["max_triangles"] = maxTriangles, ["tolerance_mm"] = toleranceMm });
                int indexBase = MeshPayload.DetectIndexBase(vertexIndices, vertexCount);
                uint offset = (uint)(positions.Count / 3);
                int first = indices.Count;
                for (int i = 0; i < vertexCount * 3; i++) positions.Add((float)coordinates[i]);
                bool hasNormals = normalVectors != null && normalVectors.Length >= vertexCount * 3;
                for (int i = 0; i < vertexCount * 3; i++) normals.Add(hasNormals ? (float)normalVectors![i] : 0f);
                for (int i = 0; i < facetCount * 3; i++) indices.Add(offset + (uint)(vertexIndices[i] - indexBase));
                mesh.Faces.Add(new MeshPayload.FaceRange
                {
                    FaceId = withIds ? X.Describe(doc, face) : null,
                    Ordinal = ordinal,
                    FirstIndex = first,
                    IndexCount = facetCount * 3,
                    Surface = face.SurfaceType.ToString(),
                    Color = AppearanceColor(face, colorCache) ?? bodyColor,
                });
            }
            mesh.Positions = positions.ToArray();
            mesh.Normals = normals.ToArray();
            mesh.Indices = indices.ToArray();
            bodies.Add(MeshPayload.ToJson(mesh));
        }

        string id = EntityReferences.DocumentId(doc);
        return new JObject
        {
            ["document_id"] = id,
            ["definition_name"] = doc.DisplayName,
            ["revision"] = ctx.Events?.Revision(id),
            ["visual_revision"] = ctx.Events?.VisualRevision(id),
            ["units"] = "cm",
            ["tolerance_mm"] = toleranceMm,
            ["face_ids_complete"] = withIds,
            ["face_count"] = totalFaces,
            ["triangle_count"] = triangles,
            ["bodies"] = bodies,
        };
    }

    /// <summary>
    /// Effective appearance colour ("#RRGGBB", sRGB) of a face or body, or null. Cosmetic only, so it
    /// never throws. NOT YET VERIFIED LIVE: the Appearance / Asset / "generic_diffuse" ColorAssetValue
    /// path is late bound (dynamic) because the interop signatures are unconfirmed; a wrong guess
    /// just yields null (grey) instead of failing the mesh. Cached per asset name.
    /// </summary>
    private static string? AppearanceColor(object entity, Dictionary<string, string?> cache)
    {
        try
        {
            dynamic appearance = ((dynamic)entity).Appearance;
            if (appearance == null) return null;
            string key;
            try { key = (string)appearance.InternalName; }
            catch { try { key = (string)appearance.DisplayName; } catch { key = ""; } }
            if (key.Length > 0 && cache.TryGetValue(key, out var cached)) return cached;
            string? hex = null;
            try
            {
                dynamic value = appearance.Item("generic_diffuse");
                dynamic color = value.Value;
                int r = (int)color.Red, g = (int)color.Green, b = (int)color.Blue;
                hex = "#" + Clamp(r).ToString("X2") + Clamp(g).ToString("X2") + Clamp(b).ToString("X2");
            }
            catch { hex = null; }
            if (key.Length > 0) cache[key] = hex;
            return hex;
        }
        catch { return null; }
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}

/// <summary>
/// <c>get_scene_graph</c>: the occurrence tree with transforms in top-level assembly space
/// (<c>ComponentOccurrence.Transformation</c> of a proxy is already expressed in the top-level
/// assembly), visibility, suppression and bounding boxes. Read-only.
/// </summary>
public sealed class GetSceneGraphHandler : ExperimentalHandler
{
    public override string Name => "get_scene_graph";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Document(app, (string?)p["document_id"]);
        int maxNodes = p["max_nodes"]?.Type == JTokenType.Integer ? Math.Max(1, Math.Min(50_000, (int)p["max_nodes"]!)) : 5000;
        string id = EntityReferences.DocumentId(doc);
        int count = 0;
        bool truncated = false;
        JObject root;
        if (doc is AssemblyDocument assembly)
        {
            var children = new JArray();
            foreach (ComponentOccurrence occurrence in assembly.ComponentDefinition.Occurrences)
            {
                var node = Visit(ctx, doc, occurrence, ref count, maxNodes, ref truncated);
                if (node != null) children.Add(node);
            }
            root = new JObject { ["name"] = doc.DisplayName, ["definition_document_id"] = id, ["definition_kind"] = "assembly", ["children"] = children };
        }
        else if (doc is PartDocument part)
        {
            root = new JObject
            {
                ["name"] = doc.DisplayName, ["definition_document_id"] = id, ["definition_kind"] = "part",
                ["visible"] = true, ["suppressed"] = false, ["children"] = new JArray(),
            };
            try { root["bbox"] = X.Box(part.ComponentDefinition.RangeBox); } catch { /* empty part */ }
            count = 1;
        }
        else throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Scene graphs exist for parts and assemblies.");

        return new JObject
        {
            ["document_id"] = id,
            ["kind"] = X.Kind(doc),
            ["revision"] = ctx.Events?.Revision(id),
            ["visual_revision"] = ctx.Events?.VisualRevision(id),
            ["matrix_space"] = "assembly",
            ["node_count"] = count,
            ["truncated"] = truncated,
            ["root"] = root,
        };
    }

    private static JObject? Visit(InventorCommandContext ctx, global::Inventor.Document top, ComponentOccurrence occurrence,
        ref int count, int maxNodes, ref bool truncated)
    {
        if (count >= maxNodes) { truncated = true; return null; }
        X.Deadline(ctx, "while reading the scene graph");
        count++;
        bool suppressed = occurrence.Suppressed;
        var node = new JObject
        {
            ["name"] = occurrence.Name,
            ["occurrence_id"] = X.Describe(top, occurrence),
            ["visible"] = occurrence.Visible,
            ["suppressed"] = suppressed,
            ["matrix_rowmajor_cm"] = X.RowMajor(occurrence.Transformation),
        };
        try { node["grounded"] = occurrence.Grounded; } catch { /* not every occurrence kind exposes it */ }
        var children = new JArray();
        if (!suppressed)
        {
            try
            {
                var definitionDocument = occurrence.Definition.Document as global::Inventor.Document;
                node["definition_document_id"] = definitionDocument == null ? null : EntityReferences.DocumentId(definitionDocument);
                node["definition_kind"] = occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject ? "assembly"
                    : occurrence.Definition is VirtualComponentDefinition ? "virtual" : "part";
            }
            catch { node["definition_kind"] = "unavailable"; }
            try { node["bbox"] = X.Box(occurrence.RangeBox); } catch { /* empty or unloaded */ }
            if (occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
                foreach (ComponentOccurrence child in occurrence.SubOccurrences)
                {
                    var childNode = Visit(ctx, top, child, ref count, maxNodes, ref truncated);
                    if (childNode != null) children.Add(childNode);
                }
        }
        node["children"] = children;
        return node;
    }
}

/// <summary>
/// <c>highlight_entity</c>: view state only. Highlights live in a per-document HighlightSet owned by
/// this add-in, so <c>clear</c> removes exactly what the server added and never the user's own.
/// </summary>
public sealed class HighlightEntityHandler : ExperimentalHandler
{
    private static readonly Dictionary<string, HighlightSet> Sets = new(StringComparer.Ordinal);
    public override string Name => "highlight_entity";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        string docId = EntityReferences.DocumentId(doc);
        string mode = (string?)p["mode"] ?? "highlight";
        if (Sets.TryGetValue(docId, out var existing))
        {
            try { existing.Clear(); } catch { Sets.Remove(docId); }
        }
        if (mode == "clear") return new JObject { ["mode"] = mode, ["highlighted"] = 0 };

        var ids = X.Strings(p, "entity_ids");
        var objects = ids.Select(id => X.Resolve(doc, id, out _)).ToArray();
        if (mode == "select")
        {
            doc.SelectSet.Clear();
            foreach (var entity in objects) doc.SelectSet.Select(entity);
            return new JObject { ["mode"] = mode, ["selected"] = objects.Length };
        }
        if (!Sets.TryGetValue(docId, out var set))
        {
            set = doc.CreateHighlightSet();
            Sets[docId] = set;
        }
        if (p["color_rgb"] is JArray rgb && rgb.Count == 3)
            set.Color = app.TransientObjects.CreateColor((byte)(int)rgb[0], (byte)(int)rgb[1], (byte)(int)rgb[2]);
        else
            set.Color = app.TransientObjects.CreateColor(255, 159, 26);
        foreach (var entity in objects) set.AddItem(entity);
        return new JObject { ["mode"] = mode, ["highlighted"] = objects.Length };
    }
}

internal static class CameraSupport
{
    public static View ActiveView(Application app)
        => app.ActiveView ?? throw new CodedFailureException(InventorErrorCodes.NO_DOCUMENT, "No active view.");

    public static JObject Describe(View view)
    {
        var camera = view.Camera;
        var up = camera.UpVector;
        var result = new JObject
        {
            ["eye_mm"] = X.Mm(camera.Eye),
            ["target_mm"] = X.Mm(camera.Target),
            ["up"] = new JArray(up.X, up.Y, up.Z),
            ["perspective"] = camera.Perspective,
            ["view_width_px"] = view.Width,
            ["view_height_px"] = view.Height,
        };
        try { result["fov_deg"] = camera.PerspectiveAngle * 180.0 / Math.PI; } catch { result["fov_deg"] = null; }
        return result;
    }

    /// <summary>Centre and diagonal (cm) of an entity's range box.</summary>
    public static (Point center, double size) Extent(Application app, object entity)
    {
        if (entity is Vertex vertex) return (vertex.Point, 1.0);
        Box box = entity switch
        {
            Face face => face.Evaluator.RangeBox,
            Edge edge => edge.Evaluator.RangeBox,
            ComponentOccurrence occurrence => occurrence.RangeBox,
            SurfaceBody body => body.RangeBox,
            _ => throw new ArgumentException("This entity type cannot be focused."),
        };
        var min = box.MinPoint;
        var max = box.MaxPoint;
        var center = app.TransientGeometry.CreatePoint((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        double size = Math.Max(min.DistanceTo(max), 0.1);
        return (center, size);
    }
}

public sealed class GetCameraHandler : ExperimentalHandler
{
    public override string Name => "get_camera";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p) => CameraSupport.Describe(CameraSupport.ActiveView(app));
}

/// <summary><c>set_camera</c>: view state only; the server has already validated the vectors.</summary>
public sealed class SetCameraHandler : ExperimentalHandler
{
    public override string Name => "set_camera";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var view = CameraSupport.ActiveView(app);
        var camera = view.Camera;
        var up = X.Vec3(p, "up");
        camera.Eye = X.P3Mm(app, X.Vec3(p, "eye_mm"));
        camera.Target = X.P3Mm(app, X.Vec3(p, "target_mm"));
        camera.UpVector = app.TransientGeometry.CreateUnitVector(up[0], up[1], up[2]);
        if (p["perspective"]?.Type == JTokenType.Boolean) camera.Perspective = (bool)p["perspective"]!;
        if (p["fov_deg"]?.Type is JTokenType.Float or JTokenType.Integer) camera.PerspectiveAngle = (double)p["fov_deg"]! * Math.PI / 180.0;
        if (X.Bool(p, "fit", false)) camera.Fit();
        camera.Apply();
        view.Update();
        return CameraSupport.Describe(view);
    }
}

/// <summary><c>focus_entity</c>: keep the view direction, re-target on the entity and fit it with margin.</summary>
public sealed class FocusEntityHandler : ExperimentalHandler
{
    public override string Name => "focus_entity";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        var entity = X.Resolve(doc, X.Str(p, "entity_id"), out _);
        double margin = X.Num(p, "margin", 1.4);
        var (center, size) = CameraSupport.Extent(app, entity);
        var view = CameraSupport.ActiveView(app);
        var camera = view.Camera;
        var eye = camera.Eye;
        var target = camera.Target;
        double dx = eye.X - target.X, dy = eye.Y - target.Y, dz = eye.Z - target.Z;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length < 1e-9) { dx = 0; dy = 0; dz = 1; length = 1; }
        double distance = size * margin * 1.5;
        camera.Target = center;
        camera.Eye = app.TransientGeometry.CreatePoint(center.X + dx / length * distance, center.Y + dy / length * distance, center.Z + dz / length * distance);
        try { ((dynamic)camera).SetExtents(size * margin, size * margin); } catch { /* perspective cameras ignore extents */ }
        camera.Apply();
        view.Update();
        return CameraSupport.Describe(view);
    }
}

/// <summary><c>raycast_entity</c>: first face along a model-space ray (FindUsingRay). Read-only.</summary>
public sealed class RaycastEntityHandler : ExperimentalHandler
{
    public override string Name => "raycast_entity";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        var origin = X.P3Mm(app, X.Vec3(p, "origin_mm"));
        var d = X.Vec3(p, "direction");
        var direction = app.TransientGeometry.CreateUnitVector(d[0], d[1], d[2]);
        double radius = UnitConvert.MmToCm(X.Num(p, "radius_mm", 0.5));
        // Typed calls: the late-bound FindUsingRay cannot convert the UnitVector argument.
        ObjectsEnumerator found, points;
        switch (doc)
        {
            case PartDocument part:
                part.ComponentDefinition.FindUsingRay(origin, direction, radius, out found, out points, true);
                break;
            case AssemblyDocument assembly:
                assembly.ComponentDefinition.FindUsingRay(origin, direction, radius, out found, out points, true);
                break;
            default:
                throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "Raycasts work in parts and assemblies.");
        }
        for (int i = 1; i <= found.Count; i++)
        {
            if (found[i] is not Face face) continue;
            var result = new JObject
            {
                ["hit"] = true,
                ["entity_id"] = X.Describe(doc, face),
                ["type"] = face is FaceProxy ? "face_proxy" : "face",
                ["surface"] = face.SurfaceType.ToString(),
            };
            if (i <= points.Count && points[i] is Point point) result["point_mm"] = X.Mm(point);
            if (face is FaceProxy proxy) result["occurrence_id"] = X.Describe(doc, proxy.ContainingOccurrence);
            return result;
        }
        return new JObject { ["hit"] = false };
    }
}

/// <summary>
/// <c>pick_entity</c>: (occurrence, part-context face) from an XR pick → the face proxy in the
/// active assembly. Stale ids are reported by the resolver, never guessed.
/// </summary>
public sealed class PickEntityHandler : ExperimentalHandler
{
    public override string Name => "pick_entity";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var occurrence = X.Resolve((global::Inventor.Document)assembly, X.Str(p, "occurrence_id"), out _) as ComponentOccurrence
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: occurrence_id is not an occurrence.");
        if (occurrence.Suppressed) throw new ArgumentException("The occurrence is suppressed.");
        var definition = occurrence.Definition.Document as global::Inventor.Document
            ?? throw new ArgumentException("The occurrence has no definition document.");
        var face = X.Resolve(definition, X.Str(p, "face_id"), out _) as Face
            ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: face_id is not a face.");
        occurrence.CreateGeometryProxy(face, out var raw);
        if (raw is not FaceProxy proxy) throw new InvalidOperationException("Inventor returned no face proxy.");
        return new JObject
        {
            ["entity_id"] = X.Describe((global::Inventor.Document)assembly, proxy),
            ["type"] = "face_proxy",
            ["surface"] = proxy.SurfaceType.ToString(),
            ["occurrence_name"] = occurrence.Name,
        };
    }
}

/// <summary><c>get_visual_revision</c>: plan and visual revision tokens of an open document.</summary>
public sealed class GetVisualRevisionHandler : ExperimentalHandler
{
    public override string Name => "get_visual_revision";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Document(app, (string?)p["document_id"]);
        string id = EntityReferences.DocumentId(doc);
        return new JObject
        {
            ["document_id"] = id,
            ["kind"] = X.Kind(doc),
            ["revision"] = ctx.Events?.Revision(id),
            ["visual_revision"] = ctx.Events?.VisualRevision(id),
            ["dirty"] = doc.Dirty,
        };
    }
}
#endif
