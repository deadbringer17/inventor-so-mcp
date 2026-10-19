using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Assets;

/// <summary>Serializes bounded native image download/PNG publication without storing document metadata in deduplicated assets.</summary>
public sealed class SketchImageAssets
{
    private const int MaxSide = 8192;
    private const long MaxPixels = (SketchImageDownload.MaxImageBytes - 54L) / 4;
    private readonly PluginClient _client;
    private readonly AssetStore _assets;
    private readonly InventorMcpConfig _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SketchImageAssets(PluginClient client, AssetStore assets, InventorMcpConfig config)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<JObject> GetAsync(string owner, string documentId, string revision, string sketchId, string imageId, CancellationToken ct = default)
    {
        if (!_config.EnableExperimental) throw new InventorGatewayException(InventorErrorCodes.EXPERIMENTAL_DISABLED, "Experimental sketch image queries are disabled.");
        foreach (string value in new[] { owner, documentId, revision, sketchId, imageId })
            if (string.IsNullOrWhiteSpace(value)) throw new InventorGatewayException(InventorErrorCodes.INVALID_ARGUMENT, "Sketch image identity is required.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        byte[]? png = null;
        try
        {
            using var image = await SketchImageDownload.DownloadNativeAsync(_client, owner, documentId, revision, sketchId, imageId, ct).ConfigureAwait(false);
            if (image.PixelWidth > MaxSide || image.PixelHeight > MaxSide || (long)image.PixelWidth * image.PixelHeight > MaxPixels)
                throw new InventorGatewayException(InventorErrorCodes.INVALID_ARGUMENT, "Sketch image dimensions exceed the PNG budget.");
            int output = (int)Math.Min(SketchImageDownload.MaxImageBytes, _assets.MaxBytes);
            var encoder = new BmpPngEncoder(SketchImageDownload.MaxImageBytes, MaxSide, MaxSide, MaxPixels,
                output, 2L * output + MaxSide * 4L + 14);
            try { png = await encoder.RenderAsync(image.Bytes, ct).ConfigureAwait(false); }
            catch (ArgumentException) { throw new InventorGatewayException(InventorErrorCodes.INVALID_ARGUMENT, "Sketch image exceeds the PNG encoding budget."); }
            await image.RecheckRevisionAsync(_client, documentId, revision, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            // Byte-addressed metadata is shared when identical PNGs are published by different owners.
            // Document identity and native frame belong only in this authenticated query response.
            AssetRecord asset;
            try
            {
                asset = _assets.Put(png, "image/png", owner, new JObject
                { ["kind"] = "Image", ["pixel_width"] = image.PixelWidth, ["pixel_height"] = image.PixelHeight });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new InventorGatewayException(InventorErrorCodes.API_ERROR, "Sketch image asset could not be published."); }
            var assetJson = asset.ToJson(_config.PublicBaseUrl);
            // Resolve on the authenticated paired endpoint, independent of a configured public alias.
            assetJson["asset_url"] = "/assets/" + asset.Id;
            return new JObject
            {
                ["document_id"] = documentId, ["revision"] = revision, ["sketch_id"] = sketchId, ["image_id"] = imageId,
                ["source_kind"] = image.SourceKind, ["pixel_width"] = image.PixelWidth, ["pixel_height"] = image.PixelHeight,
                ["width_mm"] = image.WidthMm, ["height_mm"] = image.HeightMm, ["visible"] = image.Visible,
                ["transparent"] = image.Transparent, ["image_frame"] = image.Frame, ["asset"] = assetJson
            };
        }
        finally { if (png != null) CryptographicOperations.ZeroMemory(png); _gate.Release(); }
    }
}
