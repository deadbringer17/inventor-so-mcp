using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only target/document probes. This class is registered by the <c>query</c> toolset and remains
/// visible in <c>--read-only</c> mode.
/// </summary>
[McpServerToolType]
public sealed class QueryTools
{
    private readonly PluginClient _client;
    public QueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_plan_native_package"), Description("Read-only native dependency packaging plan for the active part, assembly or drawing. Requires document_id and expected_revision. Preserves workspace hierarchy and reports unsaved, stale, missing, external-workspace and OLE dependencies as blockers. No source saves, project changes or filesystem writes. ready_for_copy is only preflight eligibility, NOT proof of portable references; portable_verified=false. Recompute before executing any later package operation.")]
    public Task<string> PlanNativePackage(string document_id, string expected_revision, CancellationToken ct = default)
        => Call("plan_native_package", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision }, ct);

    [McpServerTool(Name = "inventor_diff_checkpoint"), Description("Read-only comparison of active part against a new-format checkpoint semantic snapshot. Requires document_id, expected_revision and checkpoint_id. Reports parameters/features matched by EXACT NAME (rename=removed+added) and physical deltas with explicit tolerances. Parameter database_value uses Inventor internal units, not declared_units; expressions retain declared units. Feature inventory/health is compared, not B-Rep geometry; no geometric-equivalence proof, drawing or assembly diff. Does not open checkpoint CAD files or rebuild. Old checkpoints without semantic data report unavailable.")]
    public Task<string> DiffCheckpoint(string document_id, string expected_revision, string checkpoint_id, CancellationToken ct = default)
        => Call("checkpoint_diff", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["checkpoint_id"] = checkpoint_id }, ct);

    [McpServerTool(Name = "inventor_checkpoint_list"), Description("List up to 100 persisted standalone-part checkpoints, optionally by source document_id. Read-only and independent of the active document. Metadata listing does not hash the CAD files; restore verifies integrity. truncated=true indicates the list is incomplete.")]
    public Task<string> Checkpoints(string? document_id = null, CancellationToken ct = default)
        => Call("checkpoint_list", new JObject { ["document_id"] = document_id }, ct);

    [McpServerTool(Name = "inventor_health"),
     Description("Probe the active Inventor add-in target: reports inventor_year, process_id, whether a document is open, and the active document type. Read-only; use it to confirm the add-in is reachable.")]
    public Task<string> Health(CancellationToken ct = default)
        => Call("health", new JObject(), ct);

    [McpServerTool(Name = "inventor_list_open_documents"),
     Description("List all open Inventor documents: title, full path, document type, and which one is active.")]
    public Task<string> ListOpenDocuments(CancellationToken ct = default)
        => Call("list_open_documents", new JObject(), ct);

    [McpServerTool(Name = "inventor_get_document_info"),
     Description("Get the active Inventor document's title, full path, and document type.")]
    public Task<string> GetDocumentInfo(CancellationToken ct = default)
        => Call("get_document_info", new JObject(), ct);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }

    [McpServerTool(Name = "inventor_list_workspace_documents"), Description("List up to 500 CAD documents in the managed InventorSO workspace with size, modification time and whether each is currently open and dirty. Read-only: no filesystem or CAD write. Only workspace files appear; user documents opened from elsewhere are not listed. truncated=true indicates the listing is incomplete.")]
    public Task<string> WorkspaceDocuments(CancellationToken ct = default) => Call("workspace_list_documents", new JObject(), ct);

    [McpServerTool(Name = "inventor_get_sheet_metal_info"), Description("Read-only sheet-metal state of the active part: whether it is a sheet-metal part at all, the active rule with its thickness and bend radius, the rules available in the document, bend and body counts, and whether a flat pattern exists with its length, width, area and bend count. Reports is_sheet_metal=false for an ordinary part instead of failing. No CAD or filesystem write; it never unfolds the model.")]
    public Task<string> SheetMetalInfo(CancellationToken ct = default) => Call("get_sheet_metal_info", new JObject(), ct);

    [McpServerTool(Name = "inventor_list_topology"), Description("Read-only inventory of the active part body's edges (kind='edge') or faces (kind='face') with the portable entity ids modeling commands accept, so edges for a flange or fillet can be chosen without a user selecting them in Inventor. Reports geometry type, length or area, endpoints and midpoint in millimetres, and for planar faces the normal. Filter with body, geometry (substring of the Inventor geometry/surface type), min_length_mm and limit (1-200, default 50); truncated=true means more entities matched than were returned. Values Inventor cannot evaluate come back null rather than guessed.")]
    public Task<string> Topology(string kind = "edge", int body = 1, int limit = 50,
        string? geometry = null, double? min_length_mm = null, CancellationToken ct = default)
        => Call("list_topology", new JObject { ["kind"] = kind, ["body"] = body, ["limit"] = limit,
            ["geometry"] = geometry, ["min_length_mm"] = min_length_mm }, ct);

    [McpServerTool(Name = "inventor_get_selection"), Description("Read user-selected entities with portable Inventor reference keys. Requires the Inventor SO 2027 add-in. Does not change selection.")]
    public Task<string> Selection(CancellationToken ct = default) => Call("get_selection", new JObject(), ct);

    [McpServerTool(Name = "inventor_resolve_entity"), Description("Resolve a previously returned entity id against open documents. Reports unresolved or ambiguous references rather than selecting an arbitrary match. Inventor SO 2027 only.")]
    public Task<string> ResolveEntity(string entity_id, CancellationToken ct = default)
        => Call("resolve_entity", new JObject { ["entity_id"] = entity_id }, ct);
}
