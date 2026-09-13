using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Bimwright.Ipt.Server.Tools;

[McpServerToolType]
public sealed class SafeAssemblyTools
{
    private readonly PluginClient _client;
    public SafeAssemblyTools(PluginClient client) => _client = client;
    [McpServerTool(Name = "inventor_insert_component_safe"), Description("Insert an ungrounded part occurrence into the active assembly from source_document_id of a unique already-open, saved, clean part with one model state. No file paths accepted. translation_mm is the absolute placement from identity in assembly coordinates. Requires destination document_id/revision and minimum_clearance_mm. Owned transaction; preview defaults true and returns no component_id. Checks final placement against every existing unsuppressed top-level occurrence, rolls back failures. Does not save either document.")]
    public async Task<string> InsertComponent(string document_id, string expected_revision, string source_document_id,
        double[] translation_mm, double minimum_clearance_mm, bool preview = true, CancellationToken ct = default)
    {
        try { return (await _client.SendAsync("insert_component_safe", new JObject
        { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["source_document_id"] = source_document_id,
          ["translation_mm"] = new JArray(translation_mm), ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview }, ct)).ToString(); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }
    [McpServerTool(Name = "inventor_create_constraint_safe"), Description("Create an assembly constraint between persistent proxy IDs from inventor_list_topology (kind=face or edge on an active assembly) or from inventor_get_selection. type: mate or flush join planar faces (offset_mm); mate_axis joins the axes of cylindrical or conical faces (offset_mm); insert joins two circular EDGES and centres them (offset_mm as the distance, axes_opposed default true); angle orients two faces (angle_degrees, no offset); tangent makes two faces touch (offset_mm, inside=false for outside tangency); symmetry mirrors two entities about symmetry_plane_id (no measurement); transitional keeps a face in contact along another, such as a cam follower (no measurement). The geometry kind is checked against the type rather than inferred, so a plane is never silently treated as an axis. Only different, direct, unsuppressed, nonadaptive part occurrences. Requires document_id/revision; owned transaction with preview=true by default. All unsuppressed top-level pairs must pass interference and minimum_clearance_mm checks; no contact exemptions or swept-path validation. Preview returns no persistent constraint ID because the temporary constraint is removed.")]
    public async Task<string> CreateConstraint(string document_id, string expected_revision, string type,
        string face_a_id, string face_b_id, double minimum_clearance_mm, double offset_mm = 0,
        double? angle_degrees = null, bool axes_opposed = true, bool inside = false,
        string? symmetry_plane_id = null, bool preview = true, CancellationToken ct = default)
    {
        try
        {
            var request = new JObject
            { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["type"] = type,
              ["face_a_id"] = face_a_id, ["face_b_id"] = face_b_id,
              ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview,
              ["axes_opposed"] = axes_opposed, ["inside"] = inside };
            // An angle takes degrees and no offset; every other type takes the offset.
            if (!string.IsNullOrWhiteSpace(symmetry_plane_id)) request["symmetry_plane_id"] = symmetry_plane_id;
            if (angle_degrees.HasValue) request["angle_degrees"] = angle_degrees.Value;
            else if (type != "symmetry" && type != "transitional") request["offset_mm"] = offset_mm;
            return (await _client.SendAsync("create_constraint_safe", request, ct)).ToString();
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }
    [McpServerTool(Name = "inventor_create_joint_safe"), Description("Create an assembly joint between two component origins given as portable proxy ids (face, edge or vertex) from inventor_list_topology. joint_type: rigid, rotational, slide, cylindrical, planar or ball — a joint states the degrees of freedom that remain instead of the several constraints that would add up to the same thing. Optional gap_mm, flip_origin and flip_alignment. Only different, direct, unsuppressed, nonadaptive part occurrences. Requires document_id/revision; owned transaction with preview=true by default, rebuild, constraint AND joint health, and interference plus minimum_clearance_mm across all unsuppressed top-level pairs. A joint leaves motion available by design, so these checks describe the assembled position only: motion_path_validated is always false.")]
    public async Task<string> CreateJoint(string document_id, string expected_revision, string joint_type,
        string origin_a_id, string origin_b_id, double minimum_clearance_mm, double? gap_mm = null,
        bool flip_origin = false, bool flip_alignment = false, bool preview = true, CancellationToken ct = default)
    {
        try
        {
            var request = new JObject
            { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["joint_type"] = joint_type,
              ["origin_a_id"] = origin_a_id, ["origin_b_id"] = origin_b_id,
              ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview,
              ["flip_origin"] = flip_origin, ["flip_alignment"] = flip_alignment };
            if (gap_mm.HasValue) request["gap_mm"] = gap_mm.Value;
            return (await _client.SendAsync("create_joint_safe", request, ct)).ToString();
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }

    [McpServerTool(Name = "inventor_ground_component_safe"), Description("Ground or unground one direct occurrence of the active assembly, named by a portable component id from inventor_list_topology. A grounded component is the fixed reference the rest of the assembly resolves against. Requires document_id/revision; owned transaction, rebuild and constraint/joint health check, and it reports how many components are grounded afterwards. Grounding moves nothing, so no clearance or interference claim is made. An occurrence already in the requested state reports unchanged.")]
    public async Task<string> GroundComponent(string document_id, string expected_revision, string component_id,
        bool grounded, CancellationToken ct = default)
    {
        try { return (await _client.SendAsync("ground_component_safe", new JObject
        { ["document_id"] = document_id, ["expected_revision"] = expected_revision,
          ["component_id"] = component_id, ["grounded"] = grounded }, ct)).ToString(); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }

    [McpServerTool(Name = "inventor_edit_constraint_safe"), Description("Edit a persistent assembly constraint_id obtained from inventor_list_constraints. Supports mate/flush/insert offsets in mm and angle in deg. Requires document_id and expected_revision. Owned transaction with preview rollback by default; validates rebuild, constraint health, interference and minimum_clearance_mm for ALL unsuppressed top-level pairs. No contact exemptions; endpoint validation only. Does not validate motion paths.")]
    public async Task<string> EditConstraint(string document_id, string expected_revision, string constraint_id,
        double value, string units, double minimum_clearance_mm, bool preview = true, CancellationToken ct = default)
    {
        try { return (await _client.SendAsync("edit_constraint_safe", new JObject
        { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["constraint_id"] = constraint_id,
          ["value"] = value, ["units"] = units, ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview }, ct)).ToString(); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }
    [McpServerTool(Name = "inventor_move_component_safe"), Description("Translate and optionally rotate a direct unconstrained ungrounded assembly occurrence using persistent component_id. Supply rotation_axis[3], rotation_center_mm[3], rotation_degrees together; rotation uses assembly coordinates and right-hand rule, followed by assembly-frame translation_mm. Requires document_id/revision. Checks FINAL-position interference and minimum_clearance_mm against every other unsuppressed top-level occurrence; failure rolls back. Does NOT validate the swept motion path. preview defaults true. Does not force constraints or edit joints; nested occurrences are not supported yet.")]
    public async Task<string> Move(string document_id, string expected_revision, string component_id,
        double[] translation_mm, double minimum_clearance_mm, bool preview = true,
        double[]? rotation_axis = null, double[]? rotation_center_mm = null, double? rotation_degrees = null, CancellationToken ct = default)
    {
        try { return (await _client.SendAsync("move_component_safe", new JObject
        { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["component_id"] = component_id,
          ["translation_mm"] = new JArray(translation_mm), ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview,
          ["rotation_axis"] = rotation_axis == null ? null : new JArray(rotation_axis),
          ["rotation_center_mm"] = rotation_center_mm == null ? null : new JArray(rotation_center_mm),
          ["rotation_degrees"] = rotation_degrees }, ct)).ToString(); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }
}
