using System;
using System.Collections.Generic;
using System.Text;

namespace InventorXrSo.Core.Net
{
    public sealed class TransportResponse
    {
        private readonly Dictionary<string, string> _headers;

        public TransportResponse(int status, IDictionary<string, string> headers, byte[] body)
        {
            Status = status;
            _headers = new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            Body = body ?? Array.Empty<byte>();
        }

        public int Status { get; }
        public byte[] Body { get; }
        public string Text => Encoding.UTF8.GetString(Body);
        public bool IsSuccess => Status >= 200 && Status < 300;
        public string Header(string name) => _headers.TryGetValue(name, out var value) ? value : null;
    }
}
