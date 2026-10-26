using System;

namespace InventorXrSo.Core.Experience
{
    public enum Activity { Home, Capture, Design, Presentation, Verification }

    /// <summary>User intent is independent of document kind, connection and MR/VR environment.</summary>
    public sealed class ActivityController
    {
        public Activity Current { get; private set; } = Activity.Home;
        public Activity? Pending { get; private set; }
        public int Generation { get; private set; }
        public event Action Changed;

        public bool Request(Activity target, bool protectedWork, bool needsConnection = false)
        {
            if (!Enum.IsDefined(typeof(Activity), target)) throw new ArgumentOutOfRangeException(nameof(target));
            if (protectedWork) return false;
            if (needsConnection) { Pending = target; return false; }
            Pending = null;
            if (Current == target) return true;
            Current = target;
            Generation++;
            Changed?.Invoke();
            return true;
        }

        public void CancelPending() => Pending = null;
    }
}
