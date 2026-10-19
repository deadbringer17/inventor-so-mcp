using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed partial class InventorBackend : INativeSketchImageBackend
    {
        public async Task<NativeSketchImageAsset> GetNativeSketchImageAsync(DocumentState state, string sketchId, string imageId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); NativeSketchImageClient.RequireReferences(state, sketchId, imageId);
            var response = await _mcp.CallToolAsync("inventor_get_sketch_image_xr", new JObject {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision, ["sketch_id"] = sketchId, ["image_id"] = imageId }, ct);
            return await new NativeSketchImageClient(_transport, _server).DownloadAsync(response, state, sketchId, imageId, ct);
        }
    }
}
