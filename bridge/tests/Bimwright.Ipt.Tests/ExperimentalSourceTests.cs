using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// L1 source checks for the experimental add-in tier, which cannot be compiled here (no Inventor
/// interop): the catalogue, the handlers, the registrar and the server tools must agree, and the
/// default build must not see any of it.
/// </summary>
public sealed class ExperimentalSourceTests
{
    private static string Src(params string[] parts)
    {
        for (var d = new DirectoryInfo(System.AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src", "shared");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(d.FullName, "src", "server")))
                return Path.Combine(new[] { d.FullName, "src" }.Concat(parts).ToArray());
        }
        throw new DirectoryNotFoundException("bridge/src not found");
    }

    private static readonly Regex HandlerName = new("Name => \"([a-z_]+)\"");
    private static readonly Regex ClassName = new(@"public sealed class (\w+Handler)\b");

    private static string[] ExperimentalFiles() => Directory.GetFiles(Src("shared", "Handlers", "Experimental"), "*.cs");

    private static string[] ExperimentalWireNames() => ExperimentalFiles()
        .SelectMany(f => HandlerName.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToArray();

    [Fact]
    public void EveryExperimentalFileIsCompiledOnlyInTheExperimentalBuild()
    {
        foreach (var file in ExperimentalFiles().Append(Src("shared", "Plugin", "InventorCommandRegistry.Experimental.cs"))
                     .Append(Src("shared", "Plugin", "SelectionEventTracker.cs")))
            Assert.StartsWith("#if INVENTOR2027 && SO_EXPERIMENTAL", File.ReadAllText(file).TrimStart('﻿'));
    }

    [Fact]
    public void EveryExperimentalCatalogueCommandHasAHandler()
    {
        var names = ExperimentalWireNames().ToHashSet();
        foreach (var command in CadBatchCommandCatalog.ExperimentalNames)
            Assert.True(names.Contains(command), "no experimental handler for batch command " + command);
    }

    [Fact]
    public void EveryExperimentalHandlerIsRegisteredOnce()
    {
        var registrar = File.ReadAllText(Src("shared", "Plugin", "InventorCommandRegistry.Experimental.cs"));
        var classes = ExperimentalFiles().SelectMany(f => ClassName.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToArray();
        Assert.NotEmpty(classes);
        foreach (var name in classes)
            Assert.Equal(1, Regex.Matches(registrar, @"add\(new " + name + @"\(\)\)").Count);
        Assert.Equal(ExperimentalWireNames().Length, ExperimentalWireNames().Distinct().Count());
    }

    [Fact]
    public void ExperimentalWireNamesNeverCollideWithVerifiedHandlers()
    {
        var verified = Directory.GetFiles(Src("shared", "Handlers"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Experimental" + Path.DirectorySeparatorChar))
            .SelectMany(f => HandlerName.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToHashSet();
        foreach (var name in ExperimentalWireNames())
            Assert.False(verified.Contains(name), name + " is already a verified wire command: the registrar would throw at start-up");
    }

    [Fact]
    public void StableCatalogueCommandsAreNotImplementedByExperimentalHandlers()
    {
        var experimental = ExperimentalWireNames().ToHashSet();
        foreach (var command in CadBatchCommandCatalog.Names)
            Assert.False(experimental.Contains(command), command);
    }

    [Theory]
    [InlineData("inventor_get_display_mesh", "get_display_mesh")]
    [InlineData("inventor_get_flat_pattern_mesh", "get_flat_pattern_mesh")]
    [InlineData("inventor_history_xr", "history_xr")]
    [InlineData("inventor_get_scene_graph", "get_scene_graph")]
    [InlineData("inventor_get_visual_revision", "get_visual_revision")]
    [InlineData("inventor_highlight_entity", "highlight_entity")]
    [InlineData("inventor_focus_entity", "focus_entity")]
    [InlineData("inventor_get_camera", "get_camera")]
    [InlineData("inventor_set_camera", "set_camera")]
    [InlineData("inventor_raycast_entity", "raycast_entity")]
    [InlineData("inventor_pick_entity", "pick_entity")]
    [InlineData("inventor_inspect_xr", "inspect_xr")]
    [InlineData("inventor_get_design_context_xr", "get_design_context_xr")]
    [InlineData("inventor_get_assembly_context_xr", "get_assembly_context_xr")]
    [InlineData("inventor_activate_open_document_xr", "activate_open_document_xr")]
    [InlineData("inventor_check_interference_xr", "check_interference_xr")]
    [InlineData("inventor_measure_min_distance_xr", "measure_min_distance_xr")]
    [InlineData("inventor_assembly_health_xr", "assembly_health_xr")]
    [InlineData("inventor_get_sketch_info", "get_sketch_info")]
    [InlineData("inventor_get_dependencies", "get_dependencies")]
    [InlineData("inventor_trace_dependency", "get_dependencies")]
    [InlineData("inventor_get_semantic_state", "get_semantic_state")]
    [InlineData("inventor_get_representations", "get_representations")]
    [InlineData("inventor_get_assembly_health", "get_assembly_health")]
    [InlineData("inventor_validate_drawing", "validate_drawing")]
    [InlineData("inventor_sample_parameter_motion", "sample_parameter_motion")]
    public void ExperimentalToolsReachAnExperimentalHandler(string tool, string command)
    {
        Assert.Equal(ToolContracts.Experimental, SoToolPolicy.TierOf(tool));
        Assert.Contains(command, ExperimentalWireNames());
        var serverSources = string.Join("\n", Directory.GetFiles(Src("server", "Tools"), "*.cs").Select(File.ReadAllText));
        Assert.Contains("\"" + command + "\"", serverSources);
    }

    [Fact]
    public void EveryExperimentalToolIsCovered()
    {
        var covered = typeof(ExperimentalSourceTests).GetMethod(nameof(ExperimentalToolsReachAnExperimentalHandler))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false).Cast<InlineDataAttribute>()
            .Select(a => (string)a.GetData(null!).First()[0]).ToHashSet();
        // The release package is composed server-side from verified exports; it has no handler of its own.
        foreach (var tool in SoToolPolicy.Experimental.Where(t => t != "inventor_build_release_package"))
            Assert.True(covered.Contains(tool), tool + " is experimental but not mapped to a handler in this test");
    }

    [Fact]
    public void TheDefaultAddInBuildLeavesTheExperimentalTierOut()
    {
        var csproj = File.ReadAllText(Src("plugin-so27", "Inventor.So.AddIn.csproj"));
        Assert.Contains("<SoExperimental Condition=\"'$(SoExperimental)' == ''\">false</SoExperimental>", csproj);
        Assert.Contains("SO_EXPERIMENTAL", csproj);
    }
}
