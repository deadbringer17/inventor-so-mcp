namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Marshals work onto Inventor's main STA thread. Inventor has no <c>ExternalEvent</c> (unlike Revit),
/// so we use a hidden message-only WinForms <see cref="Control"/> created on the STA thread during
/// <c>Activate</c>; its forced handle lets <see cref="Control.BeginInvoke(Delegate)"/> queue work onto
/// the UI thread. The transport listener thread only ever touches this control via
/// <see cref="InvokeAsync{T}"/> (i.e. via <c>BeginInvoke</c>), never directly.
/// </summary>
public sealed class InventorStaDispatcher : IDisposable
{
    private readonly Control _marshal;       // created on the STA/main thread

    public InventorStaDispatcher()
    {
        // MUST be constructed on Inventor's main STA thread (during Activate).
        _marshal = new Control();
        var _ = _marshal.Handle;             // force handle creation so BeginInvoke works
    }

    public Task<T> InvokeAsync<T>(Func<T> work, int timeoutMs)
    {
        if (!_marshal.IsHandleCreated) return Task.FromException<T>(new InvalidOperationException("STA dispatcher not ready"));
        var operation = new DeadlineOperation<T>(work, timeoutMs);
        var timer = new System.Threading.Timer(_ => operation.Expire(), null, timeoutMs, Timeout.Infinite);
        _ = operation.Task.ContinueWith(_ => timer.Dispose(), TaskScheduler.Default);
        try { _marshal.BeginInvoke((Action)operation.Execute); }
        catch { timer.Dispose(); throw; }
        return operation.Task;
    }

    public void Dispose()
    {
        try { if (_marshal.IsHandleCreated) _marshal.Invoke((Action)(() => _marshal.Dispose())); else _marshal.Dispose(); } catch { }
    }
}
