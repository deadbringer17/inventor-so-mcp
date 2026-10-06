using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Bimwright.Ipt.Shared.Contracts;

public readonly struct BoundedLineReadResult
{
    public BoundedLineReadResult(string? line, bool overflow)
    {
        Line = line;
        Overflow = overflow;
    }

    public string? Line { get; }
    public bool Overflow { get; }
}

/// <summary>
/// Reads bounded NDJSON lines from a <see cref="StreamReader"/> in blocks. It keeps its own char buffer, so
/// the characters after a newline are not lost between calls: use ONE instance per stream/connection for every
/// line read from it (do not mix with <c>reader.Read*</c> on the same StreamReader afterwards).
/// <para>
/// The limit is in UTF-8 bytes of the line (CR characters are stripped and not counted). A line whose size
/// reaches <c>max + 1</c> bytes reports overflow without buffering the rest; the remainder of that line is
/// discarded lazily, up to its newline, at the start of the next read.
/// </para>
/// </summary>
public sealed class NdjsonLineBuffer
{
    private const int BlockSize = 16 * 1024;

    private readonly StreamReader _reader;
    private readonly char[] _buf = new char[BlockSize];
    private int _pos;
    private int _len;
    private bool _eof;
    private bool _discarding;

    private enum Step { NeedMore, Line, Overflow }

    public NdjsonLineBuffer(StreamReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public string? ReadLine(int maxBytes, out bool overflow)
    {
        overflow = false;
        var sb = new StringBuilder();
        long bytes = 0;
        while (true)
        {
            if (_pos >= _len)
            {
                if (_eof) return Finish(sb);
                _len = _reader.Read(_buf, 0, _buf.Length);
                _pos = 0;
                if (_len <= 0) { _len = 0; _eof = true; return Finish(sb); }
            }
            var step = Consume(sb, ref bytes, maxBytes);
            if (step == Step.Line) return sb.ToString();
            if (step == Step.Overflow) { overflow = true; return null; }
        }
    }

    public async Task<BoundedLineReadResult> ReadLineAsync(int maxBytes)
    {
        var sb = new StringBuilder();
        long bytes = 0;
        while (true)
        {
            if (_pos >= _len)
            {
                if (_eof) return new BoundedLineReadResult(Finish(sb), false);
                _len = await _reader.ReadAsync(_buf, 0, _buf.Length).ConfigureAwait(false);
                _pos = 0;
                if (_len <= 0) { _len = 0; _eof = true; return new BoundedLineReadResult(Finish(sb), false); }
            }
            var step = Consume(sb, ref bytes, maxBytes);
            if (step == Step.Line) return new BoundedLineReadResult(sb.ToString(), false);
            if (step == Step.Overflow) return new BoundedLineReadResult(null, true);
        }
    }

    private static string? Finish(StringBuilder sb) => sb.Length == 0 ? null : sb.ToString();

    private Step Consume(StringBuilder sb, ref long bytes, int maxBytes)
    {
        int nl = Array.IndexOf(_buf, '\n', _pos, _len - _pos);
        int end = nl < 0 ? _len : nl;

        if (_discarding)
        {
            if (nl < 0) { _pos = _len; return Step.NeedMore; }
            _pos = nl + 1;
            _discarding = false;
            return Step.NeedMore; // swallowed the tail of an oversized line; the next line starts fresh
        }

        bool hasCr = false;
        for (int i = _pos; i < end; i++)
        {
            char c = _buf[i];
            if (c == '\r') { hasCr = true; continue; }
            // UTF-8 size: a surrogate pair (2 chars) is 4 bytes, so 2 per surrogate char.
            bytes += c < 0x80 ? 1 : c < 0x800 ? 2 : (c >= 0xD800 && c <= 0xDFFF) ? 2 : 3;
            if (bytes > maxBytes)
            {
                if (nl >= 0) { _pos = nl + 1; }
                else { _pos = _len; _discarding = true; }
                return Step.Overflow;
            }
        }

        if (!hasCr) sb.Append(_buf, _pos, end - _pos);
        else
        {
            for (int i = _pos; i < end; i++)
                if (_buf[i] != '\r') sb.Append(_buf[i]);
        }

        if (nl >= 0) { _pos = nl + 1; return Step.Line; }
        _pos = _len;
        return Step.NeedMore;
    }
}

/// <summary>
/// Static convenience API. A reader that is reused across calls keeps its look-ahead in an
/// <see cref="NdjsonLineBuffer"/> attached to that StreamReader, so consecutive calls never lose data. Servers that
/// own the connection loop should hold an <see cref="NdjsonLineBuffer"/> directly.
/// </summary>
public static class NdjsonLineReader
{
    private static readonly ConditionalWeakTable<StreamReader, NdjsonLineBuffer> Buffers = new();

    private static NdjsonLineBuffer For(StreamReader reader)
        => Buffers.GetValue(reader, r => new NdjsonLineBuffer(r));

    public static string? ReadLineBounded(StreamReader reader, int maxBytes, out bool overflow)
        => For(reader).ReadLine(maxBytes, out overflow);

    public static Task<BoundedLineReadResult> ReadLineBoundedAsync(StreamReader reader, int maxBytes)
        => For(reader).ReadLineAsync(maxBytes);
}
