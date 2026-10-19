using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Inventor.So.Mcp.Http.Imports;

namespace Bimwright.Ipt.Tests;

public sealed class BmpImportPreviewRendererTests
{
    private static BmpImportPreviewRenderer Renderer(int output = 4096, long memory = 16384,
        BmpImportContentInspector? inspector = null) => new(inspector ?? new BmpImportContentInspector(100, 100, 10000), output, memory);
    private static byte[] Fixture(int width, int height, int bits, bool topDown, byte unused = 0)
    {
        int stride = ((width * (bits / 8) + 3) / 4) * 4;
        var bytes = new byte[54 + stride * height];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at, 2), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), value);
        U16(0, 0x4d42); U32(2, (uint)bytes.Length); U32(10, 54); U32(14, 40);
        U32(18, (uint)width); U32(22, unchecked((uint)(topDown ? -height : height)));
        U16(26, 1); U16(28, (ushort)bits); U32(34, (uint)(stride * height));
        Array.Fill(bytes, (byte)0xab, 54, bytes.Length - 54); // Deliberately nonzero row padding.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int storageY = topDown ? y : height - 1 - y;
            int p = 54 + storageY * stride + x * (bits / 8);
            bytes[p] = Blue(x, y); bytes[p + 1] = Green(x, y); bytes[p + 2] = Red(x, y);
            if (bits == 32) bytes[p + 3] = unused;
        }
        return bytes;
    }
    private static byte Red(int x, int y) => (byte)((x * 37 + y * 13) & 255);
    private static byte Green(int x, int y) => (byte)((x * 17 + y * 53) & 255);
    private static byte Blue(int x, int y) => (byte)((x * 71 + y * 23) & 255);

    // Independent test reader: validates framing/CRC, concatenates IDAT, inflates zlib and
    // checks full decoded length/filter/pixels. It does not call any production PNG helpers.
    private static (int Width, int Height, byte[] Pixels) ReadPng(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
        int offset = 8, width = 0, height = 0; bool ended = false; var names = new List<string>();
        using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            Assert.True(png.Length - offset >= 12);
            int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            Assert.InRange(count, 0, png.Length - offset - 12);
            string type = Encoding.ASCII.GetString(png, offset + 4, 4); names.Add(type);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + count, 4));
            Assert.Equal(Crc(png.AsSpan(offset + 4, count + 4)), crc);
            Assert.False(ended);
            if (type == "IHDR")
            {
                Assert.Single(names); Assert.Equal(13, count);
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8, 4)));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 12, 4)));
                Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, png.AsSpan(offset + 16, 5).ToArray());
            }
            else if (type == "IDAT") { Assert.True(width > 0); compressed.Write(png, offset + 8, count); }
            else if (type == "IEND") { Assert.Equal(0, count); Assert.Equal(0xae426082u, crc); ended = true; }
            else Assert.Fail("Unexpected PNG chunk: " + type);
            offset += count + 12;
        }
        Assert.True(ended); Assert.Equal("IHDR", names[0]); Assert.Equal("IEND", names[^1]);
        Assert.All(names.Skip(1).SkipLast(1), n => Assert.Equal("IDAT", n));
        Assert.True(compressed.Length > 0);
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        int rowLength = width * 4 + 1;
        byte[] decoded = new byte[rowLength * height];
        zlib.ReadExactly(decoded); Assert.Equal(-1, zlib.ReadByte());
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        { Assert.Equal(0, decoded[y * rowLength]); Array.Copy(decoded, y * rowLength + 1, pixels, y * width * 4, width * 4); }
        return (width, height, pixels);
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        // Polynomial long division (test code), independent of the renderer's incremental chunk helper.
        uint remainder = 0xffffffffu;
        foreach (byte b in bytes)
        {
            uint next = remainder ^ b;
            for (int bit = 0; bit < 8; bit++) next = (next & 1) != 0 ? 0xedb88320u ^ (next >> 1) : next >> 1;
            remainder = next;
        }
        return ~remainder;
    }

    [Theory]
    [InlineData(1, 1, 24, false)] [InlineData(1, 1, 32, true)]
    [InlineData(3, 2, 24, false)] [InlineData(3, 2, 24, true)]
    [InlineData(3, 2, 32, false)] [InlineData(3, 2, 32, true)]
    [InlineData(2, 3, 24, false)] [InlineData(4, 3, 24, true)]
    [InlineData(71, 41, 32, false)]
    public async Task EncodesExactRgbaPixelsWithCrcAndProperOrientation(int width, int height, int bits, bool topDown)
    {
        byte[] bmp = Fixture(width, height, bits, topDown, 17), original = (byte[])bmp.Clone();
        byte[] png = await Renderer(65536, 140000).RenderAsync(bmp, CancellationToken.None);
        var result = ReadPng(png); Assert.Equal(width, result.Width); Assert.Equal(height, result.Height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            Assert.Equal(new byte[] { Red(x, y), Green(x, y), Blue(x, y), 255 }, result.Pixels.AsSpan(p, 4).ToArray());
        }
        Assert.Equal(original, bmp);
    }

    [Fact]
    public async Task BgrxUnusedByteNeverBecomesAlphaAndEquivalentBmpLayoutsHaveIdenticalPng()
    {
        var renderer = Renderer();
        byte[] png = await renderer.RenderAsync(Fixture(3, 2, 24, false), CancellationToken.None);
        foreach (byte unused in new byte[] { 0, 127, 255 })
        foreach (bool topDown in new[] { false, true })
            Assert.Equal(png, await renderer.RenderAsync(Fixture(3, 2, 32, topDown, unused), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(10, 55)] [InlineData(14, 108)] [InlineData(18, 0)]
    [InlineData(22, 0)] [InlineData(26, 2)] [InlineData(28, 16)] [InlineData(30, 3)]
    [InlineData(34, 1)] [InlineData(46, 1)] [InlineData(50, 1)]
    public async Task RejectsUnvalidatedHeadersBeforeEncoding(int offset, int value)
    {
        byte[] bmp = Fixture(3, 2, 24, false); bmp[offset] = (byte)value;
        await Assert.ThrowsAsync<ArgumentException>(() => Renderer().RenderAsync(bmp, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsTruncationTrailingDataAndHugeDimensionsWithoutLargeArrays()
    {
        byte[] bmp = Fixture(3, 2, 24, false);
        foreach (int length in new[] { 0, 53, bmp.Length - 1 })
            await Assert.ThrowsAsync<ArgumentException>(() => Renderer().RenderAsync(bmp.AsMemory(0, length), CancellationToken.None));
        Array.Resize(ref bmp, bmp.Length + 1); BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2, 4), (uint)bmp.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => Renderer().RenderAsync(bmp, CancellationToken.None));
        bmp = Fixture(3, 2, 32, false);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18, 4), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22, 4), int.MaxValue);
        var inspector = new BmpImportContentInspector(int.MaxValue, int.MaxValue, long.MaxValue);
        await Assert.ThrowsAsync<ArgumentException>(() => Renderer(inspector: inspector).RenderAsync(bmp, CancellationToken.None));
    }

    [Fact]
    public async Task UsesTheSuppliedInspectorBudget()
    {
        var inspector = new BmpImportContentInspector(2, 2, 4); var renderer = Renderer(inspector: inspector);
        Assert.Same(inspector, renderer.Inspector);
        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(Fixture(3, 2, 24, false), CancellationToken.None));
    }

    [Fact]
    public async Task OutputLimitIncludesChunkOverheadAndTrailerAtExactBoundary()
    {
        byte[] bmp = Fixture(3, 2, 24, false);
        byte[] expected = await Renderer().RenderAsync(bmp, CancellationToken.None);
        Assert.Equal(expected, await Renderer(expected.Length).RenderAsync(bmp, CancellationToken.None));
        foreach (int limit in new[] { 1, 8, 33, expected.Length - 1 })
            await Assert.ThrowsAsync<ArgumentException>(() => Renderer(limit).RenderAsync(bmp, CancellationToken.None));
        Assert.Equal(expected, await Renderer().RenderAsync(bmp, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryBudgetIncludesEncodingCapacityFinalCopyRowAndHeader()
    {
        // Width3 -> filter+RGBA row13, IHDR13. Capacity4096 + final-copy reservation4096.
        const long exact = 4096L * 2 + 13 + 13;
        byte[] bmp = Fixture(3, 2, 24, false);
        var renderer = Renderer(4096, exact); Assert.Equal(exact, renderer.MaxWorkingBufferBytes); Assert.Equal(4096, renderer.MaxOutputBytes);
        ReadPng(await renderer.RenderAsync(bmp, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Renderer(4096, exact - 1).RenderAsync(bmp, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationPrecedesValidationAndNoInputOrSharedStateIsChanged()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var renderer = Renderer();
        byte[] bmp = Fixture(3, 2, 32, true), original = (byte[])bmp.Clone();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderAsync(bmp, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderAsync(ReadOnlyMemory<byte>.Empty, cancellation.Token));
        Assert.Equal(original, bmp); ReadPng(await renderer.RenderAsync(bmp, CancellationToken.None));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => renderer.RenderAsync(bmp, CancellationToken.None))));
        foreach (var result in results) Assert.Equal(results[0], result);
        Assert.Equal(original, bmp);
    }

    [Fact]
    public void ConstructorRequiresExplicitPositiveLimits()
    {
        Assert.Throws<ArgumentNullException>(() => new BmpImportPreviewRenderer(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer(memory: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer(memory: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer(int.MaxValue));
    }
}
