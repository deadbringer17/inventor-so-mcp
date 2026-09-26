using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Tests;

public sealed class TierPolicyTests
{
    private static readonly string[] PreviousProduction =
    {
        "inventor_list_available_targets", "inventor_get_current_target", "inventor_switch_target",
        "inventor_health", "inventor_list_open_documents", "inventor_get_document_info",
        "inventor_get_selection", "inventor_resolve_entity", "inventor_plan_native_package",
        "inventor_get_sheet_metal_info", "inventor_list_topology",
        "inventor_list_parameters", "inventor_get_parameter", "inventor_get_iproperty", "inventor_get_mass_properties",
        "inventor_list_interfaces", "inventor_check_interference", "inventor_measure_min_distance",
        "inventor_get_assembly_bom", "inventor_list_constraints", "inventor_atomic_batch", "inventor_save_artifact", "inventor_move_component_safe", "inventor_edit_constraint_safe", "inventor_create_constraint_safe", "inventor_create_joint_safe", "inventor_ground_component_safe", "inventor_insert_component_safe",
        "inventor_new_document_safe", "inventor_open_document_safe", "inventor_activate_document_safe", "inventor_save_document_safe", "inventor_close_document_safe", "inventor_list_workspace_documents",
        "inventor_checkpoint_create", "inventor_checkpoint_list", "inventor_checkpoint_restore", "inventor_diff_checkpoint", "inventor_create_drawing_safe", "inventor_list_drawing_templates"
    };

    [Fact]
    public void EveryPreviouslyProductionToolStaysProduction()
    {
        Assert.Equal(40, PreviousProduction.Length);
        foreach (var name in PreviousProduction)
            Assert.Equal(ToolContracts.Production, SoToolPolicy.TierOf(name));
    }

    [Theory]
    [InlineData("inventor_get_capabilities")]
    [InlineData("inventor_get_tool_schema")]
    [InlineData("inventor_plan_change")]
    [InlineData("inventor_commit_plan")]
    [InlineData("inventor_validate_bom")]
    [InlineData("inventor_compare_bom")]
    public void NewServerSideToolsAreProduction(string name) => Assert.True(SoToolPolicy.IsExposed(name));

    [Theory]
    [InlineData("inventor_get_display_mesh")]
    [InlineData("inventor_get_scene_graph")]
    [InlineData("inventor_set_camera")]
    [InlineData("inventor_get_sketch_info")]
    [InlineData("inventor_sample_parameter_motion")]
    [InlineData("inventor_build_release_package")]
    public void ExperimentalToolsNeedTheOptIn(string name)
    {
        Assert.False(SoToolPolicy.IsExposed(name));
        Assert.True(SoToolPolicy.IsExposed(name, fullAccess: false, experimental: true));
        Assert.Equal(ToolContracts.Experimental, SoToolPolicy.TierOf(name));
    }

    [Fact]
    public void ExperimentalOptInNeverExposesUnreviewedTools()
    {
        foreach (var name in new[] { "inventor_set_parameter", "inventor_send_code", "inventor_export_step", "inventor_run_baked_tool" })
            Assert.False(SoToolPolicy.IsExposed(name, fullAccess: false, experimental: true));
    }

    [Fact]
    public void EveryContractNamesARegisteredTool()
    {
        var registered = Program.ResolveToolTypesForRegistration(new InventorMcpConfig { Toolsets = { "all" }, EnableSendCode = true })
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n != null).ToHashSet();
        foreach (var contract in ToolContracts.Contracts)
            Assert.True(registered.Contains(contract.Name), "contract without a tool: " + contract.Name);
    }

    [Fact]
    public void ContractsAreUniqueAndWritesRequireRevision()
    {
        Assert.Equal(ToolContracts.Contracts.Count, ToolContracts.Contracts.Select(c => c.Name).Distinct().Count());
        foreach (var c in ToolContracts.Contracts.Where(c => c.Access == "write" && c.Documents.Length > 0 && c.Name.EndsWith("_safe") == false))
            Assert.True(c.RequiresRevision, c.Name);
    }

    [Fact]
    public void ReadOnlyDropsPlanningButKeepsXrQueries()
    {
        var cfg = new InventorMcpConfig { ReadOnly = true };
        var types = Program.ResolveToolTypesForRegistration(cfg);
        Assert.Contains(typeof(Bimwright.Ipt.Server.Tools.XrTools), types);
        Assert.DoesNotContain(typeof(Bimwright.Ipt.Server.Tools.PlanningTools), types);
    }

    [Fact]
    public void ExperimentalFlagLoadsFromCliAndEnvironment()
    {
        Assert.True(InventorMcpConfig.Load(new[] { "--enable-experimental" }).EnableExperimental);
        Assert.False(InventorMcpConfig.Load(System.Array.Empty<string>()).EnableExperimental);
    }
}
