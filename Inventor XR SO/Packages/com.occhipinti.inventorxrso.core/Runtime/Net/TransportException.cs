using System;

namespace InventorXrSo.Core.Net
{
    /// <summary>The PC could not be reached (network, TLS, timeout).</summary>
    public class TransportException : Exception
    {
        public TransportException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>The request ran out of time (the per-request <see cref="TransportRequest.Timeout"/>), as opposed to a refused or lost connection.</summary>
    public sealed class TransportTimeoutException : TransportException
    {
        public TransportTimeoutException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>The server presented a certificate other than the pinned one.</summary>
    public sealed class CertificateRejectedException : TransportException
    {
        public CertificateRejectedException(string presentedSha256)
            : base("The PC presented a different certificate than the one paired.")
        {
            PresentedSha256 = presentedSha256;
        }

        public string PresentedSha256 { get; }
    }
}
