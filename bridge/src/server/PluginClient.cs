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

    /// <summary>
    /// How long a non-empty registry listing is reused. Every tool call and every event poll resolves the
    /// current target; enumerating and deserializing the descriptor folder (and probing each pid) each time is
    /// wasted work. An empty listing is never cached, so a freshly started Inventor is seen at once.
    /// </summary>
    internal static readonly TimeSpan TargetCacheTtl = TimeSpan.FromSeconds(1);

    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _cacheTtl;
    private readonly object _cacheLock = new();
    private IReadOnlyList<TargetDescriptor>? _cachedLive;
    private DateTimeOffset _cachedAt;

    public PluginClient(InventorMcpConfig config, Action<string>? log = null)
        : this(config, log, null, null) { }

    internal PluginClient(InventorMcpConfig config, Action<string>? log, Func<DateTimeOffset>? clock, TimeSpan? cacheTtl)
    {
        _config = config;
        _log = log ?? (line => Console.Error.WriteLine(line)); // stderr only: stdio MCP owns stdout
        _registry = new TargetRegistry(config.DescriptorDirectory);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _cacheTtl = cacheTtl ?? TargetCacheTtl;
    }

    /// <summary>Always reads the registry (inventor_list_available_targets, pairing) and refreshes the cache.</summary>
    public IReadOnlyList<TargetDescriptor> ListTargets() => LiveTargets(fresh: true);

    private IReadOnlyList<TargetDescriptor> LiveTargets(bool fresh)
    {
        lock (_cacheLock)
        {
            var now = _clock();
            if (!fresh && _cachedLive is { } cached && now - _cachedAt < _cacheTtl && now >= _cachedAt) return cached;
            var live = _registry.List();
            _cachedLive = live.Count == 0 ? null : live;
            _cachedAt = now;
            return live;
        }
    }

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
            var live = LiveTargets(fresh: false);
            // A pinned instance missing from the cached listing may just have appeared or been replaced: look again.
            if (_selectedTargetId is { } pinned && !live.Any(t => t.TargetId == pinned)) live = LiveTargets(fresh: true);
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

        var live = LiveTargets(fresh: true);
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

    /// <summary>
    /// Extra time the client waits beyond <see cref="InventorMcpConfig.TimeoutMs"/> for the response. The
    /// add-in measures the same deadline from a later start, so without this a command could commit after
    /// the client gave up; with it the add-in's own structured TIMEOUT normally arrives first.
    /// </summary>
    internal const int AddInGraceMs = 3000;

    /// <summary>Wait for <paramref name="task"/> up to <paramref name="timeoutMs"/>: caller cancellation throws
    /// <see cref="OperationCanceledException"/>, the deadline throws <paramref name="onTimeout"/>'s exception. No timer outlives the call.</summary>
    private static async Task<T> WithDeadlineAsync<T>(Func<CancellationToken, Task<T>> start, int timeoutMs, CancellationToken ct, Func<InventorGatewayException> onTimeout)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeoutMs);
        try { return await start(linked.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && linked.IsCancellationRequested) { throw onTimeout(); }
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
                await WithDeadlineAsync(async t => { await pipe.ConnectAsync(t); return 0; },
                    _config.TimeoutMs, ct, () => ConnectTimeout(target));
            }
            catch (InventorGatewayException) { pipe.Dispose(); throw; }
            catch (OperationCanceledException) { pipe.Dispose(); throw; }
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
                await WithDeadlineAsync(async t => { await client.ConnectAsync("127.0.0.1", target.Port, t); return 0; },
                    _config.TimeoutMs, ct, () => ConnectTimeout(target));
            }
            catch (InventorGatewayException) { client.Dispose(); throw; }
            catch (OperationCanceledException) { client.Dispose(); throw; }
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
            var read = await WithDeadlineAsync(t =>
                {
                    // The bounded reader takes no token: abandon it on deadline/cancel (disposing the stream ends it)
                    // and observe whatever it throws afterwards.
                    var pending = NdjsonLineReader.ReadLineBoundedAsync(reader, _config.MaxResponseBytes);
                    _ = pending.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    return pending.WaitAsync(t);
                },
                _config.TimeoutMs + AddInGraceMs, ct,
                () => new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"request {target.TargetId} timed out after {_config.TimeoutMs} ms"));
            if (read.Overflow)
                throw new InventorGatewayException(InventorErrorCodes.RESPONSE_TOO_LARGE, $"add-in response exceeded {_config.MaxResponseBytes} bytes");
            return read.Line ?? throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, "add-in closed the connection");
        }
    }

    private static InventorGatewayException ConnectTimeout(TargetDescriptor target) =>
        new(InventorErrorCodes.TIMEOUT, $"connect to target {target.TargetId} timed out");
}
