using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Selection
{
    /// <summary>
    /// Selection following Inventor's defaults (spec §14): faces in a part; occurrences in an assembly,
    /// and a second click on the selected occurrence selects the face under the ray. The local view is
    /// told first, then Inventor highlights the same entity.
    /// </summary>
    public sealed class SelectionService
    {
        private readonly IInventorBackend _backend;

        public SelectionService(IInventorBackend backend) { _backend = backend; }

        public Selection Current { get; private set; } = Selection.None;
        public event Action<Selection> Changed;

        public static SelectionKind Resolve(string documentKind, Selection current, string occurrenceId)
        {
            if (documentKind != "assembly") return SelectionKind.Face;
            bool sameOccurrence = current.Kind != SelectionKind.None && current.OccurrenceId == occurrenceId;
            return sameOccurrence ? SelectionKind.Face : SelectionKind.Occurrence;
        }

        public async Task SelectAsync(string documentKind, string occurrenceId, string faceId, CancellationToken ct)
        {
            var kind = Resolve(documentKind, Current, occurrenceId);
            bool hadServerHighlight = Current.Kind != SelectionKind.None;
            // The view shows the selection at once; Inventor follows after the round trips.
            Set(kind == SelectionKind.Occurrence
                ? new Selection(kind, occurrenceId, null, occurrenceId)
                : new Selection(kind, occurrenceId, faceId, occurrenceId == null ? faceId : null));
            if (hadServerHighlight) await _backend.ClearHighlightAsync(ct);
            if (kind == SelectionKind.Face && occurrenceId != null)
            {
                // Only the entity id was missing: no second Changed event.
                try { Current = new Selection(kind, occurrenceId, faceId, await _backend.PickFaceAsync(occurrenceId, faceId, ct)); }
                catch
                {
                    Set(Selection.None);
                    throw;
                }
            }
            await _backend.HighlightAsync(new[] { Current.EntityId }, ct);
        }

        public async Task ClearAsync(CancellationToken ct)
        {
            if (Current.Kind == SelectionKind.None) return;
            Set(Selection.None);
            await _backend.ClearHighlightAsync(ct);
        }

        private void Set(Selection selection)
        {
            Current = selection;
            Changed?.Invoke(selection);
        }
    }
}
