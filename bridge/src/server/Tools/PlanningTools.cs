using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// PLAN → PREVIEW → (user approval in the client) → COMMIT (plan §18), and motion sampling.
/// The server never interprets natural language: the agent supplies batch operations, the server
/// checks them, previews them for real inside an aborted transaction and binds the result to a
/// revision and a content hash.
/// </summary>
[McpServerToolType]
public sealed class PlanningTools
{
    private readonly PluginClient _client;
    private readonly ChangePlanStore _plans;
    private readonly InventorMcpConfig _config;
    private readonly ICallerIdentity _caller;

    public PlanningTools(PluginClient client, ChangePlanStore plans, InventorMcpConfig config, ICallerIdentity caller)
    {
        _client = client; _plans = plans; _config = config; _caller = caller;
    }

    [McpServerTool(Name = "inventor_plan_change"), Description("Plan a change without applying it. operations uses the inventor_atomic_batch vocabulary (resource inventor://batch-commands); validate optionally adds checks such as [\"interference\",\"min_clearance:2mm\"]; intent is free text kept with the plan for the approver. The server checks the plan against the catalogue, runs a real preview (executed then rolled back, revision restored), estimates impact (dependent features of edited parameters when dependency analysis is available) and returns a plan_id valid 15 minutes, bound to document_id, expected_revision and the SHA-256 of the operations. Show the preview to the user; commit with inventor_commit_plan. Requires write permission because the preview edits then aborts.")]
    public async Task<string> PlanChange(string document_id, string expected_revision, AtomicOperation[] operations,
        string[]? validate = null, string? intent = null, CancellationToken ct = default)
    {
        if (!ParameterTools.TryBuildOperations(operations, out var ops, out var rejection))
            return rejection!.ToString(Formatting.None);
        JToken? checks = validate == null ? null : new JArray(validate);
        try
        {
            CheckOperations(ops, _config.EnableExperimental || _config.FullAccess);
            if (intent != null && intent.Length > 2000) throw new PlanException(InventorErrorCodes.INVALID_ARGUMENT, "intent is limited to 2000 characters.");
        }
        catch (PlanException ex) { return XrTools.Error(ex.Code, ex.Message); }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        JObject preview;
        try
        {
            preview = (JObject)await _client.SendAsync("atomic_batch", new JObject
            {
                ["document_id"] = document_id, ["expected_revision"] = expected_revision,
                ["operations"] = ops, ["preview"] = true, ["validate"] = checks,
            }, ct);
        }
        catch (InventorGatewayException ex)
        {
            var failed = ex.ToErrorJson();
            failed["planned"] = false;
            return failed.ToString(Formatting.None);
        }

        var plan = _plans.Create(_caller.Client, document_id, expected_revision, ops, checks, intent);
        plan.Preview = preview;
        plan.Impact = await ImpactOf(ops, ct);
        var result = plan.ToJson();
        result["next"] = "Show preview and impact to the user; on approval call inventor_commit_plan(plan_id, expected_revision). Any other change to the document invalidates the plan.";
        return result.ToString(Formatting.None);
    }

    [McpServerTool(Name = "inventor_commit_plan"), Description("Commit a plan made by inventor_plan_change exactly as previewed: same operations (hash-checked), same checks, same document and revision. Refuses with PLAN_NOT_FOUND (unknown, expired, foreign or already used) or PLAN_MISMATCH (document or revision moved: plan again), and with STALE_REVISION if the document changed after planning. The commit is one atomic Inventor transaction with rollback on any failure. A plan is consumed by the attempt, successful or not.")]
    public async Task<string> CommitPlan(string plan_id, string document_id, string expected_revision, CancellationToken ct = default)
    {
        ChangePlanStore.Plan plan;
        try { plan = _plans.Take(plan_id, _caller.Client, document_id, expected_revision); }
        catch (PlanException ex) { return XrTools.Error(ex.Code, ex.Message); }
        try
        {
            var committed = (JObject)await _client.SendAsync("atomic_batch", new JObject
            {
                ["document_id"] = plan.DocumentId, ["expected_revision"] = plan.Revision,
                ["operations"] = plan.Operations, ["preview"] = false, ["validate"] = plan.Validate,
            }, ct);
            committed["plan_id"] = plan.Id;
            committed["operations_sha256"] = plan.Hash;
            return committed.ToString(Formatting.None);
        }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }

    [McpServerTool(Name = "inventor_list_plans"), Description("List this client's pending change plans (id, document, revision, intent, expiry). Plans are held in server memory for 15 minutes and are never shared between clients.")]
    public string ListPlans() => new JObject
    {
        ["plans"] = new JArray(_plans.List(_caller.Client).Select(p => new JObject
        {
            ["plan_id"] = p.Id, ["document_id"] = p.DocumentId, ["document_revision"] = p.Revision, ["intent"] = p.Intent,
            ["operation_count"] = p.Operations.Count, ["expires_utc"] = p.ExpiresUtc.ToString("O"),
        })),
    }.ToString(Formatting.None);

