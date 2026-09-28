using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum DesignStatus { Empty, Draft, Previewing, PreviewReady, Committing, RefreshRequired, Error }

    /// <summary>Main-thread owned Design workflow. A rendered, unexpired preview is the only path to Apply.</summary>
    public sealed class DesignSession : IDisposable
    {
        private readonly IDesignBackend _backend;
        private readonly Func<DateTimeOffset> _clock;
        private CancellationTokenSource _request = new CancellationTokenSource();
        private DocumentState _context;
        private JArray _operations;
        private string[] _checks;
        private int _generation;
        private bool _online, _part, _rendered, _commitInFlight, _disposed;
        public DesignStatus Status { get; private set; }
        public DesignPreview Preview { get; private set; }
        public string Error { get; private set; }
        public bool CommitOutcomeUnknown { get; private set; }
        public DocumentState LastCommit { get; private set; }
        public event Action Changed;
        public bool CanEdit => !_disposed && !_commitInFlight && Status != DesignStatus.RefreshRequired
            && _online && _part && _context != null;
        public bool CanApply => CanEdit && Status == DesignStatus.PreviewReady && _rendered && Preview != null
            && Preview.ExpiresUtc > _clock() && Matches(Preview);

        public DesignSession(IDesignBackend backend, Func<DateTimeOffset> clock = null)
        { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _clock = clock ?? (() => DateTimeOffset.UtcNow); }

        public void SetContext(DocumentState state, bool online, bool isPart)
        {
            if (_context?.DocumentId == state?.DocumentId && _context?.Revision == state?.Revision
                && _online == online && _part == isPart) return;
            Invalidate();
            _context = state; _online = online; _part = isPart;
            _operations = null; Preview = null;
            if (!_commitInFlight && Status != DesignStatus.RefreshRequired) Status = DesignStatus.Empty;
            Changed?.Invoke();
        }

        public void SetDraft(JArray operations, string[] validationChecks = null)
        {
            if (!CanEdit) throw new InvalidOperationException("Design is unavailable until the document is online and refreshed.");
            if (operations == null || operations.Count < 1 || operations.Count > 32)
                throw new ArgumentException("A Design command needs 1–32 operations.");
            Invalidate();
            _operations = (JArray)operations.DeepClone();
            _checks = validationChecks == null ? null : (string[])validationChecks.Clone();
            // Keep the previous ghost as a visual reference while correcting the draft; it cannot be applied.
            Status = DesignStatus.Draft; Error = null; Changed?.Invoke();
        }

        /// <summary>Keep the last ghost as reference, but discard any executable draft after local validation fails.</summary>
        public void RejectDraft(string error)
        {
            if (!CanEdit) throw new InvalidOperationException("Design is unavailable until the document is online and refreshed.");
            Invalidate(); _operations = null;
            Error = error; Status = DesignStatus.Error; Changed?.Invoke();
        }

        public async Task PreviewAsync()
        {
            if (!CanEdit || _operations == null) throw new InvalidOperationException("No editable Design draft.");
            Invalidate();
            int generation = _generation;
            var state = _context;
            var ct = _request.Token;
            Status = DesignStatus.Previewing; Error = null; Changed?.Invoke();
            try
            {
                DesignPreview result;
                if (_checks != null && _checks.Length > 0)
                {
                    if (!(_backend is IDesignChecksBackend validating)) throw new InvalidOperationException("Backend does not support the requested validation checks.");
                    result = await validating.PreviewDesignWithChecksAsync(state, (JArray)_operations.DeepClone(), (string[])_checks.Clone(), ct);
                }
                else result = await _backend.PreviewDesignAsync(state, (JArray)_operations.DeepClone(), ct);
                if (_disposed || generation != _generation) return;
                if (!Matches(result) || result.ExpiresUtc <= _clock()) throw new InvalidOperationException("Preview is stale or expired.");
                Preview = result; Status = DesignStatus.PreviewReady;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                if (_disposed || generation != _generation) return;
                Error = ex.Message; Status = DesignStatus.Error;
            }
            Changed?.Invoke();
        }

        public void ConfirmRendered(string planId)
        {
            if (Status != DesignStatus.PreviewReady || Preview?.PlanId != planId) return;
            _rendered = true; Changed?.Invoke();
        }

        public async Task ApplyAsync()
        {
            if (!CanApply) throw new InvalidOperationException("Render and validate the current preview before Apply.");
            var preview = Preview;
            await MutateAsync(ct => _backend.CommitDesignAsync(preview,ct));
        }

        public async Task ApplyHistoryAsync(DesignHistory history, bool redo)
        {
            if (!CanEdit || Status != DesignStatus.Empty || !(_backend is IDesignHistoryBackend backend)
                || history == null || history.State.DocumentId != _context.DocumentId || history.State.Revision != _context.Revision
                || !(redo ? history.CanRedo : history.CanUndo)) throw new InvalidOperationException("No current XR history action available. Finish or cancel the draft first.");
            await MutateAsync(ct => backend.ApplyHistoryAsync(history,redo,ct));
        }

        private async Task MutateAsync(Func<CancellationToken,Task<DocumentState>> mutation)
        {
            var ct = _request.Token;
            _rendered = false; _commitInFlight = true; Status = DesignStatus.Committing;
            Error = null; LastCommit = null; CommitOutcomeUnknown = false; Changed?.Invoke();
            try
            {
                ct.ThrowIfCancellationRequested();
                LastCommit = await mutation(ct);
                Status = DesignStatus.RefreshRequired;
            }
            catch (McpToolException ex) when (ex.Code == "STALE_REVISION" || ex.Code == "DOCUMENT_CHANGED"
                || ex.Code == "PLAN_NOT_FOUND" || ex.Code == "PLAN_MISMATCH" || ex.Code == "ROLLED_BACK"
                || ex.Code == "READ_ONLY" || ex.Code == "HISTORY_CHANGED" || ex.Code == "TRANSACTION_BUSY")
            {
                Error = ex.Message; Status = DesignStatus.RefreshRequired;
            }
            catch (Exception ex)
            {
                // Cancellation/transport loss can happen AFTER CAD committed. Never retry that plan.
                Error = ex.Message; CommitOutcomeUnknown = true; Status = DesignStatus.RefreshRequired;
            }
            finally
            {
                _commitInFlight = false;
                Preview = null; _operations = null;
                if (!_disposed) Changed?.Invoke();
            }
        }

        /// <summary>Called after fetching authoritative CAD state; unknown outcomes also require user inspection.</summary>
        public void RequireCadReview()
        {
            if (_disposed || _commitInFlight) throw new InvalidOperationException("Cannot replace an active mutation guard.");
            Invalidate(); Preview=null; _operations=null; CommitOutcomeUnknown=true;
            Status=DesignStatus.RefreshRequired; Changed?.Invoke();
        }

        /// <summary>Called after fetching authoritative CAD state; unknown outcomes also require user inspection.</summary>
        public void AcknowledgeRefresh(DocumentState state, bool unknownOutcomeReviewed = false)
        {
            if (_disposed || _commitInFlight || !_online || state == null
                || (CommitOutcomeUnknown && !unknownOutcomeReviewed))
                throw new InvalidOperationException("Inspect the refreshed CAD state before continuing.");
            Invalidate(); _context = state; Preview = null; _operations = null;
            CommitOutcomeUnknown = false; Error = null; Status = DesignStatus.Empty; Changed?.Invoke();
        }

        public void Cancel()
        {
            if (_commitInFlight || Status == DesignStatus.RefreshRequired)
                throw new InvalidOperationException("An attempted Apply requires refreshing CAD, not canceling locally.");
            Invalidate(); Preview = null; _operations = null; Error = null;
            Status = DesignStatus.Empty; Changed?.Invoke();
        }

        private bool Matches(DesignPreview preview) => preview != null
            && preview.DocumentId == _context?.DocumentId && preview.Revision == _context?.Revision;
        private void Invalidate()
        {
            _generation++; _rendered = false;
            _request.Cancel(); _request.Dispose(); _request = new CancellationTokenSource();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _generation++; _request.Cancel(); _request.Dispose(); Changed = null;
        }
    }
}
