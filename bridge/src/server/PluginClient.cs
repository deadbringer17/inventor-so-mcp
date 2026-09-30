using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

public sealed class InventorGatewayException : Exception
{
    public string Code { get; }

    /// <summary>
    /// Machine-readable specifics from the add-in, when it sent any (the failing step index and
    /// command of a batch, for example). Carried through so a caller can branch on the failure
    /// instead of parsing the message.
    /// </summary>
    public JObject? Details { get; }

    public InventorGatewayException(string code, string message, JObject? details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    /// <summary>The failure as the tool-level error envelope every tool returns.</summary>
    public JObject ToErrorJson()
    {
        var error = new JObject { ["code"] = Code, ["message"] = Message };
        if (Details != null) error["details"] = Details;
        return new JObject { ["ok"] = false, ["error"] = error };
    }
}

/// <summary>
/// Server-side transport client. Discovers live add-in targets via <see cref="TargetRegistry"/>
/// and sends NDJSON command envelopes over the per-target transport: TCP for Inventor 2022-2024,
/// Named Pipe for 2025-2027. Ported from nwd-mcp's PluginClient, adapted to branch on
/// <see cref="TargetDescriptor.Transport"/>.
/// </summary>
public sealed class PluginClient
{
    private readonly InventorMcpConfig _config;
    private readonly TargetRegistry _registry;
    private TargetDescriptor? _current;
    private string? _selectedTargetId;
    private bool _pinExplicit;
    private readonly object _selectionLock = new();

    private readonly Action<string> _log;

    public PluginClient(InventorMcpConfig config, Action<string>? log = null)
    {
        _config = config;
        _log = log ?? (line => Console.Error.WriteLine(line)); // stderr only: stdio MCP owns stdout
        _registry = new TargetRegistry(config.DescriptorDirectory);
    }

    public IReadOnlyList<TargetDescriptor> ListTargets() => _registry.List();

