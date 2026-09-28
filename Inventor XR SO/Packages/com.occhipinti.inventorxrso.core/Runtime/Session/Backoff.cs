using System;

namespace InventorXrSo.Core.Session
{
    /// <summary>Reconnection delays: 1, 2, 4, 8, 16, then 30 s.</summary>
    public sealed class Backoff
    {
        private static readonly int[] Seconds = { 1, 2, 4, 8, 16, 30 };
        private int _attempt;

        public TimeSpan Next() => TimeSpan.FromSeconds(Seconds[Math.Min(_attempt++, Seconds.Length - 1)]);
        public void Reset() => _attempt = 0;
    }
}
