using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using Bimwright.Ipt.Server.Imports;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>
/// Owner-bound image resource resolution shared by planning and direct batch ingress.
/// Does not enable native import or provide persistent linked-file storage. Callers must
/// freeze operations before resolution and use that same snapshot for preview and commit.
/// </summary>
public sealed class SketchImportAttachments : IDisposable
{
    private readonly ImportResourceStore.Lease _lease;
    private readonly object _targetGate = new();
    private PluginClient.PinnedTarget? _target;
    private SketchImportAttachments(ImportResourceStore.Lease lease) { _lease = lease; }

    /// <summary>Lease-frozen metadata and content hashes; records never own the lease.</summary>
    public IReadOnlyList<ImportResourceRecord> Resources => _lease.Records;
    /// <summary>Internal staging bytes, never public operation arguments; returns a defensive copy.</summary>
    public byte[] Read(string assetId) => _lease.Read(assetId);
    public void Dispose() => _lease.Dispose();

    // A planned image must return to the exact instance/endpoint used for its preview.
    internal PluginClient.PinnedTarget PinTarget(PluginClient client)
    {
        lock (_targetGate) return _target ??= client.CapturePinnedTarget();
    }

    public static SketchImportAttachments? Resolve(ImportResourceStore? store, string owner,
        JArray operations, CancellationToken ct = default)
    {
        if (operations == null) throw new ArgumentNullException(nameof(operations));
        ct.ThrowIfCancellationRequested();
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in operations)
        {
            ct.ThrowIfCancellationRequested();
            if (token is not JObject step || step["command"]?.Type != JTokenType.String || (string?)step["command"] != "edit_sketch"
                || step["arguments"] is not JObject arguments || arguments["variant"]?.Type != JTokenType.String
                || (string?)arguments["variant"] != "insert_image") continue;
            if (arguments.Properties().Any(p => p.Name is not ("sketch_id" or "variant" or "options"))
                || arguments["sketch_id"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)arguments["sketch_id"])
                || arguments["options"] is not JObject options)
                throw Invalid();
            if (options.Properties().Any(p => p.Name is not ("asset_id" or "position_mm" or "width_mm" or "quarter_turns" or "link"))
                || options["asset_id"]?.Type != JTokenType.String || !ImportResourceStore.IsValidId((string?)options["asset_id"])
                || options["position_mm"] is not JArray point || point.Count != 2 || point.Any(v => !Finite(v))
                || !Finite(options["width_mm"]) || (double)options["width_mm"]! <= 0
                || !IntegerBetween(options["quarter_turns"], 0, 3)
                || options["link"]?.Type != JTokenType.Boolean)
                throw Invalid();
            string id = (string)options["asset_id"]!;
            if (seen.Add(id)) ids.Add(id);
        }
        if (ids.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(owner)) throw Invalid();
        if (store == null) throw Missing();
        ImportResourceStore.Lease? lease = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            try { lease = store.Acquire(owner, ids); }
            catch (ArgumentException) { throw Invalid(); }
            if (lease == null) throw Missing();
            foreach (var record in lease.Records)
            {
                ct.ThrowIfCancellationRequested();
                var metadata = record.Metadata;
                if (record.Format != "bmp" || metadata["kind"]?.Type != JTokenType.String || (string?)metadata["kind"] != "Image"
                    || metadata.Properties().Any(p => p.Name is not ("kind" or "dependencies" or "image" or "geometry"))
                    || metadata["dependencies"] is not JArray dependencies || dependencies.Count != 0
                    || metadata["geometry"] is { Type: not JTokenType.Null }
                    || metadata["image"] is not JObject image
                    || image.Properties().Any(p => p.Name is not ("pixel_width" or "pixel_height"))
                    || !PixelCount(image["pixel_width"]) || !PixelCount(image["pixel_height"]))
                    throw Invalid();
            }
            ct.ThrowIfCancellationRequested();
            var resolved = new SketchImportAttachments(lease);
            lease = null;
            return resolved;
        }
        finally { lease?.Dispose(); }
    }

    private static bool Finite(JToken? value) => value?.Type is JTokenType.Integer or JTokenType.Float
        && double.IsFinite((double)value);
    private static bool PixelCount(JToken? value) => IntegerBetween(value, 1, int.MaxValue);
    private static bool IntegerBetween(JToken? value, long minimum, long maximum) => value?.Type == JTokenType.Integer
        && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
        && number >= minimum && number <= maximum;
    private static PlanException Invalid() => new(InventorErrorCodes.INVALID_ARGUMENT,
        "Image import arguments or inspected resource metadata are invalid.");
    private static PlanException Missing() => new(InventorErrorCodes.ASSET_NOT_FOUND,
        "Unknown, expired or foreign import resource.");
}
