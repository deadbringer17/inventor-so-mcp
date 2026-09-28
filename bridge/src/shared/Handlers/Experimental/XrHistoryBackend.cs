#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal sealed class XrHistoryState
{
    private static readonly ConditionalWeakTable<CadEventJournal, XrHistoryState> States = new();
    public static XrHistoryState For(InventorCommandContext ctx) => States.GetValue(ctx.Events!, _ => new XrHistoryState());
    public readonly XrUndoHistory History = new();
    public int? IdleTransactionId;
    public void ObserveOwnedEnd(Application app) => IdleTransactionId = app.TransactionManager.CurrentTransaction?.Id;
}

internal sealed class XrHistoryBackend : IXrHistoryBackend
{
    private readonly InventorCommandContext _ctx;
    private readonly Application _app;
    private readonly XrHistoryState _state;
    public XrHistoryBackend(InventorCommandContext ctx, Application app)
    { _ctx = ctx; _app = app; _state = XrHistoryState.For(ctx); }
    public string DocumentId => EntityReferences.DocumentId(X.Active(_app));
    public string Revision => _ctx.Events!.Revision(DocumentId);
    public bool Busy => !_state.IdleTransactionId.HasValue
        || _app.TransactionManager.CurrentTransaction?.Id != _state.IdleTransactionId.Value
        || _app.CommandManager.ActiveCommand != "AppSelectNorthwestArrowCmd";
    public string? UndoTransactionId => Top(_app.TransactionManager.CommittedTransactions);
    public string? RedoTransactionId => Top(_app.TransactionManager.UndoneTransactions);
    private string? Top(TransactionsEnumerator transactions)
    {
        if (transactions.Count == 0) return null;
        var transaction = transactions[transactions.Count];
        // Inventor's stack is application-wide. Never step into a different document.
        if (EntityReferences.DocumentId(transaction.Document) != DocumentId) return null;
        return transaction.Id.ToString(CultureInfo.InvariantCulture);
    }
    public void Undo() { _app.TransactionManager.UndoTransaction(); Changed(); }
    public void Redo() { _app.TransactionManager.RedoTransaction(); Changed(); }
    private void Changed()
    {
        // Explicit event also covers builds where the native Undo event omits geometry change.
        _ctx.Events!.Append("document_changed",DocumentId);
        _state.ObserveOwnedEnd(_app);
    }
}

public sealed class XrHistoryHandler : ExperimentalHandler
{
    public override string Name => "history_xr";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        if (ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY,"XR history requires write permission.");
        if (X.Active(app) is not PartDocument && X.Active(app) is not AssemblyDocument)
            throw new ArgumentException("XR history requires an active part or assembly.");
        var backend = new XrHistoryBackend(ctx,app);
        string document = X.Str(p,"document_id"), revision = X.Str(p,"expected_revision"), owner = X.Str(p,"owner");
        if (backend.DocumentId != document) throw ConcurrencyFailure.DocumentChanged(document,backend.DocumentId);
        if (backend.Revision != revision) throw ConcurrencyFailure.StaleRevision(revision,backend.Revision);
        var history = XrHistoryState.For(ctx).History;
        string action = X.Str(p,"action");
        if (action != "status" && action != "undo" && action != "redo") throw new ArgumentException("action must be status, undo or redo.");
        if (action != "status")
        {
            X.Deadline(ctx,"before XR history operation");
            bool disabled = app.UserInterfaceManager.UserInteractionDisabled;
            try
            {
                app.UserInterfaceManager.UserInteractionDisabled = true;
                history.Apply(owner,document,revision,X.Str(p,"ticket"),action == "redo",backend);
            }
            catch (XrHistoryException ex) { throw new CodedFailureException(ex.Code,ex.Message); }
            finally { app.UserInterfaceManager.UserInteractionDisabled = disabled; }
        }
        var available = history.Available(owner,backend);
        return new JObject { ["document_id"] = document, ["revision"] = backend.Revision,
            ["visual_revision"] = ctx.Events!.VisualRevision(document), ["status"] = action == "status" ? "history" : "committed",
            ["can_undo"] = available.Undo, ["can_redo"] = available.Redo, ["ticket"] = available.Ticket };
    }
}
#endif
