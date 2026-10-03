using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>
/// The semantic contract of every tool the Inventor SO policy can expose - the ten questions of the
/// implementation plan (§33) answered in data. <see cref="SoToolPolicy"/> derives its production and
/// experimental lists from here, so a tool without a contract cannot be exposed, and
/// <c>inventor_get_tool_schema</c> publishes the same answers the policy enforces.
/// </summary>
public static class ToolContracts
{
    public const string Production = "production";
    public const string Experimental = "experimental";

    /// <summary>query: reads; view: changes only view state (no revision); write: changes a document;
    /// plan: preview/plan workflow; meta: server-side only; artifact: writes host-owned files.</summary>
    public sealed class Contract
    {
        public string Name { get; init; } = "";
        public string Tier { get; init; } = Production;
        public string Access { get; init; } = "query";
        public bool RequiresRevision { get; init; }
        public bool Previewable { get; init; }
        public string[] Documents { get; init; } = Array.Empty<string>();
        public string[] Validators { get; init; } = Array.Empty<string>();
        public string Rollback { get; init; } = "not applicable";
        public string ExternalEffects { get; init; } = "none";
        public string Verification { get; init; } = "L1/L2";
        public string Remote { get; init; } = "JSON over MCP";

        public JObject ToJson() => new()
        {
            ["name"] = Name,
            ["tier"] = Tier,
            ["access"] = Access,
            ["requires_document_id_and_revision"] = RequiresRevision,
            ["previewable"] = Previewable,
            ["documents"] = new JArray(Documents),
            ["validators"] = new JArray(Validators),
            ["rollback"] = Rollback,
            ["external_effects"] = ExternalEffects,
            ["verification"] = Verification,
            ["remote_representation"] = Remote,
        };
    }

    private static readonly string[] Pa = { "part" }, As = { "assembly" }, Dr = { "drawing" },
        PA = { "part", "assembly" }, PAD = { "part", "assembly", "drawing" }, None = Array.Empty<string>();

    private const string Live = "L1/L2 + live L3/L4 (docs/DEVELOPMENT.md)";
    private const string Pending = "L1/L2 only; live L3/L4 pending";
    private const string Txn = "owned Inventor transaction, aborted on any failure";

    private static Contract Q(string name, string[] docs, string tier = Production, string verification = Live) =>
        new() { Name = name, Tier = tier, Access = "query", Documents = docs, Verification = verification };

    private static Contract Meta(string name) =>
        new() { Name = name, Access = "meta", Verification = "L1/L2 (server-side only)" };

    private static Contract W(string name, string[] docs, string rollback = Txn, string[]? validators = null,
        bool preview = true, string effects = "modifies the target document only", string tier = Production, string verification = Live) =>
        new() { Name = name, Tier = tier, Access = "write", RequiresRevision = true, Previewable = preview, Documents = docs,
            Validators = validators ?? Array.Empty<string>(), Rollback = rollback, ExternalEffects = effects, Verification = verification };

    private static Contract V(string name, string[] docs) =>
        new() { Name = name, Tier = Experimental, Access = "view", Documents = docs, Verification = Pending,
            Rollback = "not needed: view state only, no document revision change", ExternalEffects = "changes the Inventor window only" };

    private static Contract XQ(string name, string[] docs, string remote = "JSON over MCP") =>
        new() { Name = name, Tier = Experimental, Access = "query", Documents = docs, Verification = Pending, Remote = remote };

