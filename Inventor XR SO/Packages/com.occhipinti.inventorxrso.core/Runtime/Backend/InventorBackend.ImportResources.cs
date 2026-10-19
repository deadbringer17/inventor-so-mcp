using System;

namespace InventorXrSo.Core.Backend
{
    public sealed partial class InventorBackend
    {
        /// <summary>Read-only resource exchange, independent of MCP/CAD capabilities and persistent GLB caches.</summary>
        public IImportResourceClient CreateImportResourceClient(ImportResourceClientPolicy policy, Func<DateTimeOffset> clock)
            => new ImportResourceClient(_transport, _server, policy, clock);
    }
}