    [McpServerTool(Name = "inventor_sample_parameter_motion"), Description("Collision-aware motion study without changing the model: set one parameter (a constraint offset or angle, a joint parameter or a user parameter) to steps values evenly spaced from from_value to to_value (in the parameter's own units, e.g. mm or deg), and at each sample rebuild and run checks - rebuild, interference, min_clearance:<n>mm (assemblies). Everything runs inside one transaction that is always aborted; the revision is restored. Returns per-sample results, first_failure and the valid sub-ranges. steps 2-100. Requires document_id/expected_revision and write permission (it edits then aborts). Experimental tier.")]
    public Task<string> SampleParameterMotion(string document_id, string expected_revision, string parameter, double from_value, double to_value,
        int steps = 10, string[]? checks = null, CancellationToken ct = default)
    {
        if (steps < 2 || steps > 100) return Task.FromResult(XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, "steps must be between 2 and 100."));
        if (double.IsNaN(from_value) || double.IsNaN(to_value) || double.IsInfinity(from_value) || double.IsInfinity(to_value))
            return Task.FromResult(XrTools.Error(InventorErrorCodes.INVALID_ARGUMENT, "from_value and to_value must be finite."));
        return Call("sample_parameter_motion", new JObject
        {
            ["document_id"] = document_id, ["expected_revision"] = expected_revision, ["parameter"] = parameter,
            ["from_value"] = from_value, ["to_value"] = to_value, ["steps"] = steps,
            ["checks"] = new JArray(checks ?? new[] { "rebuild" }),
        }, ct);
    }

    /// <summary>Catalogue check before anything reaches Inventor, mirroring the add-in's own refusal.</summary>
    internal static void CheckOperations(JArray operations, bool experimental)
    {
        if (operations.Count < 1 || operations.Count > 32)
            throw new PlanException(InventorErrorCodes.INVALID_ARGUMENT, "Use 1 to 32 operations per plan.");
        for (int i = 0; i < operations.Count; i++)
        {
            if (operations[i] is not JObject step || step["command"]?.Type != JTokenType.String)
                throw new PlanException(InventorErrorCodes.INVALID_ARGUMENT, "operation " + i + ": needs a command string and an arguments object.");
            var entry = CadBatchCommandCatalog.Find((string?)step["command"]);
            if (entry == null)
                throw new PlanException(InventorErrorCodes.INVALID_ARGUMENT, "operation " + i + ": '" + step["command"] + "' is not a batch command (see inventor://batch-commands).");
            if (entry.Experimental && !experimental)
                throw new PlanException(InventorErrorCodes.EXPERIMENTAL_DISABLED, "operation " + i + ": '" + entry.Name + "' is experimental; start the server with --enable-experimental.");
            if (step["arguments"] is not JObject)
                throw new PlanException(InventorErrorCodes.INVALID_ARGUMENT, "operation " + i + ": '" + entry.Name + "' needs an arguments object.");
        }
    }

    /// <summary>
    /// Best-effort impact: for every edited parameter, what depends on it. Dependency analysis is an
    /// experimental add-in query; when it is unavailable the impact says so instead of guessing.
    /// </summary>
    private async Task<JObject> ImpactOf(JArray operations, CancellationToken ct)
    {
        var parameters = operations.OfType<JObject>()
            .Where(o => (string?)o["command"] is "set_parameter" or "rename_parameter" or "delete_parameter")
            .Select(o => (string?)o["arguments"]?["name"]).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
        var impact = new JObject
        {
            ["commands"] = new JArray(operations.OfType<JObject>().Select(o => (string?)o["command"]).Distinct()),
            ["parameters"] = new JArray(parameters.Cast<object>().ToArray()),
        };
        if (parameters.Length == 0 || !(_config.EnableExperimental || _config.FullAccess))
        {
            impact["dependencies"] = parameters.Length == 0 ? new JArray() : null;
            if (parameters.Length > 0) impact["dependencies_unavailable"] = "dependency analysis is an experimental query";
            return impact;
        }
        var dependencies = new JArray();
        foreach (var name in parameters)
        {
            try
            {
                var trace = await _client.SendAsync("get_dependencies", new JObject { ["name"] = name, ["depth"] = 8 }, ct);
                dependencies.Add(trace);
            }
            catch (InventorGatewayException ex)
            {
                dependencies.Add(new JObject { ["name"] = name, ["error"] = ex.Code });
            }
        }
        impact["dependencies"] = dependencies;
        return impact;
    }

    private async Task<string> Call(string command, JObject arguments, CancellationToken ct)
    {
        try { return (await _client.SendAsync(command, arguments, ct)).ToString(Formatting.None); }
        catch (InventorGatewayException ex) { return ex.ToErrorJson().ToString(Formatting.None); }
    }
}
