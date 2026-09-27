using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>Hands each received line to a callback as bytes arrive (for SSE); stores nothing.</summary>
    internal sealed class LineDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<string> _onLine;
        private readonly List<byte> _pending = new List<byte>();

        public LineDownloadHandler(Action<string> onLine) : base(new byte[16 * 1024]) { _onLine = onLine; }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            for (int i = 0; i < dataLength; i++)
            {
                if (data[i] == (byte)'\n')
                {
                    _onLine(Encoding.UTF8.GetString(_pending.ToArray()));
                    _pending.Clear();
                }
                else _pending.Add(data[i]);
            }
            return true;
        }

        protected override void CompleteContent()
        {
            if (_pending.Count == 0) return;
            _onLine(Encoding.UTF8.GetString(_pending.ToArray()));
            _pending.Clear();
        }
    }
}