    /// <summary>
    /// The pinned target, refreshed from the registry on every read. An explicit pin (SwitchTarget, or a config
    /// TargetId equal to an exact target id) never moves: if that instance disappears the result is null. An
    /// automatic pin (config year / pipe / pid alias, or the single-live-instance rule) is re-resolved with the same
    /// rule when its instance is gone, so a long-lived host follows an Inventor restart; zero or several matches
    /// stay null (fail-closed).
    /// </summary>
    public TargetDescriptor? CurrentTarget
    {
        get
        {
            var live = _registry.List();
            lock (_selectionLock)
            {
                if (_selectedTargetId is { } selected)
                {
                    var still = live.FirstOrDefault(t => t.TargetId == selected);
                    if (still is not null || _pinExplicit) return _current = still;
                    // Automatic pin whose instance is gone: fall through and re-resolve by the original rule.
                }
                var previous = _selectedTargetId;
                TargetDescriptor? resolved;
                bool explicitPin = false;
                if (!string.IsNullOrWhiteSpace(_config.TargetId))
                {
                    var key = _config.TargetId!;
                    resolved = ResolveUnique(live, key);
                    explicitPin = resolved is not null &&
                        string.Equals(resolved.TargetId, key.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                else
                    resolved = live.Count == 1 ? live[0] : null;

                _current = resolved;
                if (resolved is null) return null; // keep the old automatic pin id: a later restart may still re-pin
                _selectedTargetId = resolved.TargetId;
                _pinExplicit = explicitPin;
                if (previous is not null && previous != resolved.TargetId)
                    _log($"inventor-so-mcp: re-pinned Inventor target from {previous} to {resolved.TargetId} (previous instance is gone)");
                return resolved;
            }
        }
    }

    public bool SwitchTarget(string targetId)
    {
        var key = (targetId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;

        var live = _registry.List();
        var match = ResolveUnique(live, key);
        if (match is null) return false;
        lock (_selectionLock)
        {
            _current = match;
            _selectedTargetId = match.TargetId;
            _pinExplicit = true;
        }
        return true;
    }

    private static TargetDescriptor? ResolveUnique(IReadOnlyList<TargetDescriptor> live, string key)
    {
        var exact = live.Where(t => string.Equals(t.TargetId, key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length != 0) return exact.Length == 1 ? exact[0] : null;
        var matches = live.Where(t => string.Equals(t.PipeName, key, StringComparison.OrdinalIgnoreCase) ||
            (int.TryParse(key, out var numeric) && (numeric is >= 2022 and <= 2027
                ? t.InventorYear == numeric : t.ProcessId == numeric))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public async Task<JToken> SendAsync(string command, object parameters, CancellationToken ct)
    {
        var target = CurrentTarget ?? throw new InventorGatewayException(
            InventorErrorCodes.NO_TARGET,
            "No unique live Inventor target. Load the Inventor SO add-in and explicitly select an instance when several are available.");

        var env = new InventorCommandEnvelope
        {
            Id = Guid.NewGuid(),
            Command = command,
            Params = parameters as JObject ?? JObject.FromObject(parameters),
            TimeoutMs = _config.TimeoutMs,
            AuthToken = target.AuthToken,
            ReadOnly = _config.ReadOnly
        };

        var line = JsonConvert.SerializeObject(env) + "\n";
        var response = await SendLineAsync(target, line, ct);

        var result = JsonConvert.DeserializeObject<InventorCommandResult>(response)
                     ?? throw new InventorGatewayException(InventorErrorCodes.API_ERROR, "unparseable response");
        if (!result.Ok)
            throw new InventorGatewayException(result.Error?.Code ?? InventorErrorCodes.API_ERROR,
                result.Error?.Message ?? "unknown error", result.Error?.Details);
        return result.Data ?? JValue.CreateNull();
    }

    private async Task<string> SendLineAsync(TargetDescriptor target, string line, CancellationToken ct)
    {
        Stream stream;
        IDisposable owner;

        if (string.Equals(target.Transport, "pipe", StringComparison.OrdinalIgnoreCase))
        {
            var pipe = new NamedPipeClientStream(".", target.PipeName ?? "", PipeDirection.InOut);
            owner = pipe;
            try
            {
                var connect = pipe.ConnectAsync(ct);
                if (await Task.WhenAny(connect, Task.Delay(_config.TimeoutMs, ct)) != connect)
                    throw new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"connect to target {target.TargetId} timed out");
                await connect;
            }
            catch (InventorGatewayException) { pipe.Dispose(); throw; }
            catch (Exception ex)
            {
                pipe.Dispose();
                throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, $"cannot reach target {target.TargetId}: {ex.Message}");
            }
            stream = pipe;
        }
        else // default: tcp
        {
            var client = new TcpClient();
            owner = client;
            try
            {
                var connect = client.ConnectAsync("127.0.0.1", target.Port);
                if (await Task.WhenAny(connect, Task.Delay(_config.TimeoutMs, ct)) != connect)
                    throw new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"connect to target {target.TargetId} timed out");
                await connect;
            }
            catch (InventorGatewayException) { client.Dispose(); throw; }
            catch (Exception ex)
            {
                client.Dispose();
                throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, $"cannot reach target {target.TargetId}: {ex.Message}");
            }
            stream = client.GetStream();
        }

        using (owner)
        using (stream)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line), ct);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var readTask = NdjsonLineReader.ReadLineBoundedAsync(reader, _config.MaxResponseBytes);
            if (await Task.WhenAny(readTask, Task.Delay(_config.TimeoutMs, ct)) != readTask)
                throw new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"request {target.TargetId} timed out after {_config.TimeoutMs} ms");
            var read = await readTask;
            if (read.Overflow)
                throw new InventorGatewayException(InventorErrorCodes.RESPONSE_TOO_LARGE, $"add-in response exceeded {_config.MaxResponseBytes} bytes");
            return read.Line ?? throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, "add-in closed the connection");
        }
    }
}
