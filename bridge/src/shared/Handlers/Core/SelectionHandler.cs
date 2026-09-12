#if INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class SelectionHandler : IInventorCommand
{
    public string Name => "get_selection";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        var doc = app.ActiveDocument;
        if (doc == null) return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.NO_DOCUMENT, "No active document", new());
        var selection = new JArray();
        for (int i = 1; i <= doc.SelectSet.Count; i++) selection.Add(EntityReferences.Describe(doc, doc.SelectSet[i]));
        return InventorCommandResult.Success(Guid.Empty, new JObject { ["document_id"] = EntityReferences.DocumentId(doc),
            ["database_revision"] = doc.DatabaseRevisionId, ["revision"] = ctx.Events?.Revision(EntityReferences.DocumentId(doc)), ["selection"] = selection }, new());
    }
}

public sealed class ResolveEntityHandler : IInventorCommand
{
    public string Name => "resolve_entity";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        try { return InventorCommandResult.Success(Guid.Empty, EntityReferences.Resolve((Application)ctx.Application!, (string?)p["entity_id"] ?? ""), new()); }
        catch (ArgumentException ex) { return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, ex.Message, new()); }
    }
}

public sealed class EventsHandler : IInventorCommand
{
    public string Name => "get_events";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.Events == null) return InventorCommandResult.Fail(Guid.Empty, "EVENTS_UNAVAILABLE", "Event tracker unavailable", new());
        long after = (long?)p["after"] ?? 0;
        if (after < 0) return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "after must be non-negative", new());
        return InventorCommandResult.Success(Guid.Empty, ctx.Events.Read(after, (string?)p["epoch"]), new());
    }
}
#endif
