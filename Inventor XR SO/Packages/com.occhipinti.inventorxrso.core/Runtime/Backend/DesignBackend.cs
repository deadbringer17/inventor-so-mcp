using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class DesignPreview
    {
        public string PlanId { get; }
        public string DocumentId { get; }
        public string Revision { get; }
        public DateTimeOffset ExpiresUtc { get; }
        public GlbModel Model { get; }
        public bool IsAssembly { get; }
        public IReadOnlyList<SketchSnapshot> Sketches { get; }
        public DesignPreview(string planId, string documentId, string revision, DateTimeOffset expiresUtc, GlbModel model,
            IEnumerable<SketchSnapshot> sketches = null, bool isAssembly = false)
        {
            if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(documentId)
                || string.IsNullOrWhiteSpace(revision) || model == null || model.DocumentId != documentId)
                throw new ArgumentException("Invalid Design preview.");
            PlanId = planId; DocumentId = documentId; Revision = revision; ExpiresUtc = expiresUtc; Model = model;
            IsAssembly = isAssembly;
            Sketches = sketches?.ToArray() ?? Array.Empty<SketchSnapshot>();
        }
    }

    public interface IDesignBackend
    {
        Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct);
        Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct);
    }

    public interface IDesignChecksBackend : IDesignBackend
    {
        Task<DesignPreview> PreviewDesignWithChecksAsync(DocumentState state, JArray operations, string[] checks, CancellationToken ct);
    }

    public interface IDesignWorkspaceBackend : IDesignBackend
    {
        Task<DesignContext> GetDesignContextAsync(DocumentState state, CancellationToken ct);
        Task<DocumentState> GetDocumentStateAsync(CancellationToken ct);
    }

    public sealed partial class InventorBackend : IDesignWorkspaceBackend, IDesignChecksBackend
    {
        public async Task<DesignContext> GetDesignContextAsync(DocumentState state, CancellationToken ct) =>
            DesignContext.Parse(await _mcp.CallToolAsync("inventor_get_design_context_xr", new JObject
            { ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision }, ct), state);

        public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct) =>
            PreviewDesignWithChecksAsync(state, operations, null, ct);

        public async Task<DesignPreview> PreviewDesignWithChecksAsync(DocumentState state, JArray operations, string[] checksRequested, CancellationToken ct)
        {
            var requested = (JArray)operations.DeepClone();
            bool assembly = requested.Count > 0 && requested.All(o => ((string)o["command"] ?? "").StartsWith("assembly_", StringComparison.Ordinal));
            string health = assembly ? "constraint_health" : "feature_health";
            var requiredChecks = (assembly ? new[] { "rebuild", health, "interference" } : new[] { "rebuild", health })
                .Concat(checksRequested ?? Array.Empty<string>()).Distinct().ToArray();
            var json = await _mcp.CallToolAsync("inventor_plan_change", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
                ["operations"] = requested, ["validate"] = new JArray(requiredChecks),
                ["intent"] = assembly ? "Quest Assembly" : "Quest Design", ["include_preview_mesh"] = true,
            }, ct);
            var preview = json["preview"] as JObject;
            if ((string)json["document_id"] != state.DocumentId || (string)json["document_revision"] != state.Revision
                || !JToken.DeepEquals(json["operations"], requested)
                || preview == null || (string)preview["status"] != "preview_rolled_back"
                || (string)preview["document_id"] != state.DocumentId || (string)preview["revision"] != state.Revision
                || (string)preview["units"] != "m"
                || !(preview["validated"] is JArray checks) || !checks.Values<string>().Contains("rebuild")
                || requiredChecks.Any(check => !checks.Values<string>().Contains(check))
                || !DateTimeOffset.TryParse((string)json["expires_utc"], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var expires))
                throw new FormatException("The server did not confirm this draft's validated preview.");
            string assetId = (string)preview["asset"]?["asset_id"];
            if (!AssetIds.IsValid(assetId)) throw new FormatException("Preview asset missing.");
            // Fetch from the paired host only, never from a URL supplied by the response.
            var mesh = DefinitionMesh.FromJson(state.DocumentId, new JObject
            { ["asset"] = new JObject { ["asset_id"] = assetId, ["asset_url"] = "/assets/" + assetId } });
            var model = GlbModel.Parse(await GetAssetAsync(mesh, ct));
            return new DesignPreview((string)json["plan_id"], state.DocumentId, state.Revision, expires, model,
                (preview["sketches"] as JArray ?? new JArray()).Select(SketchSnapshot.Parse), assembly);
        }

        public async Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct)
        {
            var result = await _mcp.CallToolAsync("inventor_commit_plan", new JObject
            {
                ["plan_id"] = preview.PlanId, ["document_id"] = preview.DocumentId,
                ["expected_revision"] = preview.Revision,
            }, ct);
            if ((string)result["status"] != "committed" || (string)result["document_id"] != preview.DocumentId
                || (string)result["plan_id"] != preview.PlanId || string.IsNullOrWhiteSpace((string)result["revision"]))
                throw new FormatException("Commit outcome was not confirmed. Refresh the CAD document before continuing.");
            return new DocumentState(preview.DocumentId, (string)result["revision"], null);
        }
    }
}
