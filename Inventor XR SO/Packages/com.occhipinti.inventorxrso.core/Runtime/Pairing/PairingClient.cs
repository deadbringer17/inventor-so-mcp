using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>
    /// Pairs the headset with a PC. The QR path pins the fingerprint from the code; the manual path
    /// first reads the fingerprint (no secret sent), lets the user compare it with the PC console,
    /// then sends the 6-digit code over the pinned connection.
    /// </summary>
    public sealed class PairingClient
    {
        private readonly Func<ServerTrust, IHttpTransport> _transportFactory;

        public PairingClient(Func<ServerTrust, IHttpTransport> transportFactory) { _transportFactory = transportFactory; }

        public Task<PairedServer> PairWithQrAsync(PairingPayload payload, string deviceName, CancellationToken ct) =>
            PairAsync(payload.Host, payload.Port, payload.OneTimeToken, ServerTrust.Pinned(payload.CertSha256), deviceName, ct);

        /// <summary>SHA-256 of the certificate the PC presents; sends nothing but a health check.</summary>
        public async Task<string> ProbeFingerprintAsync(string host, int port, CancellationToken ct)
        {
            var trust = ServerTrust.FirstUse();
            await _transportFactory(trust).SendAsync(new TransportRequest("GET", PairedServer.MakeBaseUrl(host, port) + "/healthz"), ct);
            if (trust.LastPresentedSha256 == null) throw new TransportException("The PC did not present a certificate.");
            return trust.LastPresentedSha256;
        }

        public async Task<PairedServer> PairAsync(string host, int port, string secret, ServerTrust trust, string deviceName, CancellationToken ct)
        {
            var body = new JObject { ["secret"] = secret, ["device_name"] = deviceName }.ToString(Formatting.None);
            var response = await _transportFactory(trust).SendAsync(
                TransportRequest.Json("POST", PairedServer.MakeBaseUrl(host, port) + "/pair", body), ct);
            JObject json = null;
            try { json = JObject.Parse(response.Text); }
            catch (JsonReaderException) { }
            if (!response.IsSuccess)
                throw new PairingException((string)json?["error"]?["code"] ?? "PAIRING_FAILED",
                    (string)json?["error"]?["message"] ?? "Pairing failed (HTTP " + response.Status + ").");
            var client = (string)json?["client_name"];
            var token = (string)json?["token"];
            if (string.IsNullOrEmpty(client) || string.IsNullOrEmpty(token))
                throw new PairingException("PAIRING_FAILED", "The PC answered without a token.");
            return new PairedServer(host, port, trust.LastPresentedSha256, client, token);
        }
    }
}
