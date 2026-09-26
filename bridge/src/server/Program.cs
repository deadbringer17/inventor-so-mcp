using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Server.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var cfg = InventorMcpConfig.Load(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
cfg.Transport = "stdio";
builder.Services.AddInventorServices(cfg);

builder.Services
    .AddMcpServer(o => o.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .AddInventorMcp(cfg);

await builder.Build().RunAsync();

internal static partial class Program
{
    internal static IMcpServerBuilder RegisterToolsets(
        IMcpServerBuilder mcp,
        IEnumerable<Type> toolTypes,
        bool fullAccess = false,
        bool experimental = false,
        Func<string, bool>? exclude = null)
    {
        foreach (var toolType in toolTypes)
        {
            var methods = toolType.GetMethods().Where(method =>
                method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { } name &&
                SoToolPolicy.IsExposed(name, fullAccess, experimental) &&
                exclude?.Invoke(name) != true);
            mcp = mcp.WithTools(methods.Select(method => McpServerTool.Create(method,
                context => ActivatorUtilities.CreateInstance(context.Services!, toolType))));
        }

        return mcp;
    }

    private static IMcpServerBuilder RegisterToolType(IMcpServerBuilder mcp, Type toolType)
    {
        if (toolType == typeof(MetaTools)) return mcp.WithTools<MetaTools>();
        if (toolType == typeof(QueryTools)) return mcp.WithTools<QueryTools>();
        if (toolType == typeof(DocumentTools)) return mcp.WithTools<DocumentTools>();
        if (toolType == typeof(ParameterTools)) return mcp.WithTools<ParameterTools>();
        if (toolType == typeof(PropertyTools)) return mcp.WithTools<PropertyTools>();
        if (toolType == typeof(SketchTools)) return mcp.WithTools<SketchTools>();
        if (toolType == typeof(FeatureTools)) return mcp.WithTools<FeatureTools>();
        if (toolType == typeof(ExportTools)) return mcp.WithTools<ExportTools>();
        if (toolType == typeof(CodeTools)) return mcp.WithTools<CodeTools>();
        if (toolType == typeof(ToolBakerTools)) return mcp.WithTools<ToolBakerTools>();
        if (toolType == typeof(ToolBakerWriteTools)) return mcp.WithTools<ToolBakerWriteTools>();
        if (toolType == typeof(AssemblyTools)) return mcp.WithTools<AssemblyTools>();
        if (toolType == typeof(AssemblyQueryTools)) return mcp.WithTools<AssemblyQueryTools>();
        if (toolType == typeof(CapabilityTools)) return mcp.WithTools<CapabilityTools>();
        if (toolType == typeof(XrTools)) return mcp.WithTools<XrTools>();
        if (toolType == typeof(InsightTools)) return mcp.WithTools<InsightTools>();
        if (toolType == typeof(PlanningTools)) return mcp.WithTools<PlanningTools>();
        if (toolType == typeof(ReleaseTools)) return mcp.WithTools<ReleaseTools>();

        throw new InvalidOperationException("Unsupported MCP tool type: " + toolType.FullName);
    }

    internal static IReadOnlyList<Type> ResolveToolTypesForRegistration(InventorMcpConfig cfg)
    {
        var ts = ToolsetFilter.Resolve(cfg);
        var types = new List<Type>();
        void Add(string toolset, Type t)
        {
            if (ts.Contains(toolset) && !types.Contains(t)) types.Add(t);
        }

        Add("meta",            typeof(MetaTools));
        Add("meta",            typeof(CapabilityTools));
        Add("query",           typeof(QueryTools));
        Add("document",        typeof(DocumentTools));
        Add("document",        typeof(SafeDocumentTools));
        Add("parameters",      typeof(ParameterTools));
        Add("properties",      typeof(PropertyTools));
        Add("sketch",          typeof(SketchTools));
        Add("feature",         typeof(FeatureTools));
        Add("export",          typeof(ExportTools));
        Add("export",          typeof(SafeArtifactTools));
        Add("code",            typeof(CodeTools));
        Add("toolbaker",       typeof(ToolBakerTools));
        Add("toolbaker_write", typeof(ToolBakerWriteTools));
        Add("assembly",        typeof(AssemblyTools));
        Add("assembly",        typeof(SafeAssemblyTools));
        Add("assembly_query",  typeof(AssemblyQueryTools));
        Add("xr",              typeof(XrTools));
        Add("insight",         typeof(InsightTools));
        Add("planning",        typeof(PlanningTools));
        Add("export",          typeof(ReleaseTools));
        return types;
    }
}
