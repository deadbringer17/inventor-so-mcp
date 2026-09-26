#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// Journals <c>selection_changed</c> (plan §20, experimental tier). A view event: the journal never
/// advances a document revision for it, so selecting in the UI cannot invalidate a change plan.
/// Subscribed and disposed on Inventor's STA, like <see cref="CadEventTracker"/>.
/// </summary>
internal sealed class SelectionEventTracker : IDisposable
{
    private readonly Application _app;
    private readonly UserInputEvents _events;
    private readonly CadEventJournal _journal;

    /// <summary>Whether a tracker is subscribed in this add-in (reported by get_capabilities).</summary>
    public static bool Active { get; private set; }

    public SelectionEventTracker(Application app, CadEventJournal journal)
    {
        _app = app;
        _journal = journal;
        _events = app.CommandManager.UserInputEvents;
        _events.OnSelect += Selected;
        _events.OnUnSelect += Unselected;
        Active = true;
    }

    private void Record(int changed)
    {
        string? id = null;
        int count = 0;
        try
        {
            var doc = _app.ActiveDocument;
            if (doc != null) { id = "doc_" + doc.InternalName; count = doc.SelectSet.Count; }
        }
        catch { /* document closing */ }
        _journal.Append("selection_changed", id, new JObject { ["changed"] = changed, ["selected"] = count });
    }

    private void Selected(ObjectsEnumerator justSelected, ref ObjectCollection moreSelected, SelectionDeviceEnum device,
        Point modelPosition, Point2d viewPosition, View view) => Record(justSelected.Count);

    private void Unselected(ObjectsEnumerator unselected, SelectionDeviceEnum device, Point modelPosition, Point2d viewPosition, View view)
        => Record(unselected.Count);

    public void Dispose()
    {
        _events.OnSelect -= Selected;
        _events.OnUnSelect -= Unselected;
        Active = false;
    }
}
#endif
