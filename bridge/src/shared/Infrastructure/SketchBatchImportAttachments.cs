using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Batch-owned sealed staging leases. No native objects, files or persistent links.</summary>
public sealed class SketchBatchImportAttachments : IDisposable
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, SketchImportStagingStore.Lease> _leases;
    private bool _disposed;
    private SketchBatchImportAttachments(Dictionary<string, SketchImportStagingStore.Lease> leases,
        Dictionary<string, Resource> resources)
    { _leases = leases; Resources = new ReadOnlyDictionary<string, Resource>(resources); }

    public IReadOnlyDictionary<string, Resource> Resources { get; }
    public byte[] Read(string assetId)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SketchBatchImportAttachments));
            if (!_leases.TryGetValue(assetId, out var lease)) throw Missing();
            return lease.ReadBytes();
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var lease in _leases.Values) lease.Dispose();
        }
    }

    public sealed class Resource
    {
        private readonly JObject _metadata;
        internal Resource(string assetId, string hash, int size, JObject metadata)
        { AssetId = assetId; Sha256 = hash; ByteLength = size; _metadata = (JObject)metadata.DeepClone(); }
        public string AssetId { get; }
        public string Sha256 { get; }
        public int ByteLength { get; }
        public string Format => "bmp";
        public JObject Metadata => (JObject)_metadata.DeepClone();
    }

    public static SketchBatchImportAttachments? Resolve(SketchImportStagingStore? store,
        JToken? descriptor, JArray operations, bool allowExperimental, Func<bool>? deadline = null)
    {
        CheckDeadline(deadline);
        if (operations == null) throw Invalid();
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in operations)
        {
            CheckDeadline(deadline);
            if (!(token is JObject step) || step["command"]?.Type != JTokenType.String || (string?)step["command"] != "edit_sketch"
                || !(step["arguments"] is JObject arguments) || arguments["variant"]?.Type != JTokenType.String || (string?)arguments["variant"] != "insert_image") continue;
            if (!Only(arguments, "sketch_id", "variant", "options") || !Text(arguments["sketch_id"])
                || !(arguments["options"] is JObject options)
                || !Only(options, "asset_id", "position_mm", "width_mm", "quarter_turns", "link")
                || !AssetId(options["asset_id"]) || !(options["position_mm"] is JArray point) || point.Count != 2
                || point.Any(v => !Finite(v)) || !Finite(options["width_mm"]) || (double)options["width_mm"]! <= 0
                || !Integer(options["quarter_turns"], 0, 3) || options["link"]?.Type != JTokenType.Boolean)
                throw Invalid();
            required.Add((string)options["asset_id"]!);
        }
        if (required.Count == 0)
        {
            if (descriptor != null && descriptor.Type != JTokenType.Null) throw Invalid();
            return null;
        }
        if (!allowExperimental) throw new CodedFailureException(InventorErrorCodes.EXPERIMENTAL_DISABLED, "Experimental image imports are disabled.");
        if (!(descriptor is JObject root) || !Only(root, "owner", "session", "resources")
            || !Text(root["owner"]) || !Text(root["session"]) || !(root["resources"] is JArray entries)
            || entries.Count != required.Count) throw Invalid();
        var declarations = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var token in entries)
        {
            CheckDeadline(deadline);
            if (!(token is JObject entry) || !Only(entry, "asset_id", "transfer_id", "sha256", "byte_length", "format", "metadata")
                || !AssetId(entry["asset_id"]) || !TransferId(entry["transfer_id"])
                || !Digest(entry["sha256"]) || (string?)entry["asset_id"] != "a_" + (string?)entry["sha256"]
                || !Integer(entry["byte_length"], 1, int.MaxValue) || entry["format"]?.Type != JTokenType.String
                || (string?)entry["format"] != "bmp" || !(entry["metadata"] is JObject metadata) || !ImageMetadata(metadata))
                throw Invalid();
            string id = (string)entry["asset_id"]!;
            if (!required.Contains(id) || declarations.ContainsKey(id)) throw Invalid();
            declarations.Add(id, (JObject)entry.DeepClone());
        }
        if (store == null) throw Missing();
        string owner = (string)root["owner"]!, session = (string)root["session"]!;
        var leases = new Dictionary<string, SketchImportStagingStore.Lease>(StringComparer.Ordinal);
        var resources = new Dictionary<string, Resource>(StringComparer.Ordinal);
        try
        {
            foreach (var pair in declarations)
            {
                CheckDeadline(deadline);
                SketchImportStagingStore.Lease? lease;
                try { lease = store.Acquire(owner, session, (string)pair.Value["transfer_id"]!); }
                catch (ArgumentException) { throw Invalid(); }
                catch (InvalidOperationException) { throw Invalid(); }
                if (lease == null) throw Missing();
                leases.Add(pair.Key, lease);
                if (lease.Sha256 != (string?)pair.Value["sha256"] || lease.Size != (int)pair.Value["byte_length"]!) throw Invalid();
                resources.Add(pair.Key, new Resource(pair.Key, lease.Sha256, lease.Size, (JObject)pair.Value["metadata"]!));
            }
            CheckDeadline(deadline);
            return new SketchBatchImportAttachments(leases, resources);
        }
        catch { foreach (var lease in leases.Values) lease.Dispose(); throw; }
    }

    private static bool Only(JObject value, params string[] fields) => value.Properties().All(p => fields.Contains(p.Name, StringComparer.Ordinal));
    private static bool Text(JToken? value) => value?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)value);
    private static bool Digest(JToken? value)
    {
        string? text = value?.Type == JTokenType.String ? (string?)value : null;
        return text != null && text.Length == 64 && text.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
    }
    private static bool AssetId(JToken? value)
    {
        string? text = value?.Type == JTokenType.String ? (string?)value : null;
        return text != null && text.StartsWith("a_", StringComparison.Ordinal) && Digest(new JValue(text.Substring(2)));
    }
    private static bool TransferId(JToken? value)
    {
        string? text = value?.Type == JTokenType.String ? (string?)value : null;
        return text != null && text.Length == 35 && text.StartsWith("st_", StringComparison.Ordinal)
            && text.Substring(3).All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
    }
    private static bool Finite(JToken? value)
    {
        if (value?.Type != JTokenType.Integer && value?.Type != JTokenType.Float) return false;
        double number;
        return double.TryParse(value.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
    }
    private static bool Integer(JToken? value, long min, long max) => value?.Type == JTokenType.Integer
        && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) && number >= min && number <= max;
    private static bool ImageMetadata(JObject metadata) => Only(metadata, "kind", "dependencies", "image", "geometry")
        && metadata["kind"]?.Type == JTokenType.String && (string?)metadata["kind"] == "Image"
        && metadata["dependencies"] is JArray dependencies && dependencies.Count == 0
        && (metadata["geometry"] == null || metadata["geometry"]!.Type == JTokenType.Null)
        && metadata["image"] is JObject image && Only(image, "pixel_width", "pixel_height")
        && Integer(image["pixel_width"], 1, int.MaxValue) && Integer(image["pixel_height"], 1, int.MaxValue);
    private static void CheckDeadline(Func<bool>? deadline)
    { if (deadline?.Invoke() == true) throw CodedFailureException.Timeout("Image import deadline was exceeded."); }
    private static CodedFailureException Invalid() => new CodedFailureException(InventorErrorCodes.INVALID_ARGUMENT, "Image import attachments or arguments are invalid.");
    private static CodedFailureException Missing() => new CodedFailureException(InventorErrorCodes.ASSET_NOT_FOUND, "Image import transfer is unavailable.");
}
