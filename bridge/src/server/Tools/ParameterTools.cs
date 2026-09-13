using System;
using System.ComponentModel;
using System.Linq;
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
            return ex.ToErrorJson().ToString(Formatting.Indented);
        }
    }

    [McpServerTool(Name = "inventor_atomic_batch"), Description("Execute up to 32 typed part-modeling operations in one reversible transaction. Requires document_id and revision from inventor://active-document. Validates rebuild/feature health before commit. preview=true executes then rolls back, leaving the revision untouched; it is not a read-only operation. Each operation has command (wire name, e.g. set_parameter) and arguments (a JSON object). The command names and their arguments are listed in the inventor://batch-commands resource. No scripting, file export or document lifecycle allowed. Inventor SO 2027 only.")]
    public Task<string> AtomicBatch(string document_id, string expected_revision, AtomicOperation[] operations, bool preview = false, CancellationToken ct = default)
    {
        if (!TryBuildOperations(operations, out var wire, out var rejection))
            return Task.FromResult(rejection!.ToString(Formatting.Indented));
        return Call("atomic_batch", new JObject { ["document_id"] = document_id, ["expected_revision"] = expected_revision,
            ["operations"] = wire, ["preview"] = preview }, ct);
    }

    /// <summary>
    /// Build the wire operations from the host-bound tool arguments, refusing a malformed step by
    /// index and name instead of forwarding a step the add-in would only reject mid-transaction.
    /// </summary>
    internal static bool TryBuildOperations(AtomicOperation[]? operations, out JArray wire, out JObject? rejection)
    {
        wire = new JArray();
        rejection = null;
        foreach (var (operation, i) in (operations ?? System.Array.Empty<AtomicOperation>()).Select((o, i) => (o, i)))
        {
            var step = operation ?? new AtomicOperation();
            if (!TryReadArguments(step.Arguments, out var arguments, out var why))
            {
                rejection = new JObject
                {
                    ["ok"] = false,
                    ["error"] = new JObject
                    {
                        ["code"] = "INVALID_ARGUMENT",
                        ["message"] = "operation " + i + " (" +
                            (string.IsNullOrWhiteSpace(step.Command) ? "no command" : step.Command) + "): " + why,
                        ["details"] = new JObject { ["step_index"] = i, ["command"] = step.Command },
                    },
                };
                return false;
            }
            wire.Add(new JObject { ["command"] = step.Command, ["arguments"] = arguments });
        }
        return true;
    }

    /// <summary>
    /// Convert one step's arguments to Newtonsoft's tree over the raw JSON text. The MCP host binds
    /// them with System.Text.Json, and a <c>JsonElement</c> handed to Newtonsoft serializes as its own
    /// public surface ({"ValueKind": N}), which silently strips every value the CAD handler needs.
    /// </summary>
    private static bool TryReadArguments(System.Text.Json.JsonElement? raw, out JObject arguments, out string rejection)
    {
        arguments = new JObject();
        rejection = "";
        if (raw == null || raw.Value.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null)
            return true; // an argument-less command; the add-in validates what each command needs.
        var element = raw.Value;
        // Clients that cannot nest an object in a tool call send the object as a JSON string; accept
        // that spelling rather than failing a plan for a transport detail.
        string text = element.ValueKind == System.Text.Json.JsonValueKind.String
            ? element.GetString() ?? ""
            : element.GetRawText();
        if (element.ValueKind is not (System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.String))
        {
            rejection = "arguments must be a JSON object, not " + element.ValueKind.ToString().ToLowerInvariant() + ".";
            return false;
        }
        try
        {
            var parsed = JToken.Parse(text);
            if (parsed is not JObject obj)
            {
                rejection = "arguments must be a JSON object.";
                return false;
            }
            arguments = obj;
            return true;
        }
        catch (JsonException ex)
        {
            rejection = "arguments must be a JSON object: " + ex.Message;
            return false;
        }
    }
}

/// <summary>One typed operation of <c>inventor_atomic_batch</c>.</summary>
public sealed class AtomicOperation
{
    [Description("Wire command name, e.g. set_parameter. See the inventor://batch-commands resource.")]
    [JsonProperty("command")]
    [System.Text.Json.Serialization.JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [Description("Arguments for that command as a JSON object, e.g. {\"name\": \"length\", \"value\": \"25 mm\"}.")]
    [JsonProperty("arguments")]
    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public System.Text.Json.JsonElement? Arguments { get; set; }
}
