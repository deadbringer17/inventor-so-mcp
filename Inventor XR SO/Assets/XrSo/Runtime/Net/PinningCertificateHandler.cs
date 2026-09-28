using InventorXrSo.Core.Net;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>Accepts only the certificate the trust pins (hostname is not checked: the pin is the identity).</summary>
    internal sealed class PinningCertificateHandler : CertificateHandler
    {
        private readonly ServerTrust _trust;
        public PinningCertificateHandler(ServerTrust trust) { _trust = trust; }
        protected override bool ValidateCertificate(byte[] certificateData) => _trust.Validate(certificateData);
    }
}
