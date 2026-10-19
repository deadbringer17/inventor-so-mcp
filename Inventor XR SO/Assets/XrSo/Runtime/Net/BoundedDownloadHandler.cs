using System;
using System.IO;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>Rejects oversized bodies before retaining the overflowing chunk. The byte cap
    /// bounds payload and backing-array capacity, not native networking buffers or the final copy.</summary>
    public class BoundedDownloadHandler : DownloadHandlerScript
    {
        private readonly int _limit;
        private readonly MemoryStream _body = new MemoryStream();
        public bool LimitExceeded { get; private set; }
        public long ReceivedBytes => _body.Length;

        public BoundedDownloadHandler(long limit) : base(new byte[16 * 1024])
        {
            if (limit <= 0 || limit > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(limit));
            _limit = (int)limit;
        }

        protected override void ReceiveContentLengthHeader(ulong contentLength)
        {
            if (contentLength > (ulong)_limit) LimitExceeded = true;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (LimitExceeded) return false;
            if (dataLength == 0) return true;
            if (data == null || dataLength < 0 || dataLength > data.Length)
                return false;
            if (dataLength > _limit - _body.Length)
            {
                LimitExceeded = true;
                return false;
            }
            int needed = checked((int)_body.Length + dataLength);
            if (needed > _body.Capacity)
            {
                long growth = Math.Max(needed, Math.Max(256L, (long)_body.Capacity * 2));
                _body.Capacity = (int)Math.Min(_limit, growth);
            }
            _body.Write(data, 0, dataLength);
            return true;
        }

        protected override byte[] GetData()
        {
            if (LimitExceeded) throw new InvalidOperationException("Response body exceeds the configured byte limit.");
            return _body.ToArray();
        }

    }
}
