using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public interface IAssemblyWorkspaceBackend : IDesignBackend, IDesignHistoryBackend
    {
        Task<AssemblyContext> GetAssemblyContextAsync(DocumentState state, string occurrenceId, CancellationToken ct);
        Task<DocumentState> GetDocumentStateAsync(CancellationToken ct);
    }
    public sealed partial class InventorBackend : IAssemblyWorkspaceBackend
    {
        public async Task<AssemblyContext> GetAssemblyContextAsync(DocumentState state, string occurrenceId, CancellationToken ct) =>
            AssemblyContext.Parse(await _mcp.CallToolAsync("inventor_get_assembly_context_xr", new JObject {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision, ["occurrence_id"] = occurrenceId }, ct), state);
    }
}
