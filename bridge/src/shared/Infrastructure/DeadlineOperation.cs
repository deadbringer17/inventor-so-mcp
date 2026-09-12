using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// A one-shot operation. Expiration prevents queued work from starting. Once work has
/// started COM cannot be safely interrupted: expiration reports an unknown outcome.
/// The scheduler must call Expire at the deadline, even when its UI queue is blocked.
/// </summary>
public sealed class DeadlineOperation<T>
{
    private readonly object _gate = new();
    private readonly Func<T> _work;
    private readonly Func<long> _elapsedMs;
    private readonly int _timeoutMs;
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _finished;

    public DeadlineOperation(Func<T> work, int timeoutMs, Func<long>? elapsedMs = null)
    {
        if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _timeoutMs = timeoutMs;
        var stopwatch = Stopwatch.StartNew();
        _elapsedMs = elapsedMs ?? (() => stopwatch.ElapsedMilliseconds);
    }

    public Task<T> Task => _completion.Task;

    public void Execute()
    {
        lock (_gate)
        {
            if (_finished || _started) return;
            // Timer callbacks may be delayed; check the deadline independently at dequeue.
            if (_elapsedMs() >= _timeoutMs) { ExpireLocked(); return; }
            _started = true;
        }
        try { _completion.TrySetResult(_work()); }
        catch (Exception ex) { _completion.TrySetException(ex); }
        finally { lock (_gate) _finished = true; }
    }

    public void Expire()
    {
        lock (_gate) ExpireLocked();
    }

    private void ExpireLocked()
    {
        if (_finished) return;
        _finished = true;
        _completion.TrySetException(new TimeoutException(_started
            ? "Operation deadline exceeded after execution started. Outcome unknown; inspect CAD before retrying."
            : "Operation expired while queued; no CAD operation was started."));
    }
}
