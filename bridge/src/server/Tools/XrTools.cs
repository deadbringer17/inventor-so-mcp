using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// XR / visualization surface (plan §5-6). Geometry never travels inside these responses: meshes
/// become content-addressed GLB assets (resource <c>inventor://assets/{id}</c>, and
/// <c>GET /assets/{id}</c> on the HTTP host) and responses carry ids, transforms and revisions.
/// View-state tools (highlight, focus, camera) change only the Inventor window and never a
/// document revision.
/// </summary>
[McpServerToolType]
public sealed class XrTools
{
    [McpServerTool(Name = "inventor_get_assembly_context_xr"), Description("Read revision-bound active assembly occurrences and native DOF axes. Optional occurrence_id selects a direct occurrence and includes face/edge proxy references and geometry in assembly millimetres. No CAD changes. Experimental.")]
    public Task<string> AssemblyContext(string document_id, string expected_revision, string? occurrence_id = null, CancellationToken ct = default)
        => Call("get_assembly_context_xr", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["occurrence_id"] = occurrence_id }, ct);
    [McpServerTool(Name = "inventor_get_design_context_xr"), Description("Read revision-bound Design references for the active part: work-plane frames, sketch frames, planar face IDs, model edge IDs with polylines in millimetres and parameters. Bounded; reports truncation and unavailable strokes. No CAD changes. Experimental.")]
    public Task<string> DesignContext(string document_id, string expected_revision, CancellationToken ct = default)
        => Call("get_design_context_xr", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision }, ct);

    [McpServerTool(Name = "inventor_inspect_xr"), Description("Read mass (kg), material, volume (mm3), area (mm2), constraint count and remaining DOF of the active document or one occurrence/proxy. Requires document_id and expected_revision. Unavailable values are null. Does not activate documents or change CAD. Experimental.")]
    public Task<string> Inspect(string document_id, string expected_revision, string? occurrence_id = null, CancellationToken ct = default)
        => Call("inspect_xr", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["occurrence_id"] = occurrence_id }, ct);

    [McpServerTool(Name = "inventor_activate_open_document_xr"), Description("Activate exactly one already-open part or assembly by document_id for XR inspection. View/context only: no open-file, save, close, rebuild or CAD edit. Refuses an active transaction and duplicate ids. Experimental.")]
    public Task<string> ActivateOpen(string document_id, CancellationToken ct = default)
        => Call("activate_open_document_xr", new JObject { ["document_id"] = document_id }, ct);

    [McpServerTool(Name = "inventor_check_interference_xr"), Description("Revision-bound Inventor interference analysis of the active assembly's direct occurrences for XR. Without occurrence_ids every unsuppressed direct occurrence is analysed; with occurrence_ids (portable ids of direct occurrences) those are analysed against all the others. Returns analyzed, count, total_volume_mm3, elapsed_ms and pairs with portable a/b occurrence ids, names, volume_mm3 and the range box (min_mm/max_mm, assembly millimetres) of each interference body. A subassembly counts as one unit. No CAD changes. Experimental.")]
    public Task<string> CheckInterferenceXr(string document_id, string expected_revision, string[]? occurrence_ids = null, CancellationToken ct = default)
        => Call("check_interference_xr", new JObject
        {
            ["document_id"] = document_id, ["expected_revision"] = expected_revision,
            ["occurrence_ids"] = occurrence_ids is null ? null : new JArray(occurrence_ids),
        }, ct);

    [McpServerTool(Name = "inventor_measure_min_distance_xr"), Description("Revision-bound Inventor minimum distance (mm) between two direct occurrences of the active assembly (portable ids) for XR. Returns distance_mm and, when Inventor provides them, the closest points point_a/point_b in assembly millimetres (points_source inventor), otherwise null points (points_source unavailable). Expect 0 on touching or interfering parts. No CAD changes. Experimental.")]
    public Task<string> MeasureMinDistanceXr(string document_id, string expected_revision, string a_occurrence_id, string b_occurrence_id, CancellationToken ct = default)
        => Call("measure_min_distance_xr", new JObject
        {
            ["document_id"] = document_id, ["expected_revision"] = expected_revision,
            ["a_occurrence_id"] = a_occurrence_id, ["b_occurrence_id"] = b_occurrence_id,
        }, ct);

    /// <summary>A composed scene fetches at most this many distinct definitions.</summary>
    public const int MaxSceneDefinitions = 200;

    private readonly PluginClient _client;
    private readonly AssetStore _assets;
    private readonly InventorMcpConfig _config;
    private readonly ICallerIdentity _caller;
    private readonly MeshCache _meshes;

    public XrTools(PluginClient client, AssetStore assets, InventorMcpConfig config, ICallerIdentity caller, MeshCache meshes)
    {
        _client = client; _assets = assets; _config = config; _caller = caller; _meshes = meshes;
    }

    [McpServerTool(Name = "inventor_get_display_mesh"), Description("Tessellate one open part (or every solid body of it) and publish it as a GLB asset: metres, Y-up as in Inventor, one mesh primitive per solid body, and primitive.extras.faces mapping each face to its index range and portable face id, so a client resolves a picked triangle to a face without a round trip. Returns asset_id (a_ + SHA-256 of the bytes: unchanged geometry keeps its id), resource_uri inventor://assets/{id}, asset_url when the HTTP host runs, revision and visual_revision, and per-body counts. document_id defaults to the active document; any open part may be named, including one referenced by an assembly. tolerance_mm (0.01-5, default 0.1) is the chord tolerance; max_triangles (default 500000) refuses larger meshes with MESH_TOO_LARGE instead of truncating. Read-only; experimental tier.")]
    public async Task<string> GetDisplayMesh(string? document_id = null, double tolerance_mm = 0.1, int max_triangles = 500_000,
        bool include_face_ids = true, CancellationToken ct = default)
    {
        try
        {
            if (!(tolerance_mm >= 0.01 && tolerance_mm <= 5)) return Error(InventorErrorCodes.INVALID_ARGUMENT, "tolerance_mm must be between 0.01 and 5.");
            if (max_triangles < 1 || max_triangles > 5_000_000) return Error(InventorErrorCodes.INVALID_ARGUMENT, "max_triangles must be between 1 and 5000000.");
            var (data, source) = await FetchMesh(document_id, tolerance_mm, max_triangles, include_face_ids, ct);
            var bytes = GlbBuilder.BuildDefinition(source);
            var record = _assets.Put(bytes, "model/gltf-binary", _caller.Client, new JObject
            {
                ["kind"] = "definition_mesh",
                ["document_id"] = data["document_id"]?.DeepClone(),
                ["visual_revision"] = data["visual_revision"]?.DeepClone(),
            });
            return new JObject
            {
                ["document_id"] = data["document_id"]?.DeepClone(),
                ["definition_name"] = data["definition_name"]?.DeepClone(),
                ["revision"] = data["revision"]?.DeepClone(),
                ["visual_revision"] = data["visual_revision"]?.DeepClone(),
                ["units"] = "m",
                ["tolerance_mm"] = tolerance_mm,
                ["face_ids_complete"] = data["face_ids_complete"]?.DeepClone(),
                ["bodies"] = new JArray(source.Bodies.Select(b => new JObject
                {
                    ["index"] = b.Index, ["name"] = b.Name, ["visible"] = b.Visible,
                    ["vertex_count"] = b.Positions.Length / 3, ["triangle_count"] = b.Indices.Length / 3, ["face_count"] = b.Faces.Count,
                })),
                ["asset"] = record.ToJson(_config.PublicBaseUrl),
            }.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { return Error(InventorErrorCodes.API_ERROR, "Mesh payload rejected: " + ex.Message); }
    }

    [McpServerTool(Name = "inventor_get_flat_pattern_mesh"), Description("Tessellate the existing flat pattern of a sheet-metal part and publish it as its own GLB asset (metres, no face ids: a display mesh, not a reference), distinct from the folded part's mesh. Never creates the flat pattern: a part without one is refused with INVALID_ARGUMENT / details.reason FLAT_PATTERN_MISSING (run a create_flat_pattern operation through inventor_atomic_batch first), a multi-body part with MULTI_BODY_PART, a non-sheet-metal document with WRONG_DOCUMENT_TYPE. Returns asset_id and resource_uri as inventor_get_display_mesh does, the source the geometry was read from, Inventor's own flat_pattern dimensions (length_mm, width_mm, bend_count, alignment), mesh_bbox_mm, a flat_pattern_identity (document, revision, dimensions, vertex hash) usable as a cache key, and edit_state_before/after with left_edit_mode_restored (the read never leaves Inventor in flat-pattern edit). document_id defaults to the active document; tolerance_mm (0.01-5, default 0.1); max_triangles (default 500000) refuses larger meshes with MESH_TOO_LARGE. Read-only; experimental tier.")]
    public async Task<string> GetFlatPatternMesh(string? document_id = null, double tolerance_mm = 0.1, int max_triangles = 500_000, CancellationToken ct = default)
    {
        try
        {
            if (!(tolerance_mm >= 0.01 && tolerance_mm <= 5)) return Error(InventorErrorCodes.INVALID_ARGUMENT, "tolerance_mm must be between 0.01 and 5.");
            if (max_triangles < 1 || max_triangles > 5_000_000) return Error(InventorErrorCodes.INVALID_ARGUMENT, "max_triangles must be between 1 and 5000000.");
            var data = (JObject)await _client.SendAsync("get_flat_pattern_mesh", new JObject
            {
                ["document_id"] = document_id, ["tolerance_mm"] = tolerance_mm, ["max_triangles"] = max_triangles,
            }, ct);
            var bodies = (data["bodies"] as JArray ?? new JArray()).OfType<JObject>().Select(MeshPayload.FromJson).ToArray();
            var source = new GlbBuilder.MeshSource
            {
                Name = (string?)data["definition_name"] ?? "flat_pattern",
                DocumentId = (string?)data["document_id"],
                Bodies = bodies,
            };
            var bytes = GlbBuilder.BuildDefinition(source);
            var record = _assets.Put(bytes, "model/gltf-binary", _caller.Client, new JObject
            {
                ["kind"] = "flat_pattern_mesh",
                ["document_id"] = data["document_id"]?.DeepClone(),
                ["visual_revision"] = data["visual_revision"]?.DeepClone(),
                ["flat_pattern_hash"] = data["flat_pattern_identity"]?["content_hash"]?.DeepClone(),
            });
            return new JObject
            {
                ["document_id"] = data["document_id"]?.DeepClone(),
                ["definition_name"] = data["definition_name"]?.DeepClone(),
                ["revision"] = data["revision"]?.DeepClone(),
                ["visual_revision"] = data["visual_revision"]?.DeepClone(),
                ["units"] = "m",
                ["tolerance_mm"] = tolerance_mm,
                ["source"] = data["source"]?.DeepClone(),
                ["thickness_mm"] = data["thickness_mm"]?.DeepClone(),
                ["triangle_count"] = source.Bodies.Sum(b => b.Indices.Length / 3),
                ["mesh_bbox_mm"] = data["mesh_bbox_mm"]?.DeepClone(),
                ["flat_pattern"] = data["flat_pattern"]?.DeepClone(),
                ["flat_pattern_identity"] = data["flat_pattern_identity"]?.DeepClone(),
                ["edit_state_before"] = data["edit_state_before"]?.DeepClone(),
                ["edit_state_after"] = data["edit_state_after"]?.DeepClone(),
                ["left_edit_mode_restored"] = data["left_edit_mode_restored"]?.DeepClone(),
                ["bodies"] = new JArray(source.Bodies.Select(b => new JObject
                {
                    ["index"] = b.Index, ["name"] = b.Name, ["visible"] = b.Visible,
                    ["vertex_count"] = b.Positions.Length / 3, ["triangle_count"] = b.Indices.Length / 3,
                })),
                ["asset"] = record.ToJson(_config.PublicBaseUrl),
            }.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { return Error(InventorErrorCodes.API_ERROR, "Mesh payload rejected: " + ex.Message); }
    }

    [McpServerTool(Name = "inventor_get_scene_graph"), Description("Occurrence tree of the active (or named open) assembly or part: persistent occurrence_id, name, definition_document_id and kind, visible, suppressed, grounded, bounding box in mm, and each transform three ways - matrix_rowmajor_cm as Inventor reports it, matrix_mm (row-major, mm) and matrix_gltf (column-major, metres). Transforms are in top-level assembly space, so leaves can be placed directly. include_meshes=true also tessellates each distinct visible part definition once (instancing, at most 200 definitions), adds mesh_asset_id to the definitions table and publishes one composed scene GLB (scene_asset). max_nodes (default 5000) bounds the tree. Read-only; experimental tier.")]
    public async Task<string> GetSceneGraph(string? document_id = null, bool include_meshes = false, double tolerance_mm = 0.1,
        int max_nodes = 5000, CancellationToken ct = default)
    {
        try
        {
            if (max_nodes < 1 || max_nodes > 50_000) return Error(InventorErrorCodes.INVALID_ARGUMENT, "max_nodes must be between 1 and 50000.");
            var scene = (JObject)await _client.SendAsync("get_scene_graph", new JObject { ["document_id"] = document_id, ["max_nodes"] = max_nodes }, ct);
            var definitions = SceneComposer.Annotate(scene);
            if (include_meshes)
            {
                if (definitions.Count > MaxSceneDefinitions)
                    return Error(InventorErrorCodes.RESPONSE_TOO_LARGE, "The scene has " + definitions.Count + " distinct definitions (limit " +
                        MaxSceneDefinitions + "); fetch meshes per definition with inventor_get_display_mesh.");
                var meshes = new Dictionary<string, GlbBuilder.MeshSource>(StringComparer.Ordinal);
                var table = new JArray();
                foreach (var definition in definitions)
                {
                    var (data, source) = await FetchMesh(definition, tolerance_mm, 2_000_000, true, ct);
                    meshes[definition] = source;
                    var record = _assets.Put(GlbBuilder.BuildDefinition(source), "model/gltf-binary", _caller.Client, new JObject
                    {
                        ["kind"] = "definition_mesh", ["document_id"] = definition, ["visual_revision"] = data["visual_revision"]?.DeepClone(),
                    });
                    table.Add(new JObject
                    {
                        ["definition_document_id"] = definition, ["visual_revision"] = data["visual_revision"]?.DeepClone(),
                        ["mesh_asset_id"] = record.Id, ["asset"] = record.ToJson(_config.PublicBaseUrl),
                    });
                }
                scene["definitions"] = table;
                var composed = SceneComposer.Compose(scene, meshes, (string?)scene["document_id"]);
                var sceneRecord = _assets.Put(composed, "model/gltf-binary", _caller.Client, new JObject
                {
                    ["kind"] = "scene", ["document_id"] = scene["document_id"]?.DeepClone(), ["visual_revision"] = scene["visual_revision"]?.DeepClone(),
                });
                scene["scene_asset"] = sceneRecord.ToJson(_config.PublicBaseUrl);
            }
            else scene["definition_document_ids"] = new JArray(definitions);
            return scene.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException)
        { return Error(InventorErrorCodes.API_ERROR, "Scene payload rejected: " + ex.Message); }
    }

    [McpServerTool(Name = "inventor_get_visual_revision"), Description("Revision (plan token, advances on any document change, save or activation) and visual_revision (advances only when geometry or structure may have changed) of an open document, default the active one. An XR client polls or subscribes to inventor://events and re-fetches meshes only when visual_revision moved; unchanged geometry then keeps its asset_id anyway. Read-only; experimental tier.")]
    public Task<string> GetVisualRevision(string? document_id = null, CancellationToken ct = default)
        => Call("get_visual_revision", new JObject { ["document_id"] = document_id }, ct);

    [McpServerTool(Name = "inventor_highlight_entity"), Description("Highlight entities in the Inventor window (view state only: no transaction, no revision change). entity_ids are portable ids (ent_...) of faces, edges, vertices or occurrences in the active document. mode: highlight (default, keeps the user's selection), select (replaces the selection set) or clear (removes this server's highlight; entity_ids ignored). color_rgb is three 0-255 integers. Experimental tier.")]
    public Task<string> Highlight(string[]? entity_ids = null, string mode = "highlight", int[]? color_rgb = null, CancellationToken ct = default)
    {
        if (mode is not ("highlight" or "select" or "clear")) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "mode must be highlight, select or clear."));
        if (mode != "clear" && (entity_ids == null || entity_ids.Length == 0 || entity_ids.Length > 256))
            return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "entity_ids needs 1 to 256 ids."));
        if (color_rgb != null && (color_rgb.Length != 3 || color_rgb.Any(c => c < 0 || c > 255)))
            return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "color_rgb must be three integers 0-255."));
        return Call("highlight_entity", new JObject
        {
            ["entity_ids"] = entity_ids == null ? null : new JArray(entity_ids), ["mode"] = mode,
            ["color_rgb"] = color_rgb == null ? null : new JArray(color_rgb),
        }, ct);
    }

    [McpServerTool(Name = "inventor_focus_entity"), Description("Point the Inventor camera at an entity or occurrence (portable id) and fit it with margin, keeping the current view direction. View state only. Experimental tier.")]
    public Task<string> Focus(string entity_id, double margin = 1.4, CancellationToken ct = default)
    {
        if (!(margin >= 1 && margin <= 10)) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "margin must be between 1 and 10."));
        return Call("focus_entity", new JObject { ["entity_id"] = entity_id, ["margin"] = margin }, ct);
    }

    [McpServerTool(Name = "inventor_get_camera"), Description("Active view camera: eye_mm, target_mm, up (unit vector), perspective and fov_deg, plus the view's pixel size. Read-only; experimental tier.")]
    public Task<string> GetCamera(CancellationToken ct = default) => Call("get_camera", new JObject(), ct);

    [McpServerTool(Name = "inventor_set_camera"), Description("Set the active view camera: eye_mm and target_mm ([x,y,z] in mm, distinct), up (non-zero, not parallel to the view direction), optional perspective and fov_deg (1-120), fit=true to fit the model after orienting. View state only: no transaction, no revision change. Experimental tier.")]
    public Task<string> SetCamera(double[] eye_mm, double[] target_mm, double[] up, bool? perspective = null, double? fov_deg = null,
        bool fit = false, CancellationToken ct = default)
    {
        if (!Vec(eye_mm) || !Vec(target_mm) || !Vec(up)) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "eye_mm, target_mm and up must be three finite numbers each."));
        var view = new[] { target_mm[0] - eye_mm[0], target_mm[1] - eye_mm[1], target_mm[2] - eye_mm[2] };
        if (Length(view) < 1e-6) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "eye_mm and target_mm must differ."));
        if (Length(up) < 1e-9) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "up must be non-zero."));
        var cross = new[] { view[1] * up[2] - view[2] * up[1], view[2] * up[0] - view[0] * up[2], view[0] * up[1] - view[1] * up[0] };
        if (Length(cross) < 1e-6 * Length(view) * Length(up)) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "up must not be parallel to the view direction."));
        if (fov_deg is { } fov && !(fov >= 1 && fov <= 120)) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "fov_deg must be between 1 and 120."));
        return Call("set_camera", new JObject
        {
            ["eye_mm"] = new JArray(eye_mm), ["target_mm"] = new JArray(target_mm), ["up"] = new JArray(up),
            ["perspective"] = perspective, ["fov_deg"] = fov_deg, ["fit"] = fit,
        }, ct);
    }

    [McpServerTool(Name = "inventor_raycast_entity"), Description("Cast a ray in model space of the active part or assembly (origin_mm, direction) and return the first face hit as a portable id, its occurrence id in an assembly, and the hit point in mm. Fallback for clients without the mesh; with a GLB, map triangle to face locally via primitive.extras.faces. Read-only; experimental tier.")]
    public Task<string> Raycast(double[] origin_mm, double[] direction, double radius_mm = 0.5, CancellationToken ct = default)
    {
        if (!Vec(origin_mm) || !Vec(direction) || Length(direction) < 1e-12)
            return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "origin_mm and a non-zero direction must be three finite numbers each."));
        if (!(radius_mm > 0 && radius_mm <= 100)) return Task.FromResult(Error(InventorErrorCodes.INVALID_ARGUMENT, "radius_mm must be in (0, 100]."));
        return Call("raycast_entity", new JObject { ["origin_mm"] = new JArray(origin_mm), ["direction"] = new JArray(direction), ["radius_mm"] = radius_mm }, ct);
    }

    [McpServerTool(Name = "inventor_pick_entity"), Description("Turn an XR pick into an assembly entity: occurrence_id (from the scene graph) plus face_id (a part-context face id from that definition's GLB extras) becomes the face proxy's portable id in the active assembly, usable by constraint and highlight tools. Stale or deleted faces are reported, never guessed. Read-only; experimental tier.")]
    public Task<string> Pick(string occurrence_id, string face_id, CancellationToken ct = default)
        => Call("pick_entity", new JObject { ["occurrence_id"] = occurrence_id, ["face_id"] = face_id }, ct);

    private async Task<(JObject data, GlbBuilder.MeshSource source)> FetchMesh(string? documentId, double toleranceMm, int maxTriangles,
        bool includeFaceIds, CancellationToken ct)
    {
        // Unchanged geometry must keep its asset id: reuse the tessellation of this visual revision.
        var revision = (JObject)await _client.SendAsync("get_visual_revision", new JObject { ["document_id"] = documentId }, ct);
        if ((string?)revision["document_id"] is { } revisionDocument && (string?)revision["visual_revision"] is { } visual
            && _meshes.TryGet(MeshCache.Key(revisionDocument, visual, toleranceMm, includeFaceIds), out var cached, out var cachedSource)
            && cachedSource.Bodies.Sum(b => b.Indices.Length / 3) <= maxTriangles)
        {
            cached["revision"] = revision["revision"]?.DeepClone();
            return (cached, cachedSource);
        }

        var data = (JObject)await _client.SendAsync("get_display_mesh", new JObject
        {
            ["document_id"] = documentId, ["tolerance_mm"] = toleranceMm, ["max_triangles"] = maxTriangles, ["include_face_ids"] = includeFaceIds,
        }, ct);
        var bodies = (data["bodies"] as JArray ?? new JArray()).OfType<JObject>().Select(MeshPayload.FromJson).ToArray();
        var source = new GlbBuilder.MeshSource
        {
            Name = (string?)data["definition_name"] ?? "definition",
            DocumentId = (string?)data["document_id"],
            Bodies = bodies,
        };
        if ((string?)data["document_id"] is { } fetchedDocument && (string?)data["visual_revision"] is { } fetchedVisual)
            _meshes.Put(MeshCache.Key(fetchedDocument, fetchedVisual, toleranceMm, includeFaceIds), data, source);
        return (data, source);
    }

    private async Task<string> Call(string command, JObject arguments, CancellationToken ct)
    {
        try { return (await _client.SendAsync(command, arguments, ct)).ToString(Formatting.None); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }

    internal static string Error(string code, string message)
        => new JObject { ["ok"] = false, ["error"] = new JObject { ["code"] = code, ["message"] = message } }.ToString(Formatting.None);

    private static bool Vec(double[]? v) => v != null && v.Length == 3 && v.All(x => !double.IsNaN(x) && !double.IsInfinity(x));
    private static double Length(double[] v) => Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
}
