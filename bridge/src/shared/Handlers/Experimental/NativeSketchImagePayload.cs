#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// STA-only document persistence for the immutable original image. Write is called only for a
/// newly created image inside the caller's owned atomic transaction. No paths or credentials
/// are persisted. The native attribute follows the image through save, Undo and Redo.
/// </summary>
internal static class NativeSketchImagePayload
{
    internal const string AttributeSetName = "InventorSO_SketchImagePayload";
    internal const int MaxImageBytes = 32 * 1024 * 1024;
    internal const int MaxDocumentBytes = 64 * 1024 * 1024;
    internal const int MaxManagedImages = 32;

    /// <summary>Bytes are an owned copy, independent of the native attribute; caller clears them after use.</summary>
    internal sealed class Record
    {
        internal Record(byte[] bytes, string sha256, int pixelWidth, int pixelHeight)
        { Bytes = bytes; Sha256 = sha256; PixelWidth = pixelWidth; PixelHeight = pixelHeight; }
        internal byte[] Bytes { get; }
        internal string Sha256 { get; }
        internal string AssetId => "a_" + Sha256;
        internal int ByteLength => Bytes.Length;
        internal int PixelWidth { get; }
        internal int PixelHeight { get; }
    }

    private sealed class Header
    {
        internal AttributeSet Set = null!;
        internal string Sha = "";
        internal int Length, Width, Height;
    }

    /// <summary>Checks all our managed images, including malformed payloads, before native Add.</summary>
    internal static void AssertCapacity(PartDocument doc, int newByteLength)
    {
        if (doc == null || newByteLength < 54 || newByteLength > MaxImageBytes) throw Invalid();
        try
        {
            var headers = new List<Header>();
            long total = newByteLength;
            foreach (PlanarSketch sketch in doc.ComponentDefinition.Sketches)
                foreach (SketchImage image in sketch.SketchImages)
                {
                    Header? header = ReadHeader(image);
                    if (header == null) continue; // Native/legacy images have no managed payload.
                    headers.Add(header);
                    total += header.Length;
                    // Decide aggregate budgets without reading any native byte-array values.
                    if (headers.Count >= MaxManagedImages || total > MaxDocumentBytes) throw Invalid();
                }
            foreach (Header header in headers)
            {
                Record record = ReadBytes(header);
                Array.Clear(record.Bytes, 0, record.Bytes.Length);
            }
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw Api(error); }
    }

    internal static void Write(SketchImage image, byte[] bytes, string sha256, int pixelWidth, int pixelHeight)
    {
        if (image == null || bytes == null || bytes.Length < 54 || bytes.Length > MaxImageBytes) throw Invalid();
        // Isolate the value handed to COM from the caller's buffer, which it clears after staging.
        byte[] owned = (byte[])bytes.Clone();
        try
        {
            Validate(owned, sha256, pixelWidth, pixelHeight);
            if (FindSet(image) != null) throw Invalid(); // Never overwrite an existing managed image.
            AttributeSet set = image.AttributeSets.Add(AttributeSetName, true);
            set.Add("schema", ValueTypeEnum.kIntegerType, 1);
            set.Add("sha256", ValueTypeEnum.kStringType, sha256);
            set.Add("asset_id", ValueTypeEnum.kStringType, "a_" + sha256);
            set.Add("byte_length", ValueTypeEnum.kIntegerType, owned.Length);
            set.Add("format", ValueTypeEnum.kStringType, "bmp");
            set.Add("pixel_width", ValueTypeEnum.kIntegerType, pixelWidth);
            set.Add("pixel_height", ValueTypeEnum.kIntegerType, pixelHeight);
            set.Add("bytes", ValueTypeEnum.kByteArrayType, owned);
            Record? persisted = Read(image);
            if (persisted == null) throw Invalid();
            try
            {
                if (persisted.Sha256 != sha256 || persisted.ByteLength != owned.Length
                    || persisted.PixelWidth != pixelWidth || persisted.PixelHeight != pixelHeight
                    || !CryptographicOperations.FixedTimeEquals(owned, persisted.Bytes)) throw Invalid();
            }
            finally { Array.Clear(persisted.Bytes, 0, persisted.Bytes.Length); }
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw Api(error); }
        finally { Array.Clear(owned, 0, owned.Length); }
    }

    internal static Record? Read(SketchImage image)
    {
        if (image == null) throw Invalid();
        try
        {
            Header? header = ReadHeader(image);
            return header == null ? null : ReadBytes(header);
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw Api(error); }
    }

