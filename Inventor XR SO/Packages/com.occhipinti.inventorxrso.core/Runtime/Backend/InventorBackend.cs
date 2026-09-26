using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class InventorBackend : IInventorBackend
    {
        private readonly IHttpTransport _transport;
        private readonly PairedServer _server;
        private readonly IAssetCache _cache;
        private readonly McpClient _mcp;

        public InventorBackend(IHttpTransport transport, PairedServer server, IAssetCache cache)
        {
            _transport = transport;
            _server = server;
            _cache = cache;
            _mcp = new McpClient(transport, server.BaseUrl, server.Token);
        }

        public Task ConnectAsync(CancellationToken ct) => _mcp.InitializeAsync(ct);

        public async Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct) =>
            CapabilitiesInfo.FromJson(await _mcp.CallToolAsync(ToolNames.GetCapabilities, new JObject(), ct));

        public async Task<DocumentState> GetDocumentStateAsync(CancellationToken ct) =>
            DocumentState.FromJson(await _mcp.CallToolAsync(ToolNames.GetVisualRevision, new JObject(), ct));

        public async Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct) =>
            SceneGraph.FromJson(await _mcp.CallToolAsync(ToolNames.GetSceneGraph, new JObject { ["include_meshes"] = false }, ct));

        public async Task<DefinitionMesh> GetDefinitionMeshAsync(string definitionDocumentId, CancellationToken ct) =>
            DefinitionMesh.FromJson(definitionDocumentId,
                await _mcp.CallToolAsync(ToolNames.GetDisplayMesh, new JObject { ["document_id"] = definitionDocumentId }, ct));

        public async Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct)
        {
            if (_cache.TryGet(mesh.AssetId, out var cached)) return cached;
            var request = new TransportRequest("GET", Resolve(mesh.AssetUrl)) { Timeout = TimeSpan.FromMinutes(2) };
            request.Headers["Authorization"] = "Bearer " + _server.Token;
            var response = await _transport.SendAsync(request, ct);
            if (response.Status == 401) throw new McpUnauthorizedException();
            if (!response.IsSuccess)
                throw new McpException("ASSET_" + response.Status, "Asset " + mesh.AssetId + " could not be downloaded (HTTP " + response.Status + ").");
            if (!AssetIds.Matches(mesh.AssetId, response.Body))
                throw new McpException("ASSET_CORRUPT", "Asset " + mesh.AssetId + " does not match its content hash.");
            _cache.Put(mesh.AssetId, response.Body);
            return response.Body;
        }

        public async Task<string> PickFaceAsync(string occurrenceId, string faceId, CancellationToken ct) =>
            (string)(await _mcp.CallToolAsync(ToolNames.PickEntity, new JObject { ["occurrence_id"] = occurrenceId, ["face_id"] = faceId }, ct))["entity_id"];

        public Task HighlightAsync(IReadOnlyList<string> entityIds, CancellationToken ct) =>
            _mcp.CallToolAsync(ToolNames.HighlightEntity,
                new JObject { ["entity_ids"] = new JArray(entityIds.Cast<object>().ToArray()), ["mode"] = "highlight" }, ct);

        public Task ClearHighlightAsync(CancellationToken ct) =>
            _mcp.CallToolAsync(ToolNames.HighlightEntity, new JObject { ["mode"] = "clear" }, ct);

        public async Task RunEventsAsync(Action onChanged, CancellationToken ct)
        {
            await _mcp.SubscribeAsync(ToolNames.ActiveDocumentUri, ct);
            await _mcp.RunEventStreamAsync(_ => onChanged(), ct);
        }

        /// <summary>asset_url is relative ("/assets/…") unless the host has a public URL.</summary>
        private string Resolve(string assetUrl) =>
            assetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? assetUrl : _server.BaseUrl + "/" + assetUrl.TrimStart('/');
    }
}
