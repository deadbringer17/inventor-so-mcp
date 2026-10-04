using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Verify
{
    public enum VerifyStatus { Idle, Running, Done, Failed, Stale }

    /// <summary>
    /// One Inventor verification at a time for the whole workspace. Inventor computes on its single UI thread and cannot be
    /// interrupted: an ignored request keeps the gate busy until its answer arrives, and after a TIMEOUT the gate refuses new
    /// verifications for <see cref="TimeoutCooldown"/> because the client cannot know when Inventor is done.
    /// </summary>
    public sealed class VerifyGate
    {
        public static readonly TimeSpan TimeoutCooldown = TimeSpan.FromSeconds(30);
        private readonly Func<DateTime> _now;
        private DateTime? _timedOutAt;

        public VerifyGate(Func<DateTime> now = null) { _now = now ?? (() => DateTime.UtcNow); }

        public bool Busy { get; internal set; }
        public bool CoolingDown => _timedOutAt.HasValue && _now() - _timedOutAt.Value < TimeoutCooldown;
        public bool CanStart => !Busy && !CoolingDown;
        public string Reason => Busy ? VerifyMessages.Busy : CoolingDown ? VerifyMessages.Timeout : null;

        internal void MarkTimeout() => _timedOutAt = _now();
    }

    public interface IVerifyJob
    {
        VerifyStatus Status { get; }
        string ErrorMessage { get; }
        event Action Changed;
        /// <summary>Running: the answer will be discarded; the gate stays busy until it arrives.</summary>
        void Ignore();
        void Reset();
        /// <summary>A Done result computed on another revision becomes Stale.</summary>
        void OnDocumentState(DocumentState state);
    }

    public sealed class VerifyJob<T> : IVerifyJob where T : class, IVerifyResult
    {
        private readonly VerifyGate _gate;
        private int _ticket;

        public VerifyJob(VerifyGate gate) { _gate = gate ?? throw new ArgumentNullException(nameof(gate)); }

        public VerifyStatus Status { get; private set; }
        public T Result { get; private set; }
        public string Revision { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public event Action Changed;

        /// <summary>False, and nothing runs, when the gate refuses. True once the run has finished (or was ignored).</summary>
        public async Task<bool> RunAsync(Func<CancellationToken, Task<T>> run, CancellationToken ct)
        {
            if (!_gate.CanStart) return false;
            int ticket = ++_ticket;
            _gate.Busy = true;
            Status = VerifyStatus.Running; Result = null; Revision = null; ErrorCode = null; ErrorMessage = null;
            try
            {
                Changed?.Invoke();
                var result = await run(ct);
                if (ticket == _ticket) { Result = result; Revision = result?.Revision; Status = VerifyStatus.Done; }
            }
            catch (OperationCanceledException) { if (ticket == _ticket) Status = VerifyStatus.Idle; }
            catch (Exception ex)
            {
                if ((ex is McpException mcp && mcp.Code == "TIMEOUT") || ex is TransportTimeoutException) _gate.MarkTimeout();
                if (ticket == _ticket)
                {
                    Status = VerifyStatus.Failed;
                    ErrorCode = (ex as McpException)?.Code;
                    ErrorMessage = VerifyMessages.For(ex);
                }
            }
            finally
            {
                _gate.Busy = false;
                Changed?.Invoke();
            }
            return true;
        }

        public void Ignore()
        {
            if (Status != VerifyStatus.Running) return;
            _ticket++;
            Status = VerifyStatus.Idle;
            Changed?.Invoke();
        }

        public void Reset()
        {
            _ticket++;
            Status = VerifyStatus.Idle; Result = null; Revision = null; ErrorCode = null; ErrorMessage = null;
            Changed?.Invoke();
        }

        public void OnDocumentState(DocumentState state)
        {
            if (Status != VerifyStatus.Done || state == null || state.Revision == Revision) return;
            Status = VerifyStatus.Stale;
            Changed?.Invoke();
        }
    }

    /// <summary>The three Inventor verifications of Ispeziona, sharing one gate.</summary>
    public sealed class VerifySession
    {
        public VerifySession(Func<DateTime> now = null)
        {
            Gate = new VerifyGate(now);
            Interference = new VerifyJob<InterferenceReport>(Gate);
            Distance = new VerifyJob<DistanceReport>(Gate);
            Health = new VerifyJob<HealthReport>(Gate);
            Jobs = new IVerifyJob[] { Interference, Distance, Health };
        }

        public VerifyGate Gate { get; }
        public VerifyJob<InterferenceReport> Interference { get; }
        public VerifyJob<DistanceReport> Distance { get; }
        public VerifyJob<HealthReport> Health { get; }
        public IReadOnlyList<IVerifyJob> Jobs { get; }
        public IVerifyJob Running => Jobs.FirstOrDefault(j => j.Status == VerifyStatus.Running);

        public void OnDocumentState(DocumentState state) { foreach (var job in Jobs) job.OnDocumentState(state); }
        public void Reset() { foreach (var job in Jobs) job.Reset(); }
    }
}
