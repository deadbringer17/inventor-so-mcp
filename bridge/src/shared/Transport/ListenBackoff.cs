using System;
using System.Threading;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Delay between retries of a listen loop that keeps failing (250 ms doubling to 2 s), so a persistent
/// accept/create error does not spin a CPU core. <see cref="Reset"/> after a successful connection.
/// </summary>
internal sealed class ListenBackoff
{
    private const int InitialMs = 250;
    private const int MaxMs = 2000;
    private int _next = InitialMs;

    public int NextDelayMs()
    {
        int current = _next;
        _next = Math.Min(MaxMs, _next * 2);
        return current;
    }

    public void Reset() => _next = InitialMs;

    /// <summary>Sleeps for the next delay in short slices, returning early once <paramref name="keepRunning"/> is false.</summary>
    public void Wait(Func<bool> keepRunning)
    {
        int remaining = NextDelayMs();
        while (remaining > 0 && keepRunning())
        {
            int slice = Math.Min(50, remaining);
            Thread.Sleep(slice);
            remaining -= slice;
        }
    }
}
