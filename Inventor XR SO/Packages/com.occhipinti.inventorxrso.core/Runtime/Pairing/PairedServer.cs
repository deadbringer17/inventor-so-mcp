using System;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>A PC this headset is paired with: where it is, which certificate, which token.</summary>
    public sealed class PairedServer
    {
        public PairedServer(string host, int port, string certSha256, string clientName, string token)
        {
            Host = host;
            Port = port;
            CertSha256 = certSha256;
            ClientName = clientName;
            Token = token;
        }

        public string Host { get; }
        public int Port { get; }
        public string CertSha256 { get; }
        public string ClientName { get; }
        public string Token { get; }
        public string BaseUrl => MakeBaseUrl(Host, Port);

        public static string MakeBaseUrl(string host, int port) =>
            "https://" + (host.Contains(":") && !host.StartsWith("[") ? "[" + host + "]" : host) + ":" + port;

        public string ToJson() => new JObject
        {
            ["host"] = Host, ["port"] = Port, ["cert_sha256"] = CertSha256, ["client_name"] = ClientName, ["token"] = Token,
        }.ToString(Formatting.None);

        public static PairedServer FromJson(string json)
        {
            try
            {
                var o = JObject.Parse(json);
                var host = (string)o["host"];
                var port = (int?)o["port"] ?? 0;
                var sha = (string)o["cert_sha256"];
                var client = (string)o["client_name"];
                var token = (string)o["token"];
                if (string.IsNullOrEmpty(host) || port < 1 || port > 65535 || !CertificatePin.IsValid(sha) ||
                    string.IsNullOrEmpty(client) || string.IsNullOrEmpty(token))
                    throw new FormatException("Incomplete stored pairing.");
                return new PairedServer(host, port, CertificatePin.Normalize(sha), client, token);
            }
            catch (JsonReaderException ex) { throw new FormatException("Unreadable stored pairing.", ex); }
        }
    }
}
