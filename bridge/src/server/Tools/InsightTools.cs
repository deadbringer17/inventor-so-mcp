using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only inspection and engineering queries (plan §7, §10-§14, §17). The BOM tools are
/// server-side analyses over the verified BOM query; the others are experimental add-in queries.
/// </summary>
[McpServerToolType]
public sealed class InsightTools
{
    private readonly PluginClient _client;
    public InsightTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_validate_bom"), Description("Check the active assembly's BOM (from the verified BOM query): blank part numbers, one part number used by different files, conflicting or missing descriptions, non-positive quantities, truncation. Returns valid (no error-severity finding), counts and findings with codes (PART_NUMBER_MISSING, PART_NUMBER_DUPLICATE, DESCRIPTION_CONFLICT, DESCRIPTION_MISSING, QUANTITY_INVALID, BOM_TRUNCATED). Read-only.")]
    public async Task<string> ValidateBom(int max_rows = 2000, CancellationToken ct = default)
    {
        try
        {
            var bom = await _client.SendAsync("get_assembly_bom", new JObject { ["max_rows"] = max_rows }, ct);
            var result = BomAnalysis.Validate(BomAnalysis.ReadRows(bom), (bool?)bom["truncated"] ?? false);
            return result.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
        catch (System.ArgumentException ex) { return XrTools.Error(InventorErrorCodes.API_ERROR, ex.Message); }
    }

    [McpServerTool(Name = "inventor_compare_bom"), Description("Compare a baseline BOM with the active assembly's current BOM. baseline_json is the JSON returned earlier by inventor_get_assembly_bom (or its bom array). Rows are matched by part number (by file when the part number is blank) and merged by quantity. Returns identical, added, removed and changed (qty, description, path before/after). Read-only.")]
    public async Task<string> CompareBom(string baseline_json, int max_rows = 2000, CancellationToken ct = default)
    {
        System.Collections.Generic.IReadOnlyList<BomAnalysis.Row> baseline;
        try { baseline = BomAnalysis.ReadRows(JToken.Parse(baseline_json)); }
        catch (System.Exception ex) when (ex is JsonException or System.ArgumentException)
        { return XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, "baseline_json: " + ex.Message); }
        try
        {
            var bom = await _client.SendAsync("get_assembly_bom", new JObject { ["max_rows"] = max_rows }, ct);
            var result = BomAnalysis.Compare(baseline, BomAnalysis.ReadRows(bom));
            result["current_truncated"] = (bool?)bom["truncated"] ?? false;
            return result.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }

    [McpServerTool(Name = "inventor_get_sketch_info"), Description("Inspect a sketch of the active part (default the most recent): entities with 1-based ids, type, construction flag and coordinates in mm; geometric constraints; dimensions (parameter name, expression, value, driven); constraint status (fully/under/over constrained) and closed profile loops. Use the ids with the sketch batch commands. Read-only; experimental tier.")]
    public Task<string> GetSketchInfo(string? sketch_name = null, int max_entities = 2000, CancellationToken ct = default)
        => Call("get_sketch_info", new JObject { ["sketch_name"] = sketch_name, ["max_entities"] = max_entities }, ct);

    [McpServerTool(Name = "inventor_get_dependencies"), Description("Dependencies of a parameter (by name) or feature (by feature name) in the active part or assembly: kind (model/user/reference/feature), expression, unit, driven_by (what it reads) and dependents (what reads it), followed up to depth levels (1-8, default 1). Read-only; experimental tier.")]
    public Task<string> GetDependencies(string name, int depth = 1, CancellationToken ct = default)
        => Call("get_dependencies", new JObject { ["name"] = name, ["depth"] = depth }, ct);

    [McpServerTool(Name = "inventor_trace_dependency"), Description("Transitive dependency trace of a parameter or feature (depth 8): every node reached through dependents, with the path, so the impact of a change is visible before planning it. Read-only; experimental tier.")]
    public Task<string> TraceDependency(string name, CancellationToken ct = default)
        => Call("get_dependencies", new JObject { ["name"] = name, ["depth"] = 8, ["trace"] = true }, ct);

    [McpServerTool(Name = "inventor_get_semantic_state"), Description("Semantic graph of the active part or assembly: nodes (parameters, features, sketches, solid bodies, work features, occurrences, constraints) with stable ids where Inventor provides them, type, name, document, revision and metadata (health, suppression, values), and relations (drives, consumes, constrains, instance_of). Bounded by max_nodes (default 2000). Read-only; experimental tier.")]
    public Task<string> GetSemanticState(int max_nodes = 2000, CancellationToken ct = default)
        => Call("get_semantic_state", new JObject { ["max_nodes"] = max_nodes }, ct);

    [McpServerTool(Name = "inventor_get_representations"), Description("Model states, design view representations and (assemblies) positional representations of the active document, with the active one of each. Activate them with the batch commands activate_model_state, activate_design_view, activate_positional_representation. Read-only; experimental tier.")]
    public Task<string> GetRepresentations(CancellationToken ct = default) => Call("get_representations", new JObject(), ct);

    [McpServerTool(Name = "inventor_get_assembly_health"), Description("Health of the active assembly: per-occurrence degrees of freedom (translational/rotational) and grounding, unconstrained occurrences, constraint and joint health with the failing ones listed. Read-only; experimental tier.")]
    public Task<string> GetAssemblyHealth(int max_occurrences = 2000, CancellationToken ct = default)
        => Call("get_assembly_health", new JObject { ["max_occurrences"] = max_occurrences }, ct);

    [McpServerTool(Name = "inventor_validate_drawing"), Description("Check the active drawing: unresolved or missing model references, views without a model, views outside their sheet, overlapping views, empty sheets, and assemblies drawn without a parts list. Returns valid plus findings with codes. Read-only; experimental tier.")]
    public Task<string> ValidateDrawing(CancellationToken ct = default) => Call("validate_drawing", new JObject(), ct);

    private async Task<string> Call(string command, JObject arguments, CancellationToken ct)
    {
        try { return (await _client.SendAsync(command, arguments, ct)).ToString(Formatting.None); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }
}
