using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Resources;

/// <summary>Read-only snapshots. No subscription support is advertised yet.</summary>
[McpServerResourceType]
public sealed class CadResources
{
    private readonly PluginClient _client;
    private readonly InventorMcpConfig? _config;
    private readonly Bimwright.Ipt.Server.Assets.AssetStore? _assets;
    private readonly ICallerIdentity _caller;

    public CadResources(PluginClient client) : this(client, null, null, null) { }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public CadResources(PluginClient client, InventorMcpConfig? config, Bimwright.Ipt.Server.Assets.AssetStore? assets, ICallerIdentity? caller)
    {
        _client = client;
        _config = config;
        _assets = assets;
        _caller = caller ?? new StdioCallerIdentity();
    }

    [McpServerResource(UriTemplate = "inventor://application", MimeType = "application/json")]
    [Description("Selected Inventor instance. Does not expose IPC credentials. This is discovery state, not a live health check.")]
    public string Application() => new MetaTools(_client).GetCurrentTarget();

    [McpServerResource(UriTemplate = "inventor://active-document", MimeType = "application/json")]
    [Description("Live active document metadata from the selected Inventor add-in.")]
    public Task<string> ActiveDocument(CancellationToken ct) => Read("get_document_info", ct);

    [McpServerResource(UriTemplate = "inventor://active-document/parameters", MimeType = "application/json")]
    [Description("Live parameters of the active document. Document-specific persistent URIs are not available yet.")]
    public Task<string> Parameters(CancellationToken ct) => Read("list_parameters", ct);

    [McpServerResource(UriTemplate = "inventor://active-document/mass", MimeType = "application/json")]
    [Description("Mass and physical properties of the active document; units are specified by the add-in response.")]
    public Task<string> Mass(CancellationToken ct) => Read("get_mass_properties", ct);

    [McpServerResource(UriTemplate = "inventor://selection", MimeType = "application/json")]
    [Description("Current user selection and portable entity references. Requires Inventor SO 2027.")]
    public Task<string> Selection(CancellationToken ct) => Read("get_selection", ct);

    [McpServerResource(UriTemplate = "inventor://batch-commands", MimeType = "application/json")]
    [Description("Command vocabulary of inventor_atomic_batch: every allowed wire command with its required and optional arguments. Static server-side contract, no Inventor round trip; a command absent here cannot run in a batch.")]
    public string BatchCommands() => Bimwright.Ipt.Shared.Contracts.CadBatchCommandCatalog
        .Describe(includeExperimental: _config?.EnableExperimental == true).ToString(Formatting.None);

    [McpServerResource(UriTemplate = "inventor://assets/{asset_id}", MimeType = "application/octet-stream")]
    [Description("A content-addressed asset (GLB mesh or scene, PNG, PDF, CSV, JSON) produced for this client by an XR or release tool. asset_id is the a_<sha256> id returned by that tool; only the client that produced the asset can read it, and it expires after the configured TTL (renewed on every read).")]
    public ModelContextProtocol.Protocol.ResourceContents Asset(string asset_id)
    {
        if (_assets == null || !_assets.TryRead(asset_id, _caller.Client, out var record, out var bytes))
            throw new ModelContextProtocol.McpException("ASSET_NOT_FOUND: unknown, expired or foreign asset id.");
        return ModelContextProtocol.Protocol.BlobResourceContents.FromBytes(bytes!, "inventor://assets/" + record!.Id, record.MimeType);
    }

    [McpServerResource(UriTemplate = "inventor://events", MimeType = "application/json")]
    [Description("Bounded document-event journal with epoch, cursor and resync_required flag. Snapshot only; push subscriptions are not implemented yet.")]
    public Task<string> Events(CancellationToken ct) => Read("get_events", ct);

    private async Task<string> Read(string command, CancellationToken ct)
    {
        // Propagate failures to MCP: never disguise a failed read as a valid CAD snapshot.
        var data = await _client.SendAsync(command, new JObject(), ct);
        return data.ToString(Formatting.None);
    }
}
