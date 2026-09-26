using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Events;

/// <summary>
/// MCP resource subscriptions over the add-in's event journal (plan §20). While at least one session
/// is subscribed, the journal is polled with its cursor; every new event batch sends
/// <c>notifications/resources/updated</c> for the resources it affects, to the sessions subscribed
/// to them only. With no subscriber nothing is polled, so an idle server costs Inventor nothing.
/// </summary>
public sealed class EventSubscriptionService : BackgroundService
{
    public static readonly string[] SubscribableUris =
    {
        "inventor://events", "inventor://active-document", "inventor://selection",
    };

    private readonly PluginClient _client;
    private readonly ILogger<EventSubscriptionService>? _logger;
    private readonly object _gate = new();
    // Keyed by session, not by McpServer: the SDK hands every request its own destination-bound
    // wrapper (reference equality), so keying by the instance made unsubscribe a no-op (seen live).
    private readonly Dictionary<string, (McpServer Server, HashSet<string> Uris)> _subscriptions = new(StringComparer.Ordinal);
    private readonly TimeSpan _interval;
    private long _cursor;
    private string? _epoch;

    public EventSubscriptionService(PluginClient client, ILogger<EventSubscriptionService>? logger = null)
        : this(client, TimeSpan.FromSeconds(1), logger) { }

    public EventSubscriptionService(PluginClient client, TimeSpan interval, ILogger<EventSubscriptionService>? logger = null)
    {
        _client = client;
        _interval = interval;
        _logger = logger;
    }

    public int SubscriberCount { get { lock (_gate) return _subscriptions.Count; } }

    public void Subscribe(McpServer server, string uri)
    {
        if (!SubscribableUris.Contains(uri, StringComparer.Ordinal))
            throw new ModelContextProtocol.McpException("Only " + string.Join(", ", SubscribableUris) + " can be subscribed.");
        lock (_gate)
        {
            var key = SessionKey(server);
            var uris = _subscriptions.TryGetValue(key, out var entry) ? entry.Uris : new HashSet<string>(StringComparer.Ordinal);
            uris.Add(uri);
            _subscriptions[key] = (server, uris);   // the latest request's handle sends from now on
        }
    }

    public void Unsubscribe(McpServer server, string uri)
    {
        lock (_gate)
        {
            var key = SessionKey(server);
            if (_subscriptions.TryGetValue(key, out var entry) && entry.Uris.Remove(uri) && entry.Uris.Count == 0) _subscriptions.Remove(key);
        }
    }

    /// <summary>A stdio process serves one session, which may have no id; HTTP sessions always do.</summary>
    public static string SessionKey(McpServer server) => server.SessionId ?? "";

    /// <summary>Resources affected by a batch of journal events.</summary>
    public static IReadOnlyCollection<string> AffectedUris(JArray events)
    {
        var uris = new HashSet<string>(StringComparer.Ordinal);
        if (events.Count > 0) uris.Add("inventor://events");
        foreach (var e in events.OfType<JObject>())
        {
            switch ((string?)e["type"])
            {
                case "selection_changed": uris.Add("inventor://selection"); break;
                case "camera_changed": break;
                default: uris.Add("inventor://active-document"); break;
            }
        }
        return uris;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
            if (SubscriberCount == 0) continue;
            try { await PollOnce(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger?.LogDebug(ex, "event poll failed"); }
        }
    }

    /// <summary>One poll: read events after the cursor and notify the affected subscribers.</summary>
    public async Task PollOnce(CancellationToken ct)
    {
        JObject read;
        try { read = (JObject)await _client.SendAsync("get_events", new JObject { ["after"] = _cursor, ["epoch"] = _epoch }, ct); }
        catch (InventorGatewayException) { return; } // No target yet: nothing to report.
        var events = read["events"] as JArray ?? new JArray();
        bool resync = (bool?)read["resync_required"] ?? false;
        _epoch = (string?)read["epoch"];
        _cursor = (long?)read["cursor"] ?? _cursor;
        // After an add-in restart or journal overflow the whole state may have moved: tell everyone.
        var uris = resync ? SubscribableUris : AffectedUris(events);
        if (uris.Count == 0) return;
        List<(string key, McpServer server, string uri)> targets;
        lock (_gate)
            targets = _subscriptions.SelectMany(s => s.Value.Uris.Where(uris.Contains).Select(u => (s.Key, s.Value.Server, u))).ToList();
        foreach (var (key, server, uri) in targets)
        {
            try
            {
                await server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
                    new ResourceUpdatedNotificationParams { Uri = uri }, cancellationToken: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A session that cannot be notified is gone; drop all its subscriptions.
                lock (_gate) _subscriptions.Remove(key);
            }
        }
    }
}
