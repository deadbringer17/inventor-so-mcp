using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Backend
{
    public enum FlatPatternState { Hidden, Loading, Ready, Unavailable, StaleReadOnly }

    /// <summary>
    /// Client-side state of the flat-pattern display. The asset is keyed by document + revision + Inventor's
    /// identity content hash and lives apart from the folded-part cache and from Design preview assets.
    /// Detach and the local offset are view state only: nothing here ever calls the CAD backend to write,
    /// and the view is not a CAD reference (<see cref="IsCadSelectable"/> is always false).
    /// </summary>
    public sealed class FlatPatternView : IDisposable
    {
        public const string Label = "Sviluppo — sola vista";
        private const int MaxCached = 4;
        private readonly ISheetMetalBackend _backend;
        private readonly Dictionary<string, FlatPatternMesh> _cache = new Dictionary<string, FlatPatternMesh>();
        private readonly Queue<string> _order = new Queue<string>();
        private CancellationTokenSource _request = new CancellationTokenSource();
        private DocumentState _context;
        private bool _online, _disposed;
        private int _generation;

        public FlatPatternState State { get; private set; } = FlatPatternState.Hidden;
        public FlatPatternMesh Asset { get; private set; }
        public FlatPatternFailure? Failure { get; private set; }
        public string Reason { get; private set; }
        public bool Detached { get; private set; }
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public double OffsetZ { get; private set; }
        public bool IsCadSelectable => false;
        /// <summary>Revision of the shown asset, for the read-only label.</summary>
        public string RevisionLabel => Asset?.Revision;
        public string StatusText => State == FlatPatternState.StaleReadOnly
            ? Label + " — revisione " + Asset?.Revision + " (sola ispezione)" : State == FlatPatternState.Ready ? Label : Reason;
        public bool IsVisible => State == FlatPatternState.Ready || State == FlatPatternState.StaleReadOnly;
        public int CachedCount => _cache.Count;
        public event Action Changed;

        public FlatPatternView(ISheetMetalBackend backend)
        { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); }

        /// <summary>Document, revision or connection changed. The asset stays hidden until re-verified by <see cref="ShowAsync"/>.</summary>
        public void SetContext(DocumentState state, bool online)
        {
            if (_disposed) return;
            bool sameDoc = _context?.DocumentId == state?.DocumentId, sameRev = sameDoc && _context?.Revision == state?.Revision;
            if (sameRev && _online == online) return;
            var previousOnline = _online;
            _context = state; _online = online;
            if (!sameRev)
            {
                Cancel();
                if (!sameDoc) { Detached = false; OffsetX = OffsetY = OffsetZ = 0; }
                Asset = null; Failure = null; Reason = null; State = FlatPatternState.Hidden;
            }
            else if (!online)
            {
                // Same document and revision but the PC is gone: keep the last verified asset, read-only.
                Cancel();
                if (State == FlatPatternState.Ready && Asset != null) State = FlatPatternState.StaleReadOnly;
                else if (State == FlatPatternState.Loading) { State = FlatPatternState.Hidden; Reason = null; }
            }
            Changed?.Invoke();
        }

        /// <summary>Fetch and verify the flat pattern of the current document/revision. Late or stale answers are dropped.</summary>
        public async Task ShowAsync()
        {
            if (_disposed || _context == null || !_online) throw new InvalidOperationException("Lo sviluppo richiede un documento online.");
            Cancel();
            int generation = _generation;
            var state = _context;
            var ct = _request.Token;
            State = FlatPatternState.Loading; Failure = null; Reason = null; Changed?.Invoke();
            try
            {
                var mesh = await _backend.GetFlatPatternMeshAsync(state, ct);
                if (!IsCurrent(generation, state)) return;
                if (mesh == null || mesh.DocumentId != state.DocumentId || mesh.Revision != state.Revision)
                    throw new FlatPatternException(FlatPatternFailure.Stale, "Lo sviluppo non coincide con la revisione attuale.");
                Asset = Remember(mesh); State = FlatPatternState.Ready;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                if (!IsCurrent(generation, state)) return;
                // Never keep or show an older asset after a failed re-verification: no estimated pattern.
                Asset = null; State = FlatPatternState.Unavailable;
                Failure = (ex as FlatPatternException)?.Failure ?? FlatPatternFailure.Other;
                Reason = ex.Message;
            }
            Changed?.Invoke();
        }

        /// <summary>User closes the display; a pending request is cancelled and its answer dropped.</summary>
        public void Hide()
        {
            Cancel(); Asset = null; Failure = null; Reason = null; State = FlatPatternState.Hidden; Changed?.Invoke();
        }

        /// <summary>Show the pattern beside the folded part. View state only.</summary>
        public void Detach()
        {
            if (Detached) return;
            Detached = true; Changed?.Invoke();
        }
        public void Attach()
        {
            if (!Detached) return;
            Detached = false; OffsetX = OffsetY = OffsetZ = 0; Changed?.Invoke();
        }

        /// <summary>Grip repositions the local view (metres, viewer space). Never a CAD pose.</summary>
        public bool MoveLocal(double x, double y, double z)
        {
            if (!Detached || !IsVisible || double.IsNaN(x + y + z) || double.IsInfinity(x + y + z)) return false;
            OffsetX = x; OffsetY = y; OffsetZ = z; Changed?.Invoke();
            return true;
        }

        private bool IsCurrent(int generation, DocumentState state) => !_disposed && generation == _generation
            && _context != null && _context.DocumentId == state.DocumentId && _context.Revision == state.Revision && _online;

        private FlatPatternMesh Remember(FlatPatternMesh mesh)
        {
            if (_cache.TryGetValue(mesh.Key, out var known)) return known;
            _cache[mesh.Key] = mesh; _order.Enqueue(mesh.Key);
            while (_order.Count > MaxCached) _cache.Remove(_order.Dequeue());
            return mesh;
        }

        private void Cancel()
        {
            _generation++;
            _request.Cancel(); _request.Dispose(); _request = new CancellationTokenSource();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _generation++; _request.Cancel(); _request.Dispose(); Changed = null;
            _cache.Clear(); _order.Clear(); Asset = null;
        }
    }
}
