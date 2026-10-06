using System;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>What the PC's pairing QR code carries.</summary>
    public sealed class PairingPayload
    {
        public PairingPayload(string host, int port, string oneTimeToken, string certSha256)
        {
            Host = host;
            Port = port;
            OneTimeToken = oneTimeToken;
            CertSha256 = certSha256;
        }

        public string Host { get; }
        public int Port { get; }
        public string OneTimeToken { get; }
        public string CertSha256 { get; }

        public static PairingPayload Parse(string text)
        {
            JObject json;
            try { json = JObject.Parse(text ?? ""); }
            catch (JsonReaderException) { throw new FormatException("The QR code is not an Inventor SO pairing code."); }
            try
            {
                if ((int?)json["v"] != 1) throw new FormatException("Unsupported pairing code version.");
                var host = (string)json["host"];
                var portToken = json["port"];
                long port = portToken != null && portToken.Type == JTokenType.Integer ? (long)portToken : 0;
                var ott = (string)json["ott"];
                var sha = (string)json["cert_sha256"];
                if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535 || string.IsNullOrEmpty(ott))
                    throw new FormatException("Incomplete pairing code.");
                if (host.IndexOfAny(new[] { '/', '\\', '@', '?', '#', ' ', '\t', '\r', '\n' }) >= 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
                    throw new FormatException("Invalid host in the pairing code.");
                if (!CertificatePin.IsValid(sha)) throw new FormatException("Invalid certificate fingerprint in the pairing code.");
                return new PairingPayload(host, (int)port, ott, CertificatePin.Normalize(sha));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is InvalidCastException) { throw new FormatException("Malformed pairing code.", ex); }
        }
    }
}
