#if INVENTOR2027
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>COM event subscriptions live and are disposed on Inventor's STA.</summary>
internal sealed class CadEventTracker : IDisposable
{
    private readonly ApplicationEvents _events;
    private readonly Dictionary<string, string> _closing = new(StringComparer.OrdinalIgnoreCase);
    public CadEventJournal Journal { get; } = new();
    public CadEventTracker(Application app)
    {
        _events = app.ApplicationEvents;
        _events.OnDocumentChange += Changed;
        _events.OnNewDocument += Created;
        _events.OnOpenDocument += Opened;
        _events.OnSaveDocument += Saved;
        _events.OnActivateDocument += Activated;
        _events.OnCloseDocument += Closed;
    }
    private void Record(string type, _Document doc)
    {
        string? id = null;
        try { id = "doc_" + doc.InternalName; } catch { /* closed document may be disconnected */ }
        Journal.Append(type, id);
    }
    private void Changed(_Document doc, EventTimingEnum timing, CommandTypesEnum reason, NameValueMap context, out HandlingCodeEnum handling)
    { handling = HandlingCodeEnum.kEventNotHandled; if (timing == EventTimingEnum.kAfter) Record("document_changed", doc); }
    private void Created(_Document doc, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
    { handling = HandlingCodeEnum.kEventNotHandled; if (timing == EventTimingEnum.kAfter) Record("document_opened", doc); }
    private void Opened(_Document doc, string path, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
    { handling = HandlingCodeEnum.kEventNotHandled; if (timing == EventTimingEnum.kAfter) Record("document_opened", doc); }
    private void Saved(_Document doc, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
    { handling = HandlingCodeEnum.kEventNotHandled; if (timing == EventTimingEnum.kAfter) Record("document_saved", doc); }
    private void Activated(_Document doc, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
    { handling = HandlingCodeEnum.kEventNotHandled; if (timing == EventTimingEnum.kAfter) Record("document_activated", doc); }
    private void Closed(_Document doc, string path, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
    {
        handling = HandlingCodeEnum.kEventNotHandled;
        if (timing == EventTimingEnum.kBefore)
        {
            try { _closing[path] = "doc_" + doc.InternalName; } catch { }
        }
        else if (timing == EventTimingEnum.kAfter)
        {
            _closing.TryGetValue(path, out var id);
            _closing.Remove(path);
            Journal.Append("document_closed", id);
        }
    }
    public void Dispose()
    {
        _events.OnDocumentChange -= Changed;
        _events.OnNewDocument -= Created;
        _events.OnOpenDocument -= Opened;
        _events.OnSaveDocument -= Saved;
        _events.OnActivateDocument -= Activated;
        _events.OnCloseDocument -= Closed;
    }
}
#endif
