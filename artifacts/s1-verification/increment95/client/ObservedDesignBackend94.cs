using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // DesignSession consumes exceptions into its UI state. Record the structured
    // failure at that boundary without retries, extra CAD reads or raw messages.
    private sealed class ObservedDesignBackend94(IDesignBackend backend,JObject row):IDesignBackend
    {
        public async Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
        {
            try{return await backend.PreviewDesignAsync(state,operations,ct);}
            catch(Exception error){row["preview_backend_failure"]=Failure(error);throw;}
        }
        public async Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct)
        {
            try{return await backend.CommitDesignAsync(preview,ct);}
            catch(Exception error){row["commit_backend_failure"]=Failure(error);throw;}
        }
    }
}
