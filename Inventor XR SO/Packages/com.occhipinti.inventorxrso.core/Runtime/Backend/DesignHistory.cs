using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed class DesignHistory
    {
        public DocumentState State { get; }
        public bool CanUndo { get; }
        public bool CanRedo { get; }
        public string Ticket { get; }
        public DesignHistory(DocumentState state, bool undo, bool redo, string ticket)
        {
            if (state == null || ((undo || redo) && string.IsNullOrWhiteSpace(ticket)))
                throw new FormatException("Invalid XR history receipt.");
            State = state; CanUndo = undo; CanRedo = redo; Ticket = ticket;
        }
        public static DesignHistory Parse(JObject json, DocumentState expected)
        {
            if ((string)json["status"] != "history" || (string)json["document_id"] != expected.DocumentId
                || (string)json["revision"] != expected.Revision || json["can_undo"]?.Type != JTokenType.Boolean
                || json["can_redo"]?.Type != JTokenType.Boolean) throw new FormatException("XR history differs from the current document.");
            return new DesignHistory(expected,(bool)json["can_undo"],(bool)json["can_redo"],(string)json["ticket"]);
        }
    }
    public interface IDesignHistoryBackend
    {
        Task<DesignHistory> GetHistoryAsync(DocumentState state, CancellationToken ct);
        Task<DocumentState> ApplyHistoryAsync(DesignHistory history, bool redo, CancellationToken ct);
    }
    public sealed partial class InventorBackend : IDesignHistoryBackend
    {
        public async Task<DesignHistory> GetHistoryAsync(DocumentState state, CancellationToken ct) =>
            DesignHistory.Parse(await _mcp.CallToolAsync("inventor_history_xr",new JObject
            { ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision, ["action"] = "status" },ct),state);
        public async Task<DocumentState> ApplyHistoryAsync(DesignHistory history, bool redo, CancellationToken ct)
        {
            if (!(redo ? history.CanRedo : history.CanUndo)) throw new InvalidOperationException("XR history action unavailable.");
            var result = await _mcp.CallToolAsync("inventor_history_xr",new JObject
            { ["document_id"] = history.State.DocumentId, ["expected_revision"] = history.State.Revision,
                ["ticket"] = history.Ticket, ["action"] = redo ? "redo" : "undo" },ct);
            if ((string)result["status"] != "committed" || (string)result["document_id"] != history.State.DocumentId
                || string.IsNullOrWhiteSpace((string)result["revision"])) throw new FormatException("XR history outcome not confirmed. Inspect Inventor.");
            return DocumentState.FromJson(result);
        }
    }
}
