using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

[McpServerToolType]
public sealed class SafeArtifactTools
{
    private readonly PluginClient _client;
    public SafeArtifactTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_create_drawing_safe"), Description("Create an unsaved, proportioned drawing from the active up-to-date part or assembly. By default uses the host default drawing template and chooses sheet_size (A4/A3/A2/A1/A0, default A3) and orientation (landscape/portrait). Pass template to start from a company IDW/DWG title-block template instead: a file NAME listed by inventor_list_drawing_templates, never a path. A template brings its own sheet, border and title block, so sheet_size and orientation are then refused; its manifest declares the area left free for views, and views are laid out inside that area. Sets projection='first' (ISO/UNI, default) or 'third' (ANSI). views is a comma-separated list of front, back, top, bottom, left, right and iso (default 'front,top,right,iso'); projected views require front. Omit scale for automatic ISO 5455 scaling. dimensions='auto' (default) retrieves the model's real parametric dimensions on orthographic views and lets Inventor arrange them; dimensions='none' creates views only. No dimensions or tolerances are invented, and an unquotable model is reported with zero dimensions. title_block_json is an optional JSON object of iProperty name to scalar value (for example {\"Title\":\"Flangia\",\"Commessa\":\"24-118\"}); a legacy JSON string is also accepted. Requires source document_id/revision. Checks view and dimension bounds, overlap and source state; preview=true closes only the new draft. Commit leaves it active. manufacturing_ready remains false until tolerances and drafting approval are supplied. Does not save sources. Export separately with inventor_save_artifact format=pdf.")]
    public Task<string> CreateDrawing(string document_id, string expected_revision, double? scale = null,
        string? sheet_size = null, string? orientation = null, string? projection = null,
        string? views = null, double? gutter_mm = null, bool preview = true,
        string? template = null, System.Text.Json.JsonElement? title_block_json = null,
        string? dimensions = null, CancellationToken ct = default)
    {
        if (!TryReadTitleBlock(title_block_json, out var titleBlock, out var why))
            return Task.FromResult(new JObject { ["ok"] = false, ["error"] = new JObject
            { ["code"] = "INVALID_ARGUMENT", ["message"] = why } }.ToString(Formatting.None));
        return CheckpointCall("create_drawing_safe", new JObject
        {
            ["document_id"] = document_id,
            ["expected_revision"] = expected_revision,
            ["scale"] = scale,
            ["sheet_size"] = sheet_size,
            ["orientation"] = orientation,
            ["projection"] = projection,
            ["views"] = views,
            ["gutter_mm"] = gutter_mm,
            ["preview"] = preview,
            ["template"] = template,
            ["title_block"] = titleBlock,
            ["dimensions"] = dimensions
        }, ct);
    }

    internal static bool TryReadTitleBlock(System.Text.Json.JsonElement? raw, out JToken? value, out string rejection)
    {
        value = null;
        rejection = "";
        if (raw == null || raw.Value.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null)
            return true;
        var element = raw.Value;
        string text = element.ValueKind == System.Text.Json.JsonValueKind.String
            ? element.GetString() ?? ""
            : element.GetRawText();
        if (element.ValueKind is not (System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.String))
        {
            rejection = "title_block_json must be a JSON object.";
            return false;
        }
        try
        {
            value = JToken.Parse(text);
            if (value is JObject) return true;
            rejection = "title_block_json must be a JSON object.";
            value = null;
            return false;
        }
        catch (Exception ex)
        {
            rejection = "title_block_json is not valid JSON: " + ex.Message;
            value = null;
            return false;
        }
    }

