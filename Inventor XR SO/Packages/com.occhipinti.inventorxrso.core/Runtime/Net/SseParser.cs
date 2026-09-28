using System;
using System.Collections.Generic;
using System.Text;

namespace InventorXrSo.Core.Net
{
    /// <summary>Server-sent events: feeds lines, emits the data of each event (only "data" fields matter here).</summary>
    public sealed class SseParser
    {
        private readonly Action<string> _onData;
        private readonly StringBuilder _data = new StringBuilder();
        private bool _hasData;

        public SseParser(Action<string> onData) { _onData = onData; }

        public void Feed(string line)
        {
            if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);
            if (line.Length == 0)
            {
                Flush();
                return;
            }
            if (line[0] == ':') return;
            int colon = line.IndexOf(':');
            var field = colon < 0 ? line : line.Substring(0, colon);
            var value = colon < 0 ? "" : line.Substring(colon + 1);
            if (value.StartsWith(" ")) value = value.Substring(1);
            if (field != "data") return;
            if (_hasData) _data.Append('\n');
            _data.Append(value);
            _hasData = true;
        }

        public void Flush()
        {
            if (!_hasData) return;
            var data = _data.ToString();
            _data.Clear();
            _hasData = false;
            _onData(data);
        }

        public static IReadOnlyList<string> DataPayloads(string text)
        {
            var list = new List<string>();
            var parser = new SseParser(list.Add);
            foreach (var line in text.Split('\n')) parser.Feed(line);
            parser.Flush();
            return list;
        }
    }
}
