using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Adapter must execute on Inventor's UI thread with interaction disabled.</summary>
public interface IXrHistoryBackend
{
    string DocumentId { get; }
    string Revision { get; }
    string? UndoTransactionId { get; }
    string? RedoTransactionId { get; }
    bool Busy { get; }
    void Undo();
    void Redo();
}

public sealed class XrHistoryException : Exception
{
    public string Code { get; }
    public XrHistoryException(string code, string message) : base(message) { Code = code; }
}

/// <summary>
/// In-memory, bounded receipt chain. A revision alone is insufficient: the native stack's
/// next transaction must also match. Never searches past unrelated desktop transactions.
/// All calls are serialized by the add-in dispatcher on Inventor's UI thread.
/// </summary>
public sealed class XrUndoHistory
{
    private sealed class Entry
    {
        public string Owner = "", Transaction = "";
    }
    private sealed class Chain
    {
        public readonly List<Entry> Entries = new();
        public int Position;
        public string Revision = "", Ticket = "";
    }
    private readonly Dictionary<string, Chain> _documents = new(StringComparer.Ordinal);
    private const int MaxDocuments = 16, MaxEntries = 64;

    /// <summary>Called only after a successful owned commit, never for previews or rollback.</summary>
    public string Record(string owner, string document, string beforeRevision, string afterRevision, string transaction)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(document)
            || string.IsNullOrWhiteSpace(beforeRevision) || string.IsNullOrWhiteSpace(afterRevision)
            || string.IsNullOrWhiteSpace(transaction)) throw new ArgumentException("Incomplete XR commit receipt.");
        if (!_documents.TryGetValue(document, out var chain) || chain.Revision != beforeRevision)
        {
            if (_documents.Count >= MaxDocuments) _documents.Clear();
            _documents[document] = chain = new Chain();
        }
        chain.Entries.RemoveRange(chain.Position, chain.Entries.Count-chain.Position);
        chain.Entries.Add(new Entry { Owner = owner, Transaction = transaction });
        if (chain.Entries.Count > MaxEntries) chain.Entries.RemoveAt(0);
        chain.Position = chain.Entries.Count;
        chain.Revision = afterRevision;
        return chain.Ticket = NewTicket();
    }

    public (bool Undo, bool Redo, string? Ticket) Available(string owner, IXrHistoryBackend backend)
    {
        if (backend.Busy || !_documents.TryGetValue(backend.DocumentId, out var chain)) return (false,false,null);
        if (chain.Revision != backend.Revision)
        { _documents.Remove(backend.DocumentId); return (false,false,null); }
        bool undo = Matches(chain,owner,false,backend.UndoTransactionId);
        bool redo = Matches(chain,owner,true,backend.RedoTransactionId);
        return (undo,redo,undo || redo ? chain.Ticket : null);
    }

    public string Apply(string owner, string document, string revision, string ticket, bool redo, IXrHistoryBackend backend)
    {
        if (backend.DocumentId != document) throw Fail("DOCUMENT_CHANGED", "The active document changed.");
        if (backend.Revision != revision) throw Fail("STALE_REVISION", "Refresh the document before Undo/Redo.");
        if (backend.Busy) throw Fail("TRANSACTION_BUSY", "Finish the active Inventor command first.");
        if (!_documents.TryGetValue(document, out var chain) || chain.Revision != revision
            || chain.Ticket != ticket || !Matches(chain,owner,redo,redo ? backend.RedoTransactionId : backend.UndoTransactionId))
            throw Fail("HISTORY_CHANGED", "No matching XR transaction at the top of the native history.");
        // Consume before touching COM. Any exception or uncertain native result disables this
        // chain, including retries with a newly observed revision after a lost response.
        _documents.Remove(document);
        var entry = chain.Entries[redo ? chain.Position : chain.Position-1];
        if (redo) backend.Redo(); else backend.Undo();
        if (backend.DocumentId != document || (redo ? backend.UndoTransactionId : backend.RedoTransactionId) != entry.Transaction)
            throw Fail("HISTORY_OUTCOME_UNKNOWN", "Inspect Inventor: the native history did not confirm the operation.");
        chain.Position += redo ? 1 : -1;
        chain.Revision = backend.Revision;
        chain.Ticket = NewTicket();
        _documents[document] = chain;
        return chain.Ticket;
    }

    private static bool Matches(Chain chain, string owner, bool redo, string? transaction)
    {
        int index = redo ? chain.Position : chain.Position-1;
        return index >= 0 && index < chain.Entries.Count && chain.Entries[index].Owner == owner
            && chain.Entries[index].Transaction == transaction;
    }
    private static string NewTicket() => Guid.NewGuid().ToString("N");
    private static XrHistoryException Fail(string code, string message) => new(code,message);
}
