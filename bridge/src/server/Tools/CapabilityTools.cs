using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Discovery (plan §25): what this server and the connected add-in can actually do, computed rather
/// than declared, and the semantic contract of every tool.
/// </summary>
[McpServerToolType]
public sealed class CapabilityTools
{
    private readonly PluginClient _client;
    private readonly InventorMcpConfig _config;

    public CapabilityTools(PluginClient client, InventorMcpConfig config)
    {
        _client = client;
        _config = config;
    }

    [McpServerTool(Name = "inventor_get_capabilities"), Description("What this Inventor SO server and the connected add-in can do right now, so a client never assumes a capability: server transport, read-only, experimental and full-access state; the selected Inventor target and whether it answers; capability flags computed from the commands the add-in actually registered (atomic_batch, drawings, sheet_metal, xr_mesh, scene_graph, camera, sketch_inspection, change_plans, motion_sampling, release_package, content_center, ...); and which batch commands are runnable versus experimental-only. Read-only, never fails: an unreachable add-in yields reachable=false with the reason and CAD capabilities false.")]
    public async Task<string> GetCapabilities(CancellationToken ct = default)
    {
        JObject? addIn = null;
        string? unreachable = null;
        try { addIn = await _client.SendAsync("get_capabilities", new JObject(), ct) as JObject; }
        catch (InventorGatewayException ex) { unreachable = ex.Code + ": " + ex.Message; }
        return Compute(_config, _client.CurrentTarget, addIn, unreachable).ToString(Formatting.None);
    }

    internal static JObject Compute(InventorMcpConfig config, TargetDescriptor? target, JObject? addIn, string? unreachable)
    {
        var commands = new HashSet<string>(
            (addIn?["commands"] as JArray ?? new JArray()).Select(t => (string?)t).Where(t => t != null)!, StringComparer.OrdinalIgnoreCase!);
        bool addInExperimental = (bool?)addIn?["experimental_enabled"] ?? false;
        bool serverExperimental = config.EnableExperimental || config.FullAccess;
        bool Has(string command) => commands.Contains(command);
        bool Experimental(string command) => serverExperimental && addInExperimental && Has(command);

        var flags = new JObject
        {
            ["atomic_batch"] = Has("atomic_batch"),
            ["change_plans"] = Has("atomic_batch") && !config.ReadOnly,
            ["drawings"] = Has("create_drawing_safe"),
            ["sheet_metal"] = Has("sheet_metal_face"),
            ["checkpoints"] = Has("checkpoint_create"),
            ["assembly_constraints"] = Has("create_constraint_safe"),
            ["bom"] = Has("get_assembly_bom"),
            ["xr_mesh"] = Experimental("get_display_mesh"),
            ["flat_pattern_mesh"] = Experimental("get_flat_pattern_mesh"),
            ["scene_graph"] = Experimental("get_scene_graph"),
            ["highlight"] = Experimental("highlight_entity"),
            ["camera"] = Experimental("get_camera"),
            ["raycast"] = Experimental("raycast_entity"),
            ["sketch_inspection"] = Experimental("get_sketch_info"),
            ["dependencies"] = Experimental("get_dependencies"),
            ["semantic_state"] = Experimental("get_semantic_state"),
            ["representations"] = Experimental("get_representations"),
            ["assembly_health"] = Experimental("get_assembly_health"),
            ["drawing_validation"] = Experimental("validate_drawing"),
            ["motion_sampling"] = Experimental("sample_parameter_motion") && !config.ReadOnly,
            ["release_package"] = serverExperimental && Has("save_artifact") && !config.ReadOnly,
            ["event_subscriptions"] = Has("get_events"),
            ["selection_events"] = addInExperimental && ((bool?)addIn?["selection_events"] ?? false),
            ["http_assets"] = config.Transport == "http",
            ["content_center"] = false,
            ["fastener_intelligence"] = false,
            ["import"] = false,
        };

        var runnable = new JArray();
        var unavailable = new JArray();
        foreach (var entry in CadBatchCommandCatalog.All)
        {
            string? reason = null;
            if (addIn == null) reason = "add-in unreachable";
            else if (!Has(entry.Name)) reason = entry.Experimental ? "not in this add-in build (build with -p:SoExperimental=true)" : "not registered by the add-in";
            else if (entry.Experimental && !addInExperimental) reason = "add-in not started with INVENTOR_SO_EXPERIMENTAL=1";
            if (reason == null) runnable.Add(entry.Name);
            else unavailable.Add(new JObject { ["command"] = entry.Name, ["stability"] = entry.Experimental ? "experimental" : "stable", ["reason"] = reason });
        }

        return new JObject
        {
            ["server"] = new JObject
            {
                ["name"] = "inventor-so-mcp",
                ["version"] = typeof(CapabilityTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(CapabilityTools).Assembly.GetName().Version?.ToString(),
                ["transport"] = config.Transport,
                ["read_only"] = config.ReadOnly,
                ["experimental_enabled"] = config.EnableExperimental,
                ["full_access"] = config.FullAccess,
                ["http_insecure_lan"] = config.Transport == "http" && config.HttpAllowInsecureLan,
                ["audit"] = config.AuditEnabled,
            },
            ["target"] = new JObject
            {
                ["target_id"] = target?.TargetId,
                ["inventor_year"] = (int?)addIn?["inventor_year"] ?? target?.InventorYear,
                ["reachable"] = addIn != null,
                ["reason"] = unreachable,
                ["add_in_experimental_build"] = (bool?)addIn?["experimental_build"] ?? false,
                ["add_in_experimental_enabled"] = addInExperimental,
                ["active_document_kind"] = addIn?["active_document_kind"]?.DeepClone(),
            },
            ["capabilities"] = flags,
            ["batch_commands"] = new JObject { ["runnable"] = runnable, ["unavailable"] = unavailable },
            ["validation_checks"] = ValidationSpec.Describe(),
        };
    }

    [McpServerTool(Name = "inventor_get_tool_schema"), Description("Semantic contract of a tool (plan §33), beyond the JSON parameter schema that tools/list already gives: tier (production or experimental), access (query, view, write, plan, artifact, meta), whether document_id and expected_revision are required, whether it is previewable, document kinds, validators, rollback behaviour, external effects, verification level and remote representation. Omit name to list every contract. For inventor_atomic_batch the full command catalogue (with document kinds and stability) and the validation vocabulary are included. Read-only, server-side.")]
    public string GetToolSchema(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new JObject
            {
                ["tools"] = new JArray(ToolContracts.Contracts
                    .Where(c => SoToolPolicy.IsExposed(c.Name, _config.FullAccess, _config.EnableExperimental))
                    .Select(c => c.ToJson())),
                ["hidden_experimental"] = _config.EnableExperimental || _config.FullAccess ? new JArray()
                    : new JArray(SoToolPolicy.Experimental.OrderBy(n => n, StringComparer.Ordinal)),
            }.ToString(Formatting.None);
        var contract = ToolContracts.Find(name);
        if (contract == null)
            return XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, "No contract for '" + name + "'. Tools without a contract are never exposed by the Inventor SO policy.");
        var result = contract.ToJson();
        result["exposed"] = SoToolPolicy.IsExposed(contract.Name, _config.FullAccess, _config.EnableExperimental);
        if (contract.Name is "inventor_atomic_batch" or "inventor_plan_change" or "inventor_commit_plan")
            result["batch_catalog"] = CadBatchCommandCatalog.Describe(includeExperimental: _config.EnableExperimental || _config.FullAccess);
        return result.ToString(Formatting.None);
    }
}
