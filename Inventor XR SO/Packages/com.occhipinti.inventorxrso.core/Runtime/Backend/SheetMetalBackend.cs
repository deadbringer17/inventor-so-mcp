using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Distinct outcomes of reading the flat pattern; each has its own message, none falls back to an estimate.</summary>
    public enum FlatPatternFailure { Missing, MultiBody, WrongDocumentType, TooLarge, Empty, Stale, Invalid, Other }

    public sealed class FlatPatternException : Exception
    {
        public FlatPatternFailure Failure { get; }
        public FlatPatternException(FlatPatternFailure failure, string message, Exception inner = null) : base(message, inner) { Failure = failure; }

        /// <summary>Maps a coded server refusal to its outcome, or null when it is not a flat-pattern refusal.</summary>
        public static FlatPatternException From(McpToolException ex)
        {
            string reason = (string)(ex.Details?["details"] as JObject)?["reason"];
            if (ex.Code == "WRONG_DOCUMENT_TYPE")
                return new FlatPatternException(FlatPatternFailure.WrongDocumentType, "Il documento non è una parte in lamiera: nessuno sviluppo.", ex);
            if (ex.Code == "MESH_TOO_LARGE")
                return new FlatPatternException(FlatPatternFailure.TooLarge, "Lo sviluppo supera il budget geometrico del visore.", ex);
            if (reason == "FLAT_PATTERN_MISSING")
                return new FlatPatternException(FlatPatternFailure.Missing, "Lo sviluppo non esiste: crealo con Crea sviluppo.", ex);
            if (reason == "MULTI_BODY_PART")
                return new FlatPatternException(FlatPatternFailure.MultiBody, "Lo sviluppo esiste solo per parti a un solo body.", ex);
            if (reason == "FLAT_PATTERN_MESH_EMPTY")
                return new FlatPatternException(FlatPatternFailure.Empty, "Inventor non ha prodotto una geometria per lo sviluppo.", ex);
            return null;
        }
    }

    /// <summary>The flat pattern as a separate display asset with Inventor's own measures. Never carries CAD references.</summary>
    public sealed class FlatPatternMesh
    {
        public string DocumentId { get; }
        public string Revision { get; }
        public string ContentHash { get; }
        public string AssetId { get; }
        public GlbModel Model { get; }
        public double LengthMm { get; }
        public double WidthMm { get; }
        public int BendCount { get; }
        public double? ThicknessMm { get; }
        public double[] BoundsSizeMm { get; }
        /// <summary>Cache key: document, revision and the identity content hash (never the transient asset id).</summary>
        public string Key => DocumentId + "|" + Revision + "|" + ContentHash;
        public FlatPatternMesh(string documentId, string revision, string contentHash, string assetId, GlbModel model,
            double lengthMm, double widthMm, int bendCount, double? thicknessMm = null, double[] boundsSizeMm = null)
        {
            if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(contentHash)
                || model == null || model.DocumentId != documentId || double.IsNaN(lengthMm) || double.IsNaN(widthMm) || lengthMm <= 0 || widthMm <= 0 || bendCount < 0)
                throw new FlatPatternException(FlatPatternFailure.Invalid, "Lo sviluppo ricevuto non è valido.");
            DocumentId = documentId; Revision = revision; ContentHash = contentHash; AssetId = assetId; Model = model;
            LengthMm = lengthMm; WidthMm = widthMm; BendCount = bendCount; ThicknessMm = thicknessMm; BoundsSizeMm = boundsSizeMm;
        }
    }

    public interface ISheetMetalBackend
    {
        Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct);
        /// <summary>Throws <see cref="FlatPatternException"/> with a distinct <see cref="FlatPatternFailure"/> per refusal.</summary>
        Task<FlatPatternMesh> GetFlatPatternMeshAsync(DocumentState state, CancellationToken ct);
    }

    public sealed partial class InventorBackend : ISheetMetalBackend
    {
        // Separate from the folded-part cache and from tentative Design preview assets.
        private readonly IAssetCache _flatPatternCache = new MemoryAssetCache();

        public async Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct)
        {
            JObject json;
            try { json = await _mcp.CallToolAsync(ToolNames.GetSheetMetalInfo, new JObject(), ct); }
            catch (McpToolException ex) { return SheetMetalContext.Unavailable(state, "Lettura lamiera rifiutata: " + ex.Message); }
            // The tool has no revision of its own: bracket it so a document that moved meanwhile reads as stale.
            var after = await GetDocumentStateAsync(ct);
            return SheetMetalContext.Parse(json, state, after);
        }

        private static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer);

        public async Task<FlatPatternMesh> GetFlatPatternMeshAsync(DocumentState state, CancellationToken ct)
        {
            JObject json;
            try
            {
                json = await _mcp.CallToolAsync(ToolNames.GetFlatPatternMesh, new JObject { ["document_id"] = state.DocumentId }, ct);
            }
            catch (McpToolException ex) when (FlatPatternException.From(ex) is FlatPatternException mapped) { throw mapped; }
            if ((string)json["document_id"] != state.DocumentId || (string)json["revision"] != state.Revision)
                throw new FlatPatternException(FlatPatternFailure.Stale, "Lo sviluppo non coincide con la revisione attuale.");
            var flat = json["flat_pattern"] as JObject;
            string contentHash = (string)json["flat_pattern_identity"]?["content_hash"];
            string assetId = (string)json["asset"]?["asset_id"];
            if ((string)json["units"] != "m" || flat == null || flat["exists"]?.Type != JTokenType.Boolean || !(bool)flat["exists"]
                || string.IsNullOrWhiteSpace(contentHash) || !AssetIds.IsValid(assetId)
                || !IsNumber(flat["length_mm"]) || !IsNumber(flat["width_mm"]) || flat["bend_count"]?.Type != JTokenType.Integer)
                throw new FlatPatternException(FlatPatternFailure.Invalid, "Inventor non ha confermato misure e identità dello sviluppo.");
            if ((bool?)json["edit_state_after"]?["flat_pattern_edit_active"] == true)
                throw new FlatPatternException(FlatPatternFailure.Invalid, "Inventor è rimasto in modifica dello sviluppo: controlla il documento.");
            // Fetch from the paired host only, never from a URL supplied by the response.
            var mesh = DefinitionMesh.FromJson(state.DocumentId, new JObject
            { ["asset"] = new JObject { ["asset_id"] = assetId, ["asset_url"] = "/assets/" + assetId } });
            var model = GlbModel.Parse(await DownloadAssetAsync(mesh, _flatPatternCache, ct));
            if (model.DocumentId != state.DocumentId)
                throw new FlatPatternException(FlatPatternFailure.Stale, "La mesh dello sviluppo appartiene a un altro documento.");
            if (model.Primitives.Any(p => p.Faces.Any(f => !string.IsNullOrEmpty(f.FaceId))))
                throw new FlatPatternException(FlatPatternFailure.Invalid, "Lo sviluppo non deve contenere riferimenti CAD.");
            double[] size = json["mesh_bbox_mm"]?["size_mm"] is JArray s && s.Count == 3 ? s.Select(v => (double)v).ToArray() : null;
            return new FlatPatternMesh(state.DocumentId, state.Revision, contentHash, assetId, model,
                (double)flat["length_mm"], (double)flat["width_mm"], (int)flat["bend_count"], (double?)json["thickness_mm"], size);
        }
    }
}
