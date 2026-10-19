using Bimwright.Ipt.Server.Imports;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Imports;

public sealed record ImportHttpFormat(string Format, string Kind);

/// <summary>Inspector of actual bytes, supplied by the embedding host. It must not invoke CAD or remote resources.</summary>
public interface IImportContentInspector
{
    Task<JObject> InspectAsync(string format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

/// <summary>
/// Explicit opt-in for the in-memory HTTP foundation. No formats, native import capabilities or budgets are inferred.
/// Inspector metadata uses the Core resource shape: kind, dependencies, image, geometry.
/// </summary>
public sealed class ImportHttpPolicy
{
    private readonly Dictionary<string, string> _formats;
    public ImportHttpPolicy(ImportResourceStorePolicy storePolicy, int maxBodyBytes, int listLimit,
        IEnumerable<ImportHttpFormat> formats, IImportContentInspector inspector, BmpImportPreviewRenderer? previewRenderer = null)
    {
        StorePolicy = storePolicy ?? throw new ArgumentNullException(nameof(storePolicy));
        Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        if (maxBodyBytes < 1 || maxBodyBytes > storePolicy.MaxBytesPerResource || listLimit < 1 || listLimit > storePolicy.MaxRecords)
            throw new ArgumentException("HTTP limits must be positive and within the explicit store policy.");
        MaxBodyBytes = maxBodyBytes; ListLimit = listLimit;
        if (formats == null) throw new ArgumentNullException(nameof(formats));
        _formats = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var format in formats)
            if (format == null || string.IsNullOrEmpty(format.Format) || format.Format.Length > storePolicy.MaxFormatLength
                || !format.Format.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')
                || !storePolicy.IsFormatAllowed(format.Format)
                || format.Kind is not ("Image" or "Points" or "Acad") || !_formats.TryAdd(format.Format, format.Kind))
                throw new ArgumentException("Declare distinct store-approved format tokens and their Image/Points/Acad kinds.", nameof(formats));
        if (_formats.Count == 0) throw new ArgumentException("Declare at least one inspected format.", nameof(formats));
        if (previewRenderer != null && (!ReferenceEquals(previewRenderer.Inspector, inspector)
            || !_formats.TryGetValue("bmp", out string? previewKind) || previewKind != "Image"))
            throw new ArgumentException("BMP preview requires the same BMP inspector and a declared BMP Image format.", nameof(previewRenderer));
        PreviewRenderer = previewRenderer;
    }
    public ImportResourceStorePolicy StorePolicy { get; }
    public IImportContentInspector Inspector { get; }
    public BmpImportPreviewRenderer? PreviewRenderer { get; }
    public int MaxBodyBytes { get; }
    public int ListLimit { get; }
    internal bool TryKind(string format, out string kind) => _formats.TryGetValue(format, out kind!);
}
