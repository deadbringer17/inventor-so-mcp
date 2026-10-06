using System.IO;
using System.IO.Pipes;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Transport;

namespace Bimwright.Ipt.Tests;

public sealed class NdjsonLineBufferBlockTests
{
    private static StreamReader Reader(string s) => new(new MemoryStream(Encoding.UTF8.GetBytes(s)), Encoding.UTF8);

    [Fact]
    public void MultipleLongLinesKeepOrderAcrossBlockBoundaries()
    {
        // Lines much longer than the internal block and of awkward lengths, so newlines land everywhere.
        var expected = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            var line = new string((char)('a' + i), 5000 + i * 3137);
            expected.Add(line);
            sb.Append(line).Append(i % 2 == 0 ? "\n" : "\r\n");
        }
        var buf = new NdjsonLineBuffer(Reader(sb.ToString()));
        foreach (var e in expected)
        {
            Assert.Equal(e, buf.ReadLine(1_000_000, out var o));
            Assert.False(o);
        }
        Assert.Null(buf.ReadLine(1_000_000, out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncMultipleLongLinesKeepOrder()
    {
        var expected = Enumerable.Range(0, 8).Select(i => new string((char)('A' + i), 20000 + i * 911)).ToList();
        var buf = new NdjsonLineBuffer(Reader(string.Join("\n", expected) + "\n"));
        foreach (var e in expected)
            Assert.Equal(e, (await buf.ReadLineAsync(1_000_000)).Line);
        Assert.Null((await buf.ReadLineAsync(1_000_000)).Line);
    }

    [Fact]
    public void StaticApiKeepsLookAheadBetweenCalls()
    {
        var r = Reader(new string('p', 40000) + "\nsecond\nthird");
        Assert.Equal(40000, NdjsonLineReader.ReadLineBounded(r, 100000, out _)!.Length);
        Assert.Equal("second", NdjsonLineReader.ReadLineBounded(r, 100000, out _));
        Assert.Equal("third", NdjsonLineReader.ReadLineBounded(r, 100000, out _));
    }

    [Fact]
    public void LimitIsCountedInUtf8BytesNotChars()
    {
        var line = new string('à', 10); // 10 chars, 20 bytes
        Assert.Equal(line, NdjsonLineReader.ReadLineBounded(Reader(line + "\n"), 20, out var ok));
        Assert.False(ok);
        Assert.Null(NdjsonLineReader.ReadLineBounded(Reader(line + "\n"), 19, out var over));
        Assert.True(over);
        // Euro sign (3 bytes) and an emoji surrogate pair (4 bytes).
        var mixed = "€\U0001F600";
        Assert.Equal(mixed, NdjsonLineReader.ReadLineBounded(Reader(mixed + "\n"), 7, out var m1));
        Assert.False(m1);
        NdjsonLineReader.ReadLineBounded(Reader(mixed + "\n"), 6, out var m2);
        Assert.True(m2);
    }

    [Fact]
    public void OverflowDiscardsRemainderOfThatLineOnly()
    {
        var r = Reader(new string('x', 50000) + "\nok\n");
        NdjsonLineReader.ReadLineBounded(r, 100, out var over);
        Assert.True(over);
        Assert.Equal("ok", NdjsonLineReader.ReadLineBounded(r, 100, out var o2));
        Assert.False(o2);
    }

    [Fact]
    public void FiveMegabyteLineIsReadQuickly()
    {
        var big = new string('q', 5 * 1024 * 1024);
        var r = Reader(big + "\nnext\n");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var line = NdjsonLineReader.ReadLineBounded(r, 8 * 1024 * 1024, out var o);
        sw.Stop();
        Assert.False(o);
        Assert.Equal(big.Length, line!.Length);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal("next", NdjsonLineReader.ReadLineBounded(r, 100, out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task FiveMegabyteLineIsReadQuicklyAsync()
    {
        var big = new string('q', 5 * 1024 * 1024);
        var r = Reader(big + "\n");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await NdjsonLineReader.ReadLineBoundedAsync(r, 8 * 1024 * 1024);
        sw.Stop();
        Assert.Equal(big.Length, res.Line!.Length);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
    }
}

public sealed class PipeTransportServerIdleTests
{
    private const string Token = "tok-secret";
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(250);

    private static (PipeTransportServer server, string name) Start()
    {
        var name = "BimwrightTest-" + Guid.NewGuid().ToString("N");
        var server = new PipeTransportServer(name, Token, Idle);
        server.Start((line, tcs) => tcs.TrySetResult("{\"ok\":true}"));
        return (server, name);
    }

    private static NamedPipeClientStream Connect(string name, int timeoutMs = 5000)
    {
        var c = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        c.Connect(timeoutMs);
        return c;
    }

    private static string RoundTrip(NamedPipeClientStream c)
    {
        var w = new StreamWriter(c, new UTF8Encoding(false)) { AutoFlush = true };
        w.WriteLine("{\"id\":\"" + Guid.NewGuid() + "\",\"auth_token\":\"" + Token + "\",\"command\":\"x\"}");
        var r = new StreamReader(c, Encoding.UTF8);
        var task = System.Threading.Tasks.Task.Run(() => r.ReadLine());
        Assert.True(task.Wait(5000), "no reply");
        return task.Result!;
    }

    [Fact]
    public void SilentClientIsDroppedAndNextClientServed()
    {
        var (server, name) = Start();
        using (server)
        {
            using var silent = Connect(name);
            // The only instance is held by the silent client; the second client waits for the idle timeout.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var second = Connect(name, 8000);
            Assert.True(sw.ElapsedMilliseconds < 6000);
            Assert.Contains("true", RoundTrip(second));
        }
    }

    [Fact]
    public void PartialLineWithoutNewlineIsDisconnectedAfterIdle()
    {
        var (server, name) = Start();
        using (server)
        {
            using var c = Connect(name);
            var bytes = Encoding.UTF8.GetBytes("{\"id\":\"partial");
            c.Write(bytes, 0, bytes.Length);
            c.Flush();
            var readTask = System.Threading.Tasks.Task.Run(() => c.ReadByte());
            Assert.True(readTask.Wait(5000), "server did not drop the idle client");
            Assert.Equal(-1, readTask.Result);
            using var second = Connect(name, 5000);
            Assert.Contains("true", RoundTrip(second));
        }
    }

    [Fact]
    public void StopReturnsPromptlyWhileClientIsIdleConnected()
    {
        var name = "BimwrightTest-" + Guid.NewGuid().ToString("N");
        var server = new PipeTransportServer(name, Token, TimeSpan.FromSeconds(60));
        server.Start((line, tcs) => tcs.TrySetResult("{}"));
        using var c = Connect(name);
        System.Threading.Thread.Sleep(100);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        server.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Stop took {sw.ElapsedMilliseconds} ms");
        var readTask = System.Threading.Tasks.Task.Run(() => c.ReadByte());
        Assert.True(readTask.Wait(2000), "client was not disconnected by Stop");
        Assert.Equal(-1, readTask.Result);
    }

    [Fact]
    public void StopReturnsPromptlyWhileWaitingForConnection()
    {
        var name = "BimwrightTest-" + Guid.NewGuid().ToString("N");
        var server = new PipeTransportServer(name, Token, TimeSpan.FromSeconds(60));
        server.Start((line, tcs) => tcs.TrySetResult("{}"));
        System.Threading.Thread.Sleep(100);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        server.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public void NormalRequestsStillWorkAndConnectionStaysOpenBetweenLines()
    {
        var (server, name) = Start();
        using (server)
        {
            using var c = Connect(name);
            Assert.Contains("true", RoundTrip(c));
            Assert.Contains("true", RoundTrip(c));
        }
    }
}
