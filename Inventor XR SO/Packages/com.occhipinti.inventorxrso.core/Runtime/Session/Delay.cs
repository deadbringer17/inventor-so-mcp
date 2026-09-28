using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Session
{
    /// <summary>Waiting, injectable so tests do not sleep.</summary>
    public interface IDelay
    {
        Task Delay(TimeSpan duration, CancellationToken ct);
    }

    public sealed class TaskDelay : IDelay
    {
        public Task Delay(TimeSpan duration, CancellationToken ct) => Task.Delay(duration, ct);
    }
}
