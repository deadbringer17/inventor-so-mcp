using System;
using System.Collections.Generic;
using System.Text;

namespace InventorXrSo.Core.Net
{
    public sealed class TransportRequest
    {
        public TransportRequest(string method, string url)
        {
            Method = method;
            Url = url;
        }

        public string Method { get; }
        public string Url { get; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; set; }
        private long _maxResponseBytes;
        /// <summary>Maximum received body size, enforced during download. Zero preserves unbounded requests.</summary>
        public long MaxResponseBytes
        {
            get => _maxResponseBytes;
            set
            {
                if (value < 0 || value > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
                _maxResponseBytes = value;
            }
        }
        /// <summary>Whole-request timeout; <c>Timeout.InfiniteTimeSpan</c> for long-lived streams.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        public static TransportRequest Json(string method, string url, string json)
        {
            var request = new TransportRequest(method, url) { Body = Encoding.UTF8.GetBytes(json) };
            request.Headers["Content-Type"] = "application/json";
            return request;
        }
    }
}
