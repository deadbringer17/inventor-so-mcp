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
    public CadResources(PluginClient client) => _client = client;

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
