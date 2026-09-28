using System;
using System.Globalization;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>The PC address typed on the headset: host, host:port or [ipv6]:port.</summary>
    public static class PairingAddress
    {
        public const int DefaultPort = 8443;

        public static bool TryParse(string text, out string host, out int port)
        {
            host = null;
            port = DefaultPort;
            var value = (text ?? "").Trim();
            if (value.Length == 0) return false;
            if (value.IndexOfAny(new[] { '/', '\\', '@', '?', '#', ' ', '\t', '\r', '\n' }) >= 0) return false;
            string portText = null;
            if (value.StartsWith("["))
            {
                int close = value.IndexOf(']');
                if (close < 0) return false;
                host = value.Substring(1, close - 1);
                if (close + 1 < value.Length)
                {
                    if (value[close + 1] != ':') return false;
                    portText = value.Substring(close + 2);
                }
            }
            else
            {
                int colon = value.LastIndexOf(':');
                if (colon != value.IndexOf(':')) return false; // IPv6 must be bracketed.
                host = colon < 0 ? value : value.Substring(0, colon);
                if (colon >= 0) portText = value.Substring(colon + 1);
            }
            if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown) return false;
            if (value.StartsWith("[") && Uri.CheckHostName(host) != UriHostNameType.IPv6) return false;
            if (portText != null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)) return false;
            return true;
        }
    }
}
