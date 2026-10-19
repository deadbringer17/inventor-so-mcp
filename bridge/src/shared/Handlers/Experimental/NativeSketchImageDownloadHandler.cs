#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// Internal bounded read transport for immutable managed native image bytes. The process-owned
/// download store is separate from upload staging. Every request binds owner and user session to
/// the exact active document, revision, sketch and image; no native object or path is serialized.
/// This is the original managed payload, not evidence that a desktop image replacement or flip
/// has left Inventor's displayed bitmap unchanged. Explicit native_bitmap mode copies the current
/// opaque native bitmap together with its empirical native boundary-role UV frame. This does not
/// verify alpha or physical display equivalence. Linked images remain unsupported.
/// </summary>
public sealed class NativeSketchImageDownloadHandler : ExperimentalHandler
{
    public const int MaxChunkBytes = 192 * 1024;
    public override string Name => "get_sketch_image_payload";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        string action = Text(p, "action");
        var fields = new HashSet<string>(StringComparer.Ordinal)
        { "action", "document_id", "expected_revision", "sketch_id", "image_id", "owner", "session", "source_mode" };
        switch (action)
        {
            case "begin": break;
            case "chunk": fields.Add("transfer_id"); fields.Add("offset"); fields.Add("count"); break;
            case "discard": fields.Add("transfer_id"); break;
            default: throw Invalid();
        }
        foreach (var property in p.Properties()) if (!fields.Contains(property.Name)) throw Invalid();
        foreach (string field in fields) if (field != "source_mode" && p[field] == null) throw Invalid();
        string sourceMode = p["source_mode"] == null ? "managed_original" : Text(p, "source_mode");
        if (sourceMode != "managed_original" && sourceMode != "native_bitmap") throw Invalid();
        string owner = Identity(p, "owner"), userSession = Identity(p, "session");
        string documentId = Text(p, "document_id"), revision = Text(p, "expected_revision");
        string sketchId = Text(p, "sketch_id"), imageId = Text(p, "image_id");
        string? transferId = action == "begin" ? null : Identity(p, "transfer_id");
        var store = ctx.SketchImageDownloads
            ?? throw new CodedFailureException(InventorErrorCodes.API_ERROR, "Native image download storage is unavailable.");
        // A JSON array avoids delimiter collisions; every component is compared ordinally.
        byte[] binding = Encoding.UTF8.GetBytes(new JArray(userSession, documentId, revision, sketchId, imageId, sourceMode).ToString(Formatting.None));
        string session;
        try { session = Convert.ToHexString(SHA256.HashData(binding)).ToLowerInvariant(); }
        finally { Array.Clear(binding, 0, binding.Length); }
        bool completed = false, retireTransfer = false;
        try
        {
            var part = X.ActivePart(app, Name);
            var doc = (global::Inventor.Document)part;
            void Recheck()
            {
                X.Deadline(ctx, "reading the managed native image payload");
                var active = app.ActiveDocument;
                string? actualId = active == null ? null : EntityReferences.DocumentId(active);
                if (active == null || actualId != documentId || !SketchReferences.SameComObject(active, doc))
                    throw ConcurrencyFailure.DocumentChanged(documentId, actualId);
                string? actualRevision = ctx.Events?.Revision(documentId);
                if (actualRevision != revision) throw ConcurrencyFailure.StaleRevision(revision, actualRevision);
            }
            Recheck();
            var sketch = SketchReferences.Resolve(doc, sketchId, "sketch") as PlanarSketch
                ?? throw Invalid();
            var image = SketchReferences.Resolve(doc, imageId, "sketch_image") as SketchImage
                ?? throw Invalid();
            if (!SketchReferences.SameComObject(image.Parent, sketch) || image.LinkedToFile) throw Invalid();
            if (action == "begin")
            {
                Recheck(); // Includes the deadline immediately before native byte read/hash.
                NativeSketchImagePixels.Record record;
                if (sourceMode == "native_bitmap") record = NativeSketchImagePixels.Read(ctx, app, image);
                else
                {
                    NativeSketchImagePayload.Record managed = NativeSketchImagePayload.Read(image) ?? throw Missing();
                    try { record = new NativeSketchImagePixels.Record(managed.Bytes, managed.Sha256, managed.PixelWidth, managed.PixelHeight); }
                    catch { Array.Clear(managed.Bytes, 0, managed.Bytes.Length); throw; }
                }
                string? id = null;
                bool delivered = false;
                try
                {
                    Recheck();
                    JObject? currentFrame = null;
                    bool visible = false, transparent = false;
                    if (sourceMode == "native_bitmap")
                    {
                        currentFrame = NativeSketchImageLiveFrame.CaptureCurrent(image);
                        visible = image.Visible;
                        transparent = image.Transparent;
                        if (transparent) throw Invalid();
                        Recheck(); // Refuse before staging if the frame or its identity changed.
                    }
                    id = store.Begin(owner, session, record.ByteLength);
                    for (int offset = 0; offset < record.ByteLength;)
                    {
                        X.Deadline(ctx, "staging the managed native image download");
                        int count = Math.Min(MaxChunkBytes, record.ByteLength - offset);
                        byte[] chunk = new byte[count];
                        try
                        {
                            Buffer.BlockCopy(record.Bytes, offset, chunk, 0, count);
                            store.Append(owner, session, id, offset, chunk);
                        }
                        finally { Array.Clear(chunk, 0, chunk.Length); }
                        offset += count;
                    }
                    Recheck();
                    store.Seal(owner, session, id, record.Sha256);
                    Recheck();
                    var result = new JObject { ["transfer_id"] = id, ["sha256"] = record.Sha256,
                        ["byte_length"] = record.ByteLength, ["format"] = "bmp",
                        ["pixel_width"] = record.PixelWidth, ["pixel_height"] = record.PixelHeight,
                        ["max_chunk_bytes"] = MaxChunkBytes, ["source_mode"] = sourceMode };
                    if (currentFrame != null)
                    {
                        result["image_frame"] = currentFrame["frame"]!.DeepClone();
                        result["width_mm"] = currentFrame["width_mm"];
                        result["height_mm"] = currentFrame["height_mm"];
                        result["visible"] = visible;
                        result["transparent"] = transparent;
                    }
                    Recheck();
                    delivered = true;
                    completed = true;
                    return result;
                }
                finally
                {
                    record.Dispose();
                    if (!delivered && id != null)
                        try { store.Discard(owner, session, id); }
                        catch { /* Preserve the original refusal; process-owned TTL bounds retention. */ }
                }
            }
            if (action == "discard")
            {
                bool discarded = store.Discard(owner, session, transferId!);
                Recheck();
                completed = true;
                return new JObject { ["discarded"] = discarded };
            }
            int chunkOffset = Integer(p, "offset"), chunkCount = Integer(p, "count");
            if (chunkOffset < 0 || chunkCount < 1 || chunkCount > MaxChunkBytes) throw Invalid();
            using var lease = store.Acquire(owner, session, transferId!) ?? throw Missing();
            byte[] bytes = lease.ReadChunk(chunkOffset, chunkCount);
            try
            {
                var result = new JObject { ["transfer_id"] = transferId, ["offset"] = chunkOffset,
                    ["byte_length"] = lease.Size, ["sha256"] = lease.Sha256,
                    ["data_base64"] = Convert.ToBase64String(bytes) };
                Recheck();
                completed = true;
                return result;
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        catch (CodedFailureException failure)
        {
            retireTransfer = failure.Code == InventorErrorCodes.TIMEOUT
                || failure.Code == InventorErrorCodes.STALE_REVISION
                || failure.Code == InventorErrorCodes.DOCUMENT_CHANGED;
            throw;
        }
        catch (KeyNotFoundException) { throw Missing(); }
        catch (ArgumentException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        finally
        {
            // Also retire a bound transfer when a deadline, revision or document guard refuses
            // before Acquire. A foreign owner/session cannot retire another caller's transfer.
            if (!completed && retireTransfer && transferId != null)
                try { store.Discard(owner, session, transferId); }
                catch { /* Preserve the primary refusal; process-owned TTL still applies. */ }
        }
    }

    private static string Text(JObject p, string name)
    {
        if (p[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)p[name])) throw Invalid();
        return (string)p[name]!;
    }
    private static string Identity(JObject p, string name)
    {
        string value = Text(p, name);
        if (value.Length > 256) throw Invalid();
        return value;
    }
    private static int Integer(JObject p, string name)
    {
        if (p[name]?.Type != JTokenType.Integer || !int.TryParse(p[name]!.ToString(Formatting.None),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) throw Invalid();
        return value;
    }
    private static CodedFailureException Invalid() => new(InventorErrorCodes.INVALID_ARGUMENT,
        "Native image download arguments or staging limits are invalid.");
    private static CodedFailureException Missing() => new(InventorErrorCodes.ASSET_NOT_FOUND,
        "The managed image payload or scoped download transfer is unavailable.");
}
#endif
