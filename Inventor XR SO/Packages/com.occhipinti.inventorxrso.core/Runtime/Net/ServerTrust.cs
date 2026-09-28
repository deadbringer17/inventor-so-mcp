using System.Threading;

namespace InventorXrSo.Core.Net
{
    /// <summary>
    /// Which server certificate a transport accepts: exactly the pinned one, or (only to read the
    /// fingerprint before manual pairing) any, remembering what was presented.
    /// </summary>
    public sealed class ServerTrust
    {
        private int _rejectionCount;

        private ServerTrust(string pinnedSha256) { PinnedSha256 = pinnedSha256; }

        public static ServerTrust Pinned(string sha256Hex) => new ServerTrust(CertificatePin.Normalize(sha256Hex));
        public static ServerTrust FirstUse() => new ServerTrust(null);

        public string PinnedSha256 { get; }
        public string LastPresentedSha256 { get; private set; }

        /// <summary>
        /// How many times <see cref="Validate"/> has rejected a certificate on this trust. A transport
        /// reads this before sending and compares it after a failure: only a change means this specific
        /// request's handshake was the one that was rejected (a trust can be long-lived and shared
        /// across concurrent requests, so a single "last rejected" flag would be racy and stale).
        /// </summary>
        public int RejectionCount => Volatile.Read(ref _rejectionCount);

        public bool Validate(byte[] der)
        {
            var presented = der == null ? null : CertificatePin.Sha256Hex(der);
            LastPresentedSha256 = presented;
            bool ok = presented != null && (PinnedSha256 == null || CertificatePin.FixedTimeEquals(presented, PinnedSha256));
            if (!ok) Interlocked.Increment(ref _rejectionCount);
            return ok;
        }
    }
}
