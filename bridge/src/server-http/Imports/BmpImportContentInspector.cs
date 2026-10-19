using System.Buffers.Binary;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Imports;

/// <summary>
/// Validates a deliberately bounded BMP subset: BM file header, 40-byte BITMAPINFOHEADER,
/// BI_RGB, 24-bit BGR or 32-bit BGRX, one plane, no palette/masks/gap/trailing data.
/// Both top-down and bottom-up rows are accepted; padding and the X byte have no pixel constraints.
/// Does not decode, render, infer physical dimensions from DPI, invoke CAD or certify native import.
/// Header and stride rules: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader
/// File header: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapfileheader
/// </summary>
public sealed class BmpImportContentInspector : IImportContentInspector
{
    public int MaxWidth { get; }
    public int MaxHeight { get; }
    public long MaxPixels { get; }

    /// <summary>Caller supplies measured deployment budgets; byte limits remain in HTTP/store policy.</summary>
    public BmpImportContentInspector(int maxWidth, int maxHeight, long maxPixels)
    {
        if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxHeight < 1) throw new ArgumentOutOfRangeException(nameof(maxHeight));
        if (maxPixels < 1) throw new ArgumentOutOfRangeException(nameof(maxPixels));
        MaxWidth = maxWidth; MaxHeight = maxHeight; MaxPixels = maxPixels;
    }

    public Task<JObject> InspectAsync(string format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(format, "bmp", StringComparison.Ordinal))
            throw new ArgumentException("This inspector accepts only the bmp format token.", nameof(format));
        ReadOnlySpan<byte> data = content.Span;
        if (data.Length < 54) throw Invalid("BMP headers are incomplete.");
        if (U16(data, 0) != 0x4d42 || U16(data, 6) != 0 || U16(data, 8) != 0)
            throw Invalid("BMP signature or reserved file-header fields are invalid.");
        if (U32(data, 2) != (uint)data.Length) throw Invalid("BMP declared file size differs from the supplied bytes.");
        // Reject other valid BMP variants explicitly rather than interpreting their headers as this subset.
        if (U32(data, 14) != 40 || U32(data, 10) != 54)
            throw Invalid("Only BITMAPINFOHEADER with immediate pixel data is supported; extended headers, palettes and gaps are unsupported.");
        int width = I32(data, 18), signedHeight = I32(data, 22);
        if (width < 1 || signedHeight == 0 || signedHeight == int.MinValue)
            throw Invalid("BMP dimensions must have positive representable magnitudes.");
        int height = Math.Abs(signedHeight);
        if (width > MaxWidth || height > MaxHeight || (long)width * height > MaxPixels)
            throw Invalid("BMP dimensions exceed the configured pixel budget.");
        ushort bits = U16(data, 28);
        if (U16(data, 26) != 1 || bits is not (24 or 32) || U32(data, 30) != 0)
            throw Invalid("Only single-plane uncompressed 24-bit BGR or 32-bit BGRX BMP is supported.");
        if (U32(data, 46) != 0 || U32(data, 50) != 0)
            throw Invalid("BMP color tables and important-color declarations are unsupported.");
        // Widen before multiplication and guard the image-size product before evaluating it.
        long stride = (((long)width * bits + 31) / 32) * 4;
        if (stride > long.MaxValue / height) throw Invalid("BMP padded image size exceeds the supported numeric range.");
        long pixelBytes = stride * height;
        uint declaredPixelBytes = U32(data, 34);
        if (declaredPixelBytes != 0 && declaredPixelBytes != pixelBytes)
            throw Invalid("BMP image size does not match its padded row layout.");
        // The full pixel array, including row padding, must exist exactly once and end at EOF.
        // Every BGR/BGRX byte value is legal; no pixel-sized allocation or decompressor is needed.
        if (pixelBytes != (long)data.Length - 54)
            throw Invalid("BMP pixel data is truncated, overlong or outside the file range.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new JObject
        {
            ["kind"] = "Image", ["dependencies"] = new JArray(),
            ["image"] = new JObject { ["pixel_width"] = width, ["pixel_height"] = height },
            ["geometry"] = null
        });
    }

    private static ArgumentException Invalid(string message) => new(message, "content");
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
}