    // Metadata only: download performs the full byte/hash validation separately.
    internal static JObject? Describe(SketchImage image)
    {
        try
        {
            Header? header = ReadHeader(image);
            return header == null ? null : new JObject { ["schema"] = 1, ["kind"] = "managed_original",
                ["asset_id"] = "a_" + header.Sha, ["sha256"] = header.Sha,
                ["byte_length"] = header.Length, ["format"] = "bmp",
                ["pixel_width"] = header.Width, ["pixel_height"] = header.Height };
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw Api(error); }
    }

    private static AttributeSet? FindSet(SketchImage image)
    {
        AttributeSet? found = null;
        foreach (AttributeSet candidate in image.AttributeSets)
        {
            if (!string.Equals(candidate.Name, AttributeSetName, StringComparison.OrdinalIgnoreCase)) continue;
            if (found != null || !string.Equals(candidate.Name, AttributeSetName, StringComparison.Ordinal)) throw Invalid();
            found = candidate;
        }
        return found;
    }

    private static Header? ReadHeader(SketchImage image)
    {
        AttributeSet? set = FindSet(image);
        if (set == null) return null;
        if (set.Count != 8 || set.Transient) throw Invalid();
        var fields = new Dictionary<string, Inventor.Attribute>(StringComparer.Ordinal);
        foreach (Inventor.Attribute field in set)
            if (!fields.TryAdd(field.Name, field)) throw Invalid();
        Inventor.Attribute Field(string name, ValueTypeEnum type)
        {
            if (!fields.TryGetValue(name, out Inventor.Attribute? field) || field.ValueType != type) throw Invalid();
            return field;
        }
        int Integer(string name)
        {
            object value = Field(name, ValueTypeEnum.kIntegerType).Value;
            if (value is not int result) throw Invalid();
            return result;
        }
        string Text(string name)
        {
            object value = Field(name, ValueTypeEnum.kStringType).Value;
            if (value is not string result) throw Invalid();
            return result;
        }
        int length = Integer("byte_length"), width = Integer("pixel_width"), height = Integer("pixel_height");
        string sha = Text("sha256");
        if (Integer("schema") != 1 || length < 54 || length > MaxImageBytes || width <= 0 || height <= 0
            || Text("format") != "bmp" || !ValidHash(sha) || Text("asset_id") != "a_" + sha) throw Invalid();
        Field("bytes", ValueTypeEnum.kByteArrayType); // Check native type before materializing its value.
        // Even a minimal 24-bit uncompressed image must fit in the declared byte count.
        long minimumStride = (((long)width * 24 + 31) / 32) * 4;
        if (minimumStride > (length - 54L) / height) throw Invalid();
        return new Header { Set = set, Sha = sha, Length = length, Width = width, Height = height };
    }

    private static Record ReadBytes(Header header)
    {
        // Inventor exposes no byte-array length without Value; declared length was bounded first.
        // Clone the COM result so a caller clearing the returned bytes cannot mutate native data.
        if (header.Set["bytes"].Value is not byte[] native || native.Length != header.Length) throw Invalid();
        byte[] owned = (byte[])native.Clone();
        try
        {
            Validate(owned, header.Sha, header.Width, header.Height);
            return new Record(owned, header.Sha, header.Width, header.Height);
        }
        catch { Array.Clear(owned, 0, owned.Length); throw; }
    }

    private static bool ValidHash(string sha)
    {
        if (sha == null || sha.Length != 64) return false;
        foreach (char character in sha)
            if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f')) return false;
        return true;
    }

    private static void Validate(byte[] bytes, string sha, int width, int height)
    {
        if (bytes.Length < 54 || bytes.Length > MaxImageBytes || width <= 0 || height <= 0 || !ValidHash(sha)) throw Invalid();
        ReadOnlySpan<byte> b = bytes;
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(22, 4));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(28, 2));
        long stride = (((long)width * bits + 31) / 32) * 4;
        if (BinaryPrimitives.ReadUInt16LittleEndian(b) != 0x4d42 || U32(2) != bytes.Length || U32(6) != 0
            || U32(10) != 54 || U32(14) != 40 || BinaryPrimitives.ReadInt32LittleEndian(b.Slice(18, 4)) != width
            || signedHeight == int.MinValue || Math.Abs(signedHeight) != height
            || BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(26, 2)) != 1 || bits is not (24 or 32)
            || U32(30) != 0 || U32(46) != 0 || U32(50) != 0 || stride > (bytes.Length - 54L) / height
            || stride * height != bytes.Length - 54L || (U32(34) != 0 && U32(34) != stride * height)
            || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != sha) throw Invalid();
    }

    private static CodedFailureException Invalid() => new(InventorErrorCodes.INVALID_ARGUMENT,
        "The managed native image payload or its document capacity is invalid.");
    private static CodedFailureException Api(Exception error) => new(InventorErrorCodes.API_ERROR,
        "The managed native image payload could not be accessed.", inner: error);
}
#endif
