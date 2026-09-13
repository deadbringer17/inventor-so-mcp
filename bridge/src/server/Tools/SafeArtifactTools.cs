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

    [McpServerTool(Name = "inventor_create_drawing_safe"), Description("Create an unsaved A3 landscape drawing from the active up-to-date part or assembly using the host default drawing template. Front, two projected and isometric views at explicit scale. Requires source document_id/revision. Checks view bounds/overlap and source state; preview=true by default closes only the new draft. Commit leaves the draft active. No dimensions, tolerances or manufacturing approval are added; manufacturing_ready=false. Does not save sources. Export separately with inventor_save_artifact format=pdf.")]
    public Task<string> CreateDrawing(string document_id, string expected_revision, double scale, bool preview = true, CancellationToken ct = default)
        => CheckpointCall("create_drawing_safe", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["scale"] = scale, ["preview"] = preview }, ct);

    [McpServerTool(Name = "inventor_checkpoint_create"), Description("Create a persistent hash-verified native snapshot of the active standalone single-model-state part. Requires document_id, expected_revision and label (1-120 characters). Does not save the source in place. Stored under host-owned InventorSO/checkpoints; failures may retain partial files. Assemblies and external references are not supported yet.")]
    public Task<string> CreateCheckpoint(string document_id, string expected_revision, string label, CancellationToken ct = default)
        => CheckpointCall("checkpoint_create", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["label"] = label }, ct);

    [McpServerTool(Name = "inventor_checkpoint_restore"), Description("Verify a checkpoint hash and open a new independent recovery IPT copy. Never overwrites the original file, never closes open documents, never edits the stored checkpoint. Refuses if any document with the checkpoint source identity is still open; close/save that source safely first. Accepts only a checkpoint_id returned by create/list, not paths. Recovery is NOT an in-place restore. Opening a CAD file is not a rollbackable transaction.")]
    public Task<string> RestoreCheckpoint(string checkpoint_id, CancellationToken ct = default)
        => CheckpointCall("checkpoint_restore", new JObject { ["checkpoint_id"] = checkpoint_id }, ct);

    private async Task<string> CheckpointCall(string command, JObject arguments, CancellationToken ct)
    {
        try { return (await _client.SendAsync(command, arguments, ct)).ToString(); }
        catch (InventorGatewayException ex) { return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }); }
    }

    [McpServerTool(Name = "inventor_save_artifact"), Description("Save a new part or drawing copy (native: IPT or current IDW/Inventor-DWG), part/assembly STEP (step), drawing PDF (pdf, all sheets) or the flat-pattern DXF of a sheet-metal part (dxf, optional dxf_version 2000/2004/2007/2010/2013/2018, default 2018, and optional dxf_layers_json, a JSON object naming layers such as OuterProfileLayer, InteriorProfilesLayer, BendUpLayer or BendDownLayer; the flat pattern must already exist) under host-controlled InventorSO/artifacts. Native drawings require saved clean referenced models inside the active Inventor project; required_project identifies that project for reopening. Dependencies are NOT copied, required_references lists their paths; this is NOT a portable package. Never saves sources/dependents in place, changes project settings or overwrites files. Requires document_id/revision; assembly/drawing must be updated with no missing references. Checks source/reference state. Filesystem output is not CAD rollback; failures can retain partial files. No manufacturing-completeness certification or assembly native packaging.")]
    public async Task<string> Save(string document_id, string expected_revision, string format,
        string? dxf_version = null, string? dxf_layers_json = null, CancellationToken ct = default)
    {
        try
        {
            var request = new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision,
                ["format"] = format, ["dxf_version"] = dxf_version };
            if (!string.IsNullOrWhiteSpace(dxf_layers_json))
            {
                try { request["dxf_layers"] = JObject.Parse(dxf_layers_json!); }
                catch (Exception ex)
                { return JsonConvert.SerializeObject(new { ok = false, error = new { code = "INVALID_ARGUMENT", message = "dxf_layers_json must be a JSON object of layer option to layer name: " + ex.Message } }); }
            }
            return (await _client.SendAsync("save_artifact", request, ct)).ToString();
        }
        catch (InventorGatewayException ex)
        { return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }); }
    }
}
