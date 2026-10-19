using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Ipt.Server.Assets;

/// <summary>
/// Encodes canonical BMP BI_RGB/DIB40 24-bit BGR or 32-bit BGRX as static RGBA8 PNG.
/// The unused X byte is ignored and alpha is always 255; no color-space or DPI inference.
/// Working-buffer budget includes renderer-owned arrays and maximum final copy, excluding
/// caller input, CLR object overhead and framework/native zlib workspace. No full decoded
/// image or compressed intermediate array is allocated. PNG rules: https://www.w3.org/TR/png-3/
/// </summary>
public sealed class BmpPngEncoder
{
    public int MaxInputBytes { get; }
    public int MaxWidth { get; }
    public int MaxHeight { get; }
    public long MaxPixels { get; }
    public int MaxOutputBytes { get; }
    public long MaxWorkingBufferBytes { get; }

    public BmpPngEncoder(int maxInputBytes, int maxWidth, int maxHeight, long maxPixels,
        int maxOutputBytes, long maxWorkingBufferBytes)
    {
        if (maxInputBytes < 1 || maxInputBytes > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxHeight < 1) throw new ArgumentOutOfRangeException(nameof(maxHeight));
        if (maxPixels < 1) throw new ArgumentOutOfRangeException(nameof(maxPixels));
        if (maxOutputBytes < 1 || maxOutputBytes > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));
        if (maxWorkingBufferBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxWorkingBufferBytes));
        MaxInputBytes = maxInputBytes; MaxWidth = maxWidth; MaxHeight = maxHeight; MaxPixels = maxPixels;
        MaxOutputBytes = maxOutputBytes; MaxWorkingBufferBytes = maxWorkingBufferBytes;
    }

    public Task<byte[]> RenderAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(content.Span);
        return Task.FromResult(RenderValidated(content.Span, cancellationToken));
    }

    private void Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length < 54 || data.Length > MaxInputBytes) throw Invalid();
        if (U16(data, 0) != 0x4d42 || U32(data, 2) != data.Length || U32(data, 6) != 0
            || U32(data, 10) != 54 || U32(data, 14) != 40) throw Invalid();
        int width = I32(data, 18), signedHeight = I32(data, 22);
        if (width < 1 || signedHeight == 0 || signedHeight == int.MinValue) throw Invalid();
        int height = Math.Abs(signedHeight);
        if (width > MaxWidth || height > MaxHeight || (long)width * height > MaxPixels) throw Invalid();
        ushort bits = U16(data, 28);
        if (U16(data, 26) != 1 || bits is not (24 or 32) || U32(data, 30) != 0
            || U32(data, 46) != 0 || U32(data, 50) != 0) throw Invalid();
        long stride = (((long)width * bits + 31) / 32) * 4;
        // Division bounds the product before evaluating it, including extreme declared dimensions.
        if (stride > (data.Length - 54L) / height || stride * height != data.Length - 54L
            || U32(data, 34) != 0 && U32(data, 34) != stride * height) throw Invalid();
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    private static ArgumentException Invalid() => new("BMP content is invalid or exceeds the configured budget.", "content");
    private byte[] RenderValidated(ReadOnlySpan<byte> input, CancellationToken cancellationToken)
    {
        int width = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(18, 4));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(22, 4));
        int height = Math.Abs(signedHeight);
        int bytesPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(28, 2)) / 8;
        long stride = (((long)width * bytesPerPixel + 3) / 4) * 4;
        long rowLength = (long)width * 4 + 1;
        if (rowLength > Array.MaxLength || stride > int.MaxValue || stride > (input.Length - 54L) / height)
            throw new ArgumentException("BMP row layout exceeds renderer array ranges.", nameof(input));
        // Reserve the maximum possible final copy as well as the capacity of the encoding buffer.
        // Wide arithmetic prevents budget checks wrapping before any large array allocation.
        long required = 2L * MaxOutputBytes + rowLength + 13;
        if (required > MaxWorkingBufferBytes)
            throw new ArgumentException("PNG working arrays exceed the configured memory budget.", nameof(input));
        cancellationToken.ThrowIfCancellationRequested();
        using var png = new PngOutput(MaxOutputBytes, cancellationToken);
        byte[] row = new byte[(int)rowLength];
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8; header[9] = 6; // RGBA8, deflate, filter method 0, no interlacing.
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        png.Append(signature);
        png.Chunk("IHDR"u8, header);
        using (var idat = new IdatOutput(png))
        using (var compressor = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sourceY = signedHeight < 0 ? y : height - 1 - y;
                long sourceOffset = 54L + sourceY * stride;
                if (sourceOffset < 54 || sourceOffset + stride > input.Length)
                    throw new ArgumentException("BMP source row lies outside the input.", nameof(input));
                var source = input.Slice((int)sourceOffset, (int)stride);
                row[0] = 0; // No filter; top-to-bottom RGBA rows.
                for (int x = 0, s = 0, d = 1; x < width; x++, s += bytesPerPixel, d += 4)
                {
                    if ((x & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    row[d] = source[s + 2]; row[d + 1] = source[s + 1]; row[d + 2] = source[s]; row[d + 3] = 255;
                }
                compressor.Write(row);
            }
        } // zlib trailer is also encoded through the bounded IDAT sink.
        cancellationToken.ThrowIfCancellationRequested();
        png.Chunk("IEND"u8, ReadOnlySpan<byte>.Empty);
        return png.Finish();
    }

    private sealed class PngOutput : IDisposable
    {
        private readonly byte[] _bytes;
        private readonly CancellationToken _cancellation;
        private int _length;
        internal PngOutput(int maxBytes, CancellationToken cancellation) { _bytes = new byte[maxBytes]; _cancellation = cancellation; }
        private void Reserve(int count)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (count < 0 || count > _bytes.Length - _length)
                throw new ArgumentException("PNG output exceeds the configured byte budget.");
        }
        internal void Append(ReadOnlySpan<byte> bytes)
        { Reserve(bytes.Length); bytes.CopyTo(_bytes.AsSpan(_length)); _length += bytes.Length; }
        internal void Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            if (type.Length != 4 || data.Length > int.MaxValue - 12) throw new ArgumentException("Invalid PNG chunk length.");
            Reserve(data.Length + 12); // Bound before appending any portion of this chunk.
            Span<byte> field = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(field, (uint)data.Length); Append(field); Append(type); Append(data);
            uint crc = UpdateCrc(uint.MaxValue, type);
            crc = UpdateCrc(crc, data) ^ uint.MaxValue;
            BinaryPrimitives.WriteUInt32BigEndian(field, crc); Append(field);
        }
        private uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if ((i & 4095) == 0) _cancellation.ThrowIfCancellationRequested();
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
            return crc;
        }
        internal byte[] Finish()
        { _cancellation.ThrowIfCancellationRequested(); return _bytes.AsSpan(0, _length).ToArray(); }
        public void Dispose() { }
    }

    // Each ZLib write becomes one consecutive IDAT chunk; the zlib stream itself is continuous.
    private sealed class IdatOutput : Stream
    {
        private readonly PngOutput _png;
        internal IdatOutput(PngOutput png) => _png = png;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { if (!buffer.IsEmpty) _png.Chunk("IDAT"u8, buffer); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

