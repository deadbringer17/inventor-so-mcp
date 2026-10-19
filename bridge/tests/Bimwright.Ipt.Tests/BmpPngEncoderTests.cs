using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Bimwright.Ipt.Server.Assets;

namespace Bimwright.Ipt.Tests;

public sealed class BmpPngEncoderTests
{
    private static BmpPngEncoder Encoder(int input = 1024, int width = 10, int height = 10,
        long pixels = 100, int output = 512, long working = 2048)
        => new(input, width, height, pixels, output, working);

    private static byte[] NativeBitmap(byte x = 0)
    {
        var bytes = new byte[70];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x4d42);
        void Integer(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        Integer(2, bytes.Length); Integer(10, 54); Integer(14, 40); Integer(18, 2); Integer(22, -2);
        bytes[26] = 1; bytes[28] = 32; Integer(34, 16);
        var bgrx = new byte[] { 30, 20, 10, x, 60, 50, 40, x, 90, 80, 70, x, 120, 110, 100, x };
        bgrx.CopyTo(bytes, 54); return bytes;
    }

    // Independent consumer: extracts IDAT through PNG framing and uses framework zlib decoding.
    private static byte[] Scanlines(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
        using var compressed = new MemoryStream();
        var names = new List<string>();
        for (int offset = 8; offset < png.Length;)
        {
            int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            Assert.InRange(count, 0, png.Length - offset - 12);
            string name = Encoding.ASCII.GetString(png, offset + 4, 4); names.Add(name);
            if (name == "IHDR")
            {
                Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8, 4)));
                Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 12, 4)));
                Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, png.AsSpan(offset + 16, 5).ToArray());
            }
            if (name == "IDAT") compressed.Write(png, offset + 8, count);
            offset += count + 12;
        }
        Assert.Equal("IHDR", names.First()); Assert.Equal("IEND", names.Last());
        compressed.Position = 0;
        using var decoder = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream(); decoder.CopyTo(decoded); return decoded.ToArray();
    }

    [Fact]
    public async Task NativeTopDownBgrxEncodesExactOpaqueColorsAndPreservesInput()
    {
        byte[] bmp = NativeBitmap(17), original = (byte[])bmp.Clone();
        byte[] png = await Encoder().RenderAsync(bmp, CancellationToken.None);
        Assert.Equal(new byte[] { 0, 10, 20, 30, 255, 40, 50, 60, 255,
            0, 70, 80, 90, 255, 100, 110, 120, 255 }, Scanlines(png));
        Assert.Equal(original, bmp);
        Assert.Equal(png, await Encoder().RenderAsync(NativeBitmap(0), CancellationToken.None));
        Assert.Equal(png, await Encoder().RenderAsync(NativeBitmap(255), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(2, 69)] [InlineData(6, 1)] [InlineData(10, 55)]
    [InlineData(14, 108)] [InlineData(18, 0)] [InlineData(22, 0)] [InlineData(26, 2)]
    [InlineData(28, 8)] [InlineData(30, 1)] [InlineData(34, 1)] [InlineData(46, 1)] [InlineData(50, 1)]
    public async Task SharedEntryPointValidatesHeadersWithoutRelyingOnHttpInspector(int offset, int value)
    {
        var bytes = NativeBitmap(); bytes[offset] = (byte)value;
        await Assert.ThrowsAsync<ArgumentException>(() => Encoder().RenderAsync(bytes, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsTruncatedTrailingAndExtremeLayoutsBeforeEncoding()
    {
        byte[] bytes = NativeBitmap();
        await Assert.ThrowsAsync<ArgumentException>(() => Encoder().RenderAsync(bytes.AsMemory(0, 53), CancellationToken.None));
        Array.Resize(ref bytes, 71); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2, 4), bytes.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => Encoder().RenderAsync(bytes, CancellationToken.None));
        bytes = NativeBitmap();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), int.MaxValue);
        await Assert.ThrowsAsync<ArgumentException>(() => Encoder(width: int.MaxValue, height: int.MaxValue, pixels: long.MaxValue).RenderAsync(bytes, CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitInputDimensionsPixelsOutputAndWorkingBudgetsAreEnforced()
    {
        byte[] bytes = NativeBitmap();
        foreach (var encoder in new[]
        {
            Encoder(input: bytes.Length - 1), Encoder(width: 1), Encoder(height: 1), Encoder(pixels: 3),
            Encoder(output: 16), Encoder(working: 2 * 512 + 9 + 13 - 1)
        }) await Assert.ThrowsAsync<ArgumentException>(() => encoder.RenderAsync(bytes, CancellationToken.None));
        byte[] result = await Encoder(input: bytes.Length, width: 2, height: 2, pixels: 4,
            working: 2 * 512 + 9 + 13).RenderAsync(bytes, CancellationToken.None);
        Assert.NotEmpty(result); Assert.InRange(result.Length, 1, 512);
    }

    [Fact]
    public async Task CancellationPrecedesValidationAndArrayAllocation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Encoder().RenderAsync(ReadOnlyMemory<byte>.Empty, cancel.Token));
    }

    [Theory]
    [InlineData("input")] [InlineData("width")] [InlineData("height")]
    [InlineData("pixels")] [InlineData("output")] [InlineData("working")]
    public void EveryBudgetMustBeExplicitAndPositive(string budget)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Encoder(input: budget == "input" ? 0 : 1024,
            width: budget == "width" ? 0 : 10, height: budget == "height" ? 0 : 10,
            pixels: budget == "pixels" ? 0 : 100, output: budget == "output" ? 0 : 512,
            working: budget == "working" ? 0 : 2048));
    }
}

