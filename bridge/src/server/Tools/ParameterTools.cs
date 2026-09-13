using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Model parameter tools (toolset <c>parameters</c>). Lists/reads/sets user and model parameters of
/// the active part document, and creates new user parameters. Thin wrappers over wire commands.
/// </summary>
[McpServerToolType]
public sealed class ParameterTools
{
    private readonly PluginClient _client;
    public ParameterTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_parameters"),
     Description("List a part document's parameters (model + user): name, expression, evaluated value, unit, and kind. Defaults to the active document; pass document_id to read any other OPEN part without activating it, which is how one part's dimension is carried into another. Inventor offers no cross-document parameter link outside iLogic or a derived part, so the propagation itself is done by writing the value into the other part with inventor_atomic_batch.")]
    public Task<string> ListParameters(string? document_id = null, CancellationToken ct = default)
        => Call("list_parameters", new JObject { ["document_id"] = document_id }, ct);

    [McpServerTool(Name = "inventor_get_parameter"),
     Description("Get a single parameter of the active part document by name: expression, evaluated value, and unit.")]
    public Task<string> GetParameter(string name, CancellationToken ct = default)
        => Call("get_parameter", new JObject { ["name"] = name }, ct);

    [McpServerTool(Name = "inventor_set_parameter"),
     Description("Set an existing parameter's expression/value by name (e.g. value=\"25 mm\" or a numeric expression). Then updates the document.")]
    public Task<string> SetParameter(string name, string value, CancellationToken ct = default)
        => Call("set_parameter", new JObject { ["name"] = name, ["value"] = value }, ct);

    [McpServerTool(Name = "inventor_create_parameter"),
     Description("Create a new user parameter on the active part document: name, expression, and unit (e.g. unit=mm, expression=\"10\").")]
    public Task<string> CreateParameter(string name, string expression, string unit, CancellationToken ct = default)
        => Call("create_parameter", new JObject { ["name"] = name, ["expression"] = expression, ["unit"] = unit }, ct);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }

    [McpServerTool(Name = "inventor_atomic_batch"), Description("Execute up to 32 typed part-modeling operations in one reversible transaction. Requires document_id and revision from inventor://active-document. Validates rebuild/feature health before commit. preview=true executes then rolls back; it is not a read-only operation. Each operation has command (wire name, e.g. set_parameter) and arguments. No scripting, file export or document lifecycle allowed. Inventor SO 2027 only.")]
    public Task<string> AtomicBatch(string document_id, string expected_revision, System.Text.Json.JsonElement operations, bool preview = false, CancellationToken ct = default)
        => Call("atomic_batch", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision,
            ["operations"] = JToken.Parse(operations.GetRawText()), ["preview"] = preview }, ct);
}
