using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Backend
{
    public interface IInventorBackend
    {
        Task ConnectAsync(CancellationToken ct);
        Task<CapabilitiesInfo> GetCapabilitiesAsync(CancellationToken ct);
        Task<DocumentState> GetDocumentStateAsync(CancellationToken ct);
        Task<SceneGraph> GetSceneGraphAsync(CancellationToken ct);
        Task<DefinitionMesh> GetDefinitionMeshAsync(string definitionDocumentId, CancellationToken ct);
        Task<byte[]> GetAssetAsync(DefinitionMesh mesh, CancellationToken ct);
        Task<string> PickFaceAsync(string occurrenceId, string faceId, CancellationToken ct);
        Task HighlightAsync(IReadOnlyList<string> entityIds, CancellationToken ct);
        Task ClearHighlightAsync(CancellationToken ct);
        /// <summary>Subscribe to the active document and report changes until the stream ends.</summary>
        Task RunEventsAsync(Action onChanged, CancellationToken ct);
    }
}
