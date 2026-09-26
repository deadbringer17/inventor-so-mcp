namespace InventorXrSo.Core.Net
{
    /// <summary>
    /// Which server certificate a transport accepts: exactly the pinned one, or (only to read the
    /// fingerprint before manual pairing) any, remembering what was presented.
    /// </summary>
    public sealed class ServerTrust
    {
        private ServerTrust(string pinnedSha256) { PinnedSha256 = pinnedSha256; }

        public static ServerTrust Pinned(string sha256Hex) => new ServerTrust(CertificatePin.Normalize(sha256Hex));
        public static ServerTrust FirstUse() => new ServerTrust(null);

        public string PinnedSha256 { get; }
        public string LastPresentedSha256 { get; private set; }
        public bool LastRejected { get; private set; }

        public bool Validate(byte[] der)
        {
            var presented = der == null ? null : CertificatePin.Sha256Hex(der);
            LastPresentedSha256 = presented;
            bool ok = presented != null && (PinnedSha256 == null || CertificatePin.FixedTimeEquals(presented, PinnedSha256));
            LastRejected = !ok;
            return ok;
        }
    }
}
