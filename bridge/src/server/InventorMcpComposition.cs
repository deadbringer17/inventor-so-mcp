using System;
using System.Linq;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Server.Audit;
using Bimwright.Ipt.Server.Events;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Server.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Server;

/// <summary>
/// The one composition of the Inventor SO MCP server, shared by the stdio host (this project) and the
/// remote host (Inventor.So.Mcp.Http): same services, tools, resources, policy, audit and
/// subscriptions. A host adds only its transport and, for HTTP, its own <see cref="ICallerIdentity"/>.
/// </summary>
public static class InventorMcpComposition
{
    /// <summary>Register the server's services. A host may register its own ICallerIdentity first.</summary>
    public static IServiceCollection AddInventorServices(this IServiceCollection services, InventorMcpConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton<ServerState>();
        services.AddSingleton<PluginClient>();
        services.AddSingleton<AssetStore>();
        services.AddSingleton<MeshCache>();
        services.AddSingleton<ChangePlanStore>();
        services.AddSingleton<AuditLog>();
        services.TryAddSingleton<ICallerIdentity, StdioCallerIdentity>();
        services.AddSingleton<EventSubscriptionService>();
        services.AddHostedService(sp => sp.GetRequiredService<EventSubscriptionService>());
        return services;
    }

    /// <summary>Resources, policy-filtered tools, the audit filter and resource subscriptions.</summary>
    public static IMcpServerBuilder AddInventorMcp(this IMcpServerBuilder mcp, InventorMcpConfig config)
    {
        mcp = mcp.WithResources<CadResources>();
        mcp = Program.RegisterToolsets(mcp, Program.ResolveToolTypesForRegistration(config), config.FullAccess, config.EnableExperimental,
            exclude: name => config.Transport == "http" && name == "inventor_switch_target");
        mcp = mcp.WithRequestFilters(filters => filters.AddCallToolFilter(AuditLog.Filter()));
        mcp = mcp.WithSubscribeToResourcesHandler(async (context, ct) =>
        {
            var service = context.Services!.GetRequiredService<EventSubscriptionService>();
            if (context.Params?.Uri is { } uri) service.Subscribe(context.Server, uri);
            return await new System.Threading.Tasks.ValueTask<EmptyResult>(new EmptyResult());
        });
        mcp = mcp.WithUnsubscribeFromResourcesHandler(async (context, ct) =>
        {
            var service = context.Services!.GetRequiredService<EventSubscriptionService>();
            if (context.Params?.Uri is { } uri) service.Unsubscribe(context.Server, uri);
            return await new System.Threading.Tasks.ValueTask<EmptyResult>(new EmptyResult());
        });
        return mcp;
    }
}
