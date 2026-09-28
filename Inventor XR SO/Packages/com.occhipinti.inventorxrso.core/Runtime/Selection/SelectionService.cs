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
        private int _generation;

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
            ct.ThrowIfCancellationRequested();
            int generation = ++_generation;
            var kind = Resolve(documentKind, Current, occurrenceId);
            bool hadServerHighlight = Current.Kind != SelectionKind.None;
            // The view shows the selection at once; Inventor follows after the round trips.
            Set(kind == SelectionKind.Occurrence
                ? new Selection(kind, occurrenceId, null, occurrenceId)
                : new Selection(kind, occurrenceId, faceId, occurrenceId == null ? faceId : null));
            try
            {
                if (hadServerHighlight) await _backend.ClearHighlightAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (generation != _generation) return;
                if (kind == SelectionKind.Face && occurrenceId != null)
                {
                    var entityId = await _backend.PickFaceAsync(occurrenceId, faceId, ct);
                    ct.ThrowIfCancellationRequested();
                    if (generation != _generation) return;
                    Current = new Selection(kind, occurrenceId, faceId, entityId);
                }
                await _backend.HighlightAsync(new[] { Current.EntityId }, ct);
            }
            catch
            {
                if (generation == _generation) Set(Selection.None);
                throw;
            }
        }

        /// <summary>A new scene invalidates entity ids without issuing a command on the new document.</summary>
        public void ResetLocal() { ++_generation; Set(Selection.None); }

        public async Task ClearAsync(CancellationToken ct)
        {
            if (Current.Kind == SelectionKind.None) return;
            ResetLocal();
            await _backend.ClearHighlightAsync(ct);
        }

        private void Set(Selection selection)
        {
            Current = selection;
            Changed?.Invoke(selection);
        }
    }
}