    private static readonly Contract[] All =
    {
        // ---- Production: server-side discovery and target selection ----
        Meta("inventor_list_available_targets"), Meta("inventor_get_current_target"), Meta("inventor_switch_target"),
        Meta("inventor_get_capabilities"), Meta("inventor_get_tool_schema"),

        // ---- Production: verified queries ----
        Q("inventor_health", None), Q("inventor_list_open_documents", None), Q("inventor_get_document_info", PAD),
        Q("inventor_get_selection", PAD), Q("inventor_resolve_entity", PAD), Q("inventor_plan_native_package", PAD),
        Q("inventor_get_sheet_metal_info", Pa), Q("inventor_list_topology", PA),
        Q("inventor_list_parameters", PA), Q("inventor_get_parameter", PA), Q("inventor_get_iproperty", PAD),
        Q("inventor_get_mass_properties", PA), Q("inventor_list_interfaces", As), Q("inventor_check_interference", As),
        Q("inventor_measure_min_distance", As), Q("inventor_get_assembly_bom", As), Q("inventor_list_constraints", As),
        Q("inventor_list_workspace_documents", None), Q("inventor_checkpoint_list", Pa), Q("inventor_diff_checkpoint", Pa),
        Q("inventor_list_drawing_templates", None),
        new() { Name = "inventor_validate_bom", Access = "query", Documents = As, Verification = "L1/L2 over the verified BOM query" },
        new() { Name = "inventor_compare_bom", Access = "query", Documents = As, Verification = "L1/L2 over the verified BOM query" },

        // ---- Production: verified writes ----
        W("inventor_atomic_batch", PAD, validators: new[] { "rebuild", "feature_health", "constraint_health", "caller validate[]" }),
        W("inventor_move_component_safe", As, validators: new[] { "rebuild", "constraint_health", "interference", "min_clearance" }),
        W("inventor_edit_constraint_safe", As, validators: new[] { "rebuild", "constraint_health", "interference", "min_clearance" }),
        W("inventor_create_constraint_safe", As, validators: new[] { "rebuild", "constraint_health", "interference", "min_clearance" }),
        W("inventor_create_joint_safe", As, validators: new[] { "rebuild", "constraint_health", "interference", "min_clearance" }),
        W("inventor_ground_component_safe", As, validators: new[] { "rebuild", "constraint_health" }),
        W("inventor_insert_component_safe", As, validators: new[] { "rebuild", "constraint_health", "interference", "min_clearance" }),
        W("inventor_create_drawing_safe", PA, rollback: "preview closes the new unsaved drawing; sources untouched",
            effects: "creates a new unsaved drawing document", validators: new[] { "view bounds", "view overlap", "source up to date" }),
        W("inventor_save_artifact", PAD, rollback: "none: filesystem output; a failed export may leave partial files in its own new folder",
            preview: false, effects: "writes a new host-owned artifact folder"),
        W("inventor_checkpoint_create", Pa, rollback: "none: new checkpoint folder", preview: false, effects: "writes a new host-owned checkpoint"),
        W("inventor_checkpoint_restore", Pa, rollback: "none: opens an independent recovery copy", preview: false, effects: "opens a new document"),
        W("inventor_new_document_safe", None, rollback: "close the new document", preview: false, effects: "creates a document in the host workspace"),
        W("inventor_open_document_safe", None, rollback: "close the document", preview: false, effects: "opens a workspace document"),
        W("inventor_activate_document_safe", None, rollback: "activate the previous document", preview: false, effects: "changes the active document"),
        W("inventor_save_document_safe", PAD, rollback: "none: saved in place inside the workspace", preview: false, effects: "writes the workspace file"),
        W("inventor_close_document_safe", PAD, rollback: "reopen from the workspace", preview: false, effects: "closes a document"),

        // ---- Production: planning over the verified batch ----
        new() { Name = "inventor_plan_change", Access = "plan", RequiresRevision = true, Previewable = true, Documents = PAD,
            Validators = new[] { "catalogue", "caller validate[]" }, Rollback = "always: the preview transaction is aborted and the revision restored",
            ExternalEffects = "none on CAD; stores a plan in server memory (TTL)", Verification = "L1/L2 over the verified atomic batch" },
        new() { Name = "inventor_commit_plan", Access = "write", RequiresRevision = true, Previewable = false, Documents = PAD,
            Validators = new[] { "plan hash", "revision", "caller validate[]" }, Rollback = Txn,
            ExternalEffects = "modifies the target document only", Verification = "L1/L2 over the verified atomic batch" },
        Meta("inventor_list_plans"),

        // ---- Experimental: XR / visualization ----
        XQ("inventor_get_display_mesh", PA, "GLB asset: inventor://assets/{id} and GET /assets/{id}"),
        XQ("inventor_get_flat_pattern_mesh", Pa, "GLB asset of the flat pattern (no face ids) + flat_pattern_identity"),
        XQ("inventor_get_scene_graph", PA, "JSON scene graph + optional composed GLB asset"),
        XQ("inventor_get_visual_revision", PAD),
        new() { Name = "inventor_inspect_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = PA, Verification = Pending },
        new() { Name = "inventor_get_design_context_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = Pa, Verification = Pending },
        new() { Name = "inventor_get_assembly_context_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = As, Verification = Pending },
        new() { Name = "inventor_check_interference_xr", Tier = Experimental, Access = "query", RequiresRevision = true, Documents = As, Verification = Pending },
        new() { Name = "inventor_history_xr", Tier = Experimental, Access = "write", RequiresRevision = true, Documents = PA,
            Validators = new[] { "owner", "revision", "history ticket", "native transaction identity" },
            Rollback = "none: native Undo/Redo; uncertain outcomes invalidate the history chain", Verification = Pending },
        V("inventor_activate_open_document_xr", PA),
        V("inventor_highlight_entity", PAD), V("inventor_focus_entity", PAD),
        XQ("inventor_get_camera", PAD), V("inventor_set_camera", PAD),
        XQ("inventor_raycast_entity", PA), XQ("inventor_pick_entity", As),

        // ---- Experimental: inspection and intelligence ----
        XQ("inventor_get_sketch_info", Pa), XQ("inventor_get_dependencies", PA), XQ("inventor_trace_dependency", PA),
        XQ("inventor_get_semantic_state", PA), XQ("inventor_get_representations", PA),
        XQ("inventor_get_assembly_health", As), XQ("inventor_validate_drawing", Dr),
        new() { Name = "inventor_sample_parameter_motion", Tier = Experimental, Access = "plan", RequiresRevision = true, Previewable = true,
            Documents = PA, Validators = new[] { "rebuild", "interference", "min_clearance" },
            Rollback = "always: every sample runs inside one aborted transaction and the revision is restored",
            ExternalEffects = "none", Verification = Pending },
        new() { Name = "inventor_build_release_package", Tier = Experimental, Access = "artifact", RequiresRevision = true, Previewable = false,
            Documents = PAD, Validators = new[] { "source state", "BOM", "checksums" },
            Rollback = "none: filesystem output in a new host-owned folder; never overwrites",
            ExternalEffects = "writes a new release folder", Verification = Pending, Remote = "folder listing + JSON/CSV assets" },
    };

    private static readonly Dictionary<string, Contract> ByName = All.ToDictionary(c => c.Name, StringComparer.Ordinal);

    public static IReadOnlyList<Contract> Contracts => All;

    public static Contract? Find(string? name) => name != null && ByName.TryGetValue(name, out var c) ? c : null;

    public static IEnumerable<string> NamesIn(string tier) => All.Where(c => c.Tier == tier).Select(c => c.Name);
}
