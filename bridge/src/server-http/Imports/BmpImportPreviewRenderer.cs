using Bimwright.Ipt.Server.Assets;

namespace Inventor.So.Mcp.Http.Imports;

/// <summary>HTTP import preview policy adapter over the shared bounded PNG encoder.</summary>
public sealed class BmpImportPreviewRenderer
{
    public BmpImportContentInspector Inspector { get; }
    public int MaxOutputBytes { get; }
    public long MaxWorkingBufferBytes { get; }

    public BmpImportPreviewRenderer(BmpImportContentInspector inspector, int maxOutputBytes, long maxWorkingBufferBytes)
    {
        Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        if (maxOutputBytes < 1 || maxOutputBytes > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));
        if (maxWorkingBufferBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxWorkingBufferBytes));
        MaxOutputBytes = maxOutputBytes; MaxWorkingBufferBytes = maxWorkingBufferBytes;
    }

    public async Task<byte[]> RenderAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        await Inspector.InspectAsync("bmp", content, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var encoder = new BmpPngEncoder(content.Length, Inspector.MaxWidth, Inspector.MaxHeight,
            Inspector.MaxPixels, MaxOutputBytes, MaxWorkingBufferBytes);
        return await encoder.RenderAsync(content, cancellationToken).ConfigureAwait(false);
    }
}
