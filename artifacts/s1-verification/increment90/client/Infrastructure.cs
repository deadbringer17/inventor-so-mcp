using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private const double Tol = 1e-5;
    private static string Phase = "arguments";
    private static void Require(bool value, string assertion) { if (!value) throw new Assertion83(assertion); }

    private static JObject Operation(string sketchId, string variant, JObject options) => new()
    { ["command"] = "edit_sketch", ["arguments"] = new JObject { ["sketch_id"] = sketchId, ["variant"] = variant, ["options"] = options } };
    private static JObject Represented(NativeSketchSnapshot s) => new()
    { ["entities"] = new JArray(s.Entities.Select(e => e.Data)), ["dimensions"] = new JArray(s.Dimensions.Select(e => e.Data)),
        ["constraints"] = new JArray(s.Constraints.Select(e => e.Data)), ["complete"] = s.NativeMutationComplete, ["partial"] = s.Partial, ["constraint_status"] = s.ConstraintStatus };
    private static bool Same(DocumentState a, DocumentState b) => a.DocumentId == b.DocumentId && a.Revision == b.Revision;
    private static async Task Inventory(InventorBackend backend, S1AcceptanceBootstrapMetadata binding, CancellationToken ct)
    {
        Require(binding.ExpiresUtc > DateTimeOffset.UtcNow, "QA binding expired.");
        var docs = await backend.ListOpenAsync(ct);
        Require(docs.Count == 2 && docs.Any(d => d.Id == binding.PartDocumentId && d.Name == "XR_S1_Quest_Acceptance_Part.ipt")
            && docs.Any(d => d.Id == binding.SheetDocumentId && d.Name == "XR_S1_Quest_Acceptance_Sheet.ipt"), "Exact owned fixture inventory differs.");
    }
    private static async Task Guard(InventorBackend backend, S1AcceptanceBootstrapMetadata binding, DocumentState state, CancellationToken ct)
    {
        await Inventory(backend, binding, ct);
        Require(state.DocumentId == binding.PartDocumentId || state.DocumentId == binding.SheetDocumentId, "Mutation document is not owned.");
        Require(Same(state, await backend.GetDocumentStateAsync(ct)), "Mutation baseline is no longer current.");
    }
    private static async Task<(DocumentState State, NativeSketchSnapshot Snapshot)> Apply(InventorBackend backend,
        S1AcceptanceBootstrapMetadata binding, DocumentState state, NativeSketchSnapshot before, JObject operation, string kind, CancellationToken ct)
    {
        await Guard(backend, binding, state, ct);
        using var session = new DesignSession(backend); session.SetContext(state, true, true); session.SetDraft(new JArray { operation });
        Phase = kind + "_preview"; await session.PreviewAsync();
        Require(session.Status == DesignStatus.PreviewReady && session.Preview != null && !session.CanApply, "Preview failed its render-confirmation gate.");
        var restoredState = await backend.GetDocumentStateAsync(ct);
        var restored = await backend.GetNativeSketchAsync(restoredState, before.SketchId, ct);
        Require(Same(state, restoredState) && JToken.DeepEquals(Represented(before), Represented(restored)), "Preview rollback changed represented sketch/revision.");
        var preview = session.Preview!.Sketches.Single(s => s.Name == "S1_Copy88");
        Require(preview.NativeMutationComplete && preview.NativePaths.Count > 0, "Native preview sketch is incomplete or lacks paths.");
        await Guard(backend, binding, state, ct);
        session.ConfirmRendered(session.Preview.PlanId); // Explicit synthetic PC proof; never a Unity/Quest rendering assertion.
        Phase = kind + "_apply"; await session.ApplyAsync();
        Require(!session.CommitOutcomeUnknown && session.LastCommit != null && session.LastCommit.DocumentId == state.DocumentId
            && session.LastCommit.Revision != state.Revision, "Native commit outcome is unknown or unchanged.");
        var after = session.LastCommit!;
        Phase = kind + "_first_snapshot";
        var first = await backend.GetNativeSketchAsync(after, before.SketchId, ct); // FIRST request after Apply; no intervening oracle/read/repair.
        Require(first.NativeMutationComplete && !first.Partial, "First post-Apply snapshot is incomplete.");
        Require(Same(after, await backend.GetDocumentStateAsync(ct)), "First snapshot changed document/revision.");
        foreach (var old in before.Entities)
            Require(first.Entities.Any(e => e.Reference.EntityId == old.Reference.EntityId && e.Type == old.Type), "Existing native entity identity was lost.");
        foreach (var old in before.Constraints)
            Require(first.Constraints.Any(e => e.Reference.EntityId == old.Reference.EntityId), "Existing native constraint identity was lost.");
        return (after, first);
    }

    private static async Task Refused(InventorBackend backend, S1AcceptanceBootstrapMetadata binding, DocumentState request,
        DocumentState actual, NativeSketchSnapshot before, JArray operations, string kind, bool stale, JObject report, CancellationToken ct)
    {
        Phase = "refusal_" + kind; await Guard(backend, binding, actual, ct);
        string? code = null, stepCode = null; int? stepIndex = null;
        try { await backend.PreviewDesignAsync(request, operations, ct); }
        catch (McpException error) when (stale ? error.Code == "STALE_REVISION" : error.Code is "ROLLED_BACK" or "INVALID_ARGUMENT")
        {
            code = error.Code; var details = (error as McpToolException)?.Details;
            stepCode = (string?)details?["details"]?["step_code"] ?? (string?)details?["step_code"];
            stepIndex = (int?)details?["details"]?["step_index"] ?? (int?)details?["step_index"];
            report["last_refusal_observation"] = new JObject { ["kind"] = kind, ["code"] = code,
                ["step_code"] = stepCode, ["step_index"] = stepIndex };
            Require(code != "ROLLED_BACK" || stepCode == "INVALID_ARGUMENT", "Refusal rollback must identify the invalid argument step.");
        }
        Require(code != null, "Expected native refusal was not observed.");
        Require(kind != "post_valid_copy_failure" || code == "ROLLED_BACK" && stepIndex == 1,
            "Second-step failure must identify step1 after a valid copy creation.");
        Phase = "refusal_" + kind + "_state";
        var state = await backend.GetDocumentStateAsync(ct);
        report["last_refusal_revision"] = new JObject { ["expected"] = actual.Revision, ["observed"] = state.Revision };
        Phase = "refusal_" + kind + "_snapshot";
        var after = await backend.GetNativeSketchAsync(state, before.SketchId, ct);
        Require(Same(actual, state) && JToken.DeepEquals(Represented(before), Represented(after)), "Refused operation changed represented sketch/revision.");
        ((JArray)report["refusals"]!).Add(new JObject { ["document_id"] = actual.DocumentId, ["kind"] = kind,
            ["code"] = code, ["step_code"] = stepCode, ["step_index"] = stepIndex,
            ["represented_rollback_exact"] = true, ["event_revision_preserved"] = true });
    }
    private static JObject Failure(Exception error)
    {
        var result = new JObject { ["phase"] = Phase, ["type"] = error.GetType().FullName, ["hresult"] = error.HResult };
        if (error is Assertion83 assertion) result["assertion"] = assertion.Message;
        if (error is McpException mcp && System.Text.RegularExpressions.Regex.IsMatch(mcp.Code ?? "", "\\A[A-Z0-9_]{1,64}\\z")) result["code"] = mcp.Code;
        if (error is McpToolException tool)
        {
            var details = tool.Details?["details"] as JObject ?? tool.Details as JObject;
            foreach (string field in new[] { "expected_revision", "actual_revision", "phase", "step_code", "step_index" })
                if (details?[field] is JValue value) result["detail_" + field] = value.DeepClone();
        }
        return result;
    }
    private sealed class Assertion83(string assertion) : Exception(assertion);
}

