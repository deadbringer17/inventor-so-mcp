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
        catch (InventorGatewayException ex) { return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }); }
    }
    [McpServerTool(Name = "inventor_create_constraint_safe"), Description("Create planar mate or flush between persistent face_proxy IDs from selection in an active assembly. Only different, direct, unsuppressed, nonadaptive part occurrences. Offset in mm. Requires document_id/revision; owned transaction with preview=true by default. All unsuppressed top-level pairs must pass interference and minimum_clearance_mm checks; no contact exemptions or swept-path validation. Preview returns no persistent constraint ID because the temporary constraint is removed.")]
    public async Task<string> CreateConstraint(string document_id, string expected_revision, string type,
        string face_a_id, string face_b_id, double offset_mm, double minimum_clearance_mm, bool preview = true, CancellationToken ct = default)
    {
        try { return (await _client.SendAsync("create_constraint_safe", new JObject
        { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["type"] = type,
          ["face_a_id"] = face_a_id, ["face_b_id"] = face_b_id, ["offset_mm"] = offset_mm,
          ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview }, ct)).ToString(); }
        catch (InventorGatewayException ex) { return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }); }
    }
    [McpServerTool(Name = "inventor_edit_constraint_safe"), Description("Edit a persistent assembly constraint_id obtained from inventor_list_constraints. Supports mate/flush/insert offsets in mm and angle in deg. Requires document_id and expected_revision. Owned transaction with preview rollback by default; validates rebuild, constraint health, interference and minimum_clearance_mm for ALL unsuppressed top-level pairs. No contact exemptions; endpoint validation only. Does not validate motion paths.")]
    public async Task<string> EditConstraint(string document_id, string expected_revision, string constraint_id,
        double value, string units, double minimum_clearance_mm, bool preview = true, CancellationToken ct = default)
    {
        try { return (await _client.SendAsync("edit_constraint_safe", new JObject
        { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["constraint_id"] = constraint_id,
          ["value"] = value, ["units"] = units, ["minimum_clearance_mm"] = minimum_clearance_mm, ["preview"] = preview }, ct)).ToString(); }
        catch (InventorGatewayException ex) { return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }); }
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
        catch (InventorGatewayException ex) { return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }); }
    }
}