    [McpServerTool(Name = "inventor_list_drawing_templates"), Description("List the company drawing templates installed in the host-owned template library (INVENTOR_SO_TEMPLATES, by default %LOCALAPPDATA%/InventorSO/templates), for use as the template argument of inventor_create_drawing_safe. Read-only: nothing is opened, created or modified. Each entry reports the file name, the sheet it declares (for example 'A3 landscape') and the usable_area_mm its manifest leaves free for views. A template whose sidecar manifest is missing or malformed is still listed, with usable=false and the reason, so it can be fixed before drawing time. Arbitrary paths are never accepted: only files inside the library are visible.")]
    public Task<string> ListDrawingTemplates(CancellationToken ct = default)
        => CheckpointCall("list_drawing_templates", new JObject(), ct);

    [McpServerTool(Name = "inventor_checkpoint_create"), Description("Create a persistent hash-verified native snapshot of the active standalone single-model-state part. Requires document_id, expected_revision and label (1-120 characters). Does not save the source in place. Stored under host-owned InventorSO/checkpoints; failures may retain partial files. Assemblies and external references are not supported yet.")]
    public Task<string> CreateCheckpoint(string document_id, string expected_revision, string label, CancellationToken ct = default)
        => CheckpointCall("checkpoint_create", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["label"] = label }, ct);

    [McpServerTool(Name = "inventor_checkpoint_restore"), Description("Verify a checkpoint hash and open a new independent recovery IPT copy. Never overwrites the original file, never closes open documents, never edits the stored checkpoint. Refuses if any document with the checkpoint source identity is still open; close/save that source safely first. Accepts only a checkpoint_id returned by create/list, not paths. Recovery is NOT an in-place restore. Opening a CAD file is not a rollbackable transaction.")]
    public Task<string> RestoreCheckpoint(string checkpoint_id, CancellationToken ct = default)
        => CheckpointCall("checkpoint_restore", new JObject { ["checkpoint_id"] = checkpoint_id }, ct);

    private async Task<string> CheckpointCall(string command, JObject arguments, CancellationToken ct)
    {
        try { return (await _client.SendAsync(command, arguments, ct)).ToString(); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }

    [McpServerTool(Name = "inventor_save_artifact"), Description("Save a new part or drawing copy (native: IPT or current IDW/Inventor-DWG), part/assembly STEP (step), drawing PDF (pdf, all sheets) or the flat-pattern DXF of a sheet-metal part (dxf, optional dxf_version 2000/2004/2007/2010/2013/2018, default 2018, and optional dxf_layers_json, a JSON object naming layers such as OuterProfileLayer, InteriorProfilesLayer, BendUpLayer or BendDownLayer; the flat pattern must already exist) under host-controlled InventorSO/artifacts. Each artifact gets its own directory, so name only chooses the file stem (1-60 letters, digits, space, underscore or hyphen; default 'model') and can never overwrite an earlier export. Native drawings require saved clean referenced models inside the active Inventor project; required_project identifies that project for reopening. Dependencies are NOT copied, required_references lists their paths; this is NOT a portable package. Never saves sources/dependents in place, changes project settings or overwrites files. Requires document_id/revision; assembly/drawing must be updated with no missing references. Checks source/reference state. Filesystem output is not CAD rollback; failures can retain partial files. No manufacturing-completeness certification or assembly native packaging.")]
    public async Task<string> Save(string document_id, string expected_revision, string format,
        string? dxf_version = null, string? dxf_layers_json = null, string? name = null, CancellationToken ct = default)
    {
        try
        {
            var request = new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision,
                ["format"] = format, ["dxf_version"] = dxf_version, ["name"] = name };
            if (!string.IsNullOrWhiteSpace(dxf_layers_json))
            {
                try { request["dxf_layers"] = JObject.Parse(dxf_layers_json!); }
                catch (Exception ex)
                { return JsonConvert.SerializeObject(new { ok = false, error = new { code = "INVALID_ARGUMENT", message = "dxf_layers_json must be a JSON object of layer option to layer name: " + ex.Message } }); }
            }
            return (await _client.SendAsync("save_artifact", request, ct)).ToString();
        }
        catch (InventorGatewayException ex)
        { return ex.ToErrorJson().ToString(Formatting.None); }
    }
}
