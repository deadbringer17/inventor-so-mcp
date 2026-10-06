using System.IO;
using System.Net.Sockets;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class MeshPayloadHardeningTests
{
    private static JObject Valid() => MeshPayload.ToJson(GlbBuilderTests.Body());

    [Theory]
    [InlineData(int.MaxValue, 3)]
    [InlineData(3, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void FaceRangeThatOverflowsIntIsRejected(int first, int count)
    {
        var json = Valid();
        json["faces"]![0]!["first_index"] = first;
        json["faces"]![0]!["index_count"] = count - count % 3;
        if (count % 3 != 0) json["faces"]![0]!["index_count"] = 3 * (count / 3);
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }

    [Fact]
    public void FirstIndexMaxValueWithThreeIndicesIsRejected()
    {
        var json = Valid();
        json["faces"]![0]!["first_index"] = int.MaxValue;
        json["faces"]![0]!["index_count"] = 3;
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }

    [Fact]
    public void NormalsLengthMismatchIsRejected()
    {
        var json = Valid();
        json["normals"] = MeshPayload.EncodeFloats(new float[] { 0, 0, 1 });
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }

    [Fact]
    public void PositionsNotTriplesAreRejected()
    {
        var json = Valid();
        json["normals"] = MeshPayload.EncodeFloats(new float[0]);
        json["positions"] = MeshPayload.EncodeFloats(new float[] { 0, 1, 2, 3 });
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }

    [Fact]
    public void InvalidBase64IsRejected()
    {
        var json = Valid();
        json["positions"] = "not*base64!!";
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }
}

public sealed class NdjsonLineReaderTests
{
    private static StreamReader Reader(string s) => new(new MemoryStream(Encoding.UTF8.GetBytes(s)), Encoding.UTF8);

    [Fact]
    public void CrLfIsStripped()
    {
        var r = Reader("abc\r\ndef\r\n");
        Assert.Equal("abc", NdjsonLineReader.ReadLineBounded(r, 100, out var o1));
        Assert.False(o1);
        Assert.Equal("def", NdjsonLineReader.ReadLineBounded(r, 100, out _));
        Assert.Null(NdjsonLineReader.ReadLineBounded(r, 100, out _));
    }

    [Fact]
    public void TrailingLineWithoutNewlineIsReturnedAtEof()
    {
        var r = Reader("one\ntwo");
        Assert.Equal("one", NdjsonLineReader.ReadLineBounded(r, 100, out _));
        Assert.Equal("two", NdjsonLineReader.ReadLineBounded(r, 100, out _));
        Assert.Null(NdjsonLineReader.ReadLineBounded(r, 100, out _));
    }

    [Fact]
    public void EmptyStreamYieldsNull()
        => Assert.Null(NdjsonLineReader.ReadLineBounded(Reader(""), 100, out _));

    [Fact]
    public void ExactlyMaxIsAcceptedAndMaxPlusOneOverflows()
    {
        Assert.Equal(new string('x', 10), NdjsonLineReader.ReadLineBounded(Reader(new string('x', 10) + "\n"), 10, out var ok));
        Assert.False(ok);
        Assert.Null(NdjsonLineReader.ReadLineBounded(Reader(new string('x', 11) + "\n"), 10, out var over));
        Assert.True(over);
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncVariantMatchesSyncBehaviour()
    {
        var r = Reader("ab\r\n" + new string('y', 5) + "\n" + new string('z', 6) + "\n");
        Assert.Equal("ab", (await NdjsonLineReader.ReadLineBoundedAsync(r, 5)).Line);
        var five = await NdjsonLineReader.ReadLineBoundedAsync(r, 5);
        Assert.Equal(new string('y', 5), five.Line);
        Assert.False(five.Overflow);
        Assert.True((await NdjsonLineReader.ReadLineBoundedAsync(r, 5)).Overflow);
        Assert.Null((await NdjsonLineReader.ReadLineBoundedAsync(Reader(""), 5)).Line);
    }
}

public sealed class CadEventJournalRevisionTests
{
    [Theory]
    [InlineData("nocolon")]
    [InlineData(":5")]
    [InlineData("x:")]
    public void MalformedTokensAreRejected(string token)
    {
        var j = new CadEventJournal();
        j.Append("document_changed", "doc");
        Assert.False(j.TryRestoreRevision("doc", token));
    }

    [Theory]
    [InlineData(":")]
    [InlineData(":-1")]
    [InlineData(":x")]
    [InlineData(":")]
    public void BadRevisionNumberIsRejected(string suffix)
    {
        var j = new CadEventJournal();
        j.Append("document_changed", "doc");
        string before = j.Revision("doc");
        Assert.False(j.TryRestoreRevision("doc", j.Epoch + suffix));
        Assert.Equal(before, j.Revision("doc"));
    }

    [Fact]
    public void NullDocumentIdOrRevisionIsRejected()
    {
        var j = new CadEventJournal();
        Assert.False(j.TryRestoreRevision(null!, j.Epoch + ":0"));
        Assert.False(j.TryRestoreRevision("doc", null!));
    }

    [Fact]
    public void ReadAfterCursorOrOtherEpochForcesResync()
    {
        var j = new CadEventJournal();
        j.Append("document_changed", "doc");
        long cursor = (long)j.Read()["cursor"]!;
        Assert.True((bool)j.Read(cursor + 1, j.Epoch)["resync_required"]!);
        Assert.True((bool)j.Read(cursor + 100)["resync_required"]!);
        Assert.True((bool)j.Read(0, "other-epoch")["resync_required"]!);
        Assert.False((bool)j.Read(cursor, j.Epoch)["resync_required"]!);
    }
}

public sealed class TargetRegistryRobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inv-reg2-" + Guid.NewGuid().ToString("N"));
    public TargetRegistryRobustnessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static int Self() => System.Diagnostics.Process.GetCurrentProcess().Id;

    [Fact]
    public void TruncatedAndMalformedJsonIsSkippedWithoutThrowing()
    {
        File.WriteAllText(Path.Combine(_dir, "trunc.json"), "{ \"target_id\": \"inventor-2025-1\", \"inventor_ye");
        File.WriteAllText(Path.Combine(_dir, "garbage.json"), "\0\0\0 not json");
        File.WriteAllText(Path.Combine(_dir, "empty.json"), "");
        File.WriteAllText(Path.Combine(_dir, "array.json"), "[1,2,3]");
        var good = new TargetDescriptor
        {
            TargetId = "inventor-2026-" + Self(), InventorYear = 2026, ProcessId = Self(), HostApp = "Inventor",
            Transport = "tcp", Port = 1, AuthToken = "t", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(Path.Combine(_dir, "good.json"), JsonConvert.SerializeObject(good));
        var list = new TargetRegistry(_dir).List();
        Assert.Single(list);
        Assert.Equal(good.TargetId, list[0].TargetId);
    }

    [Fact]
    public void UpdateDocumentPersistsTitleAndPath()
    {
        var d = new TargetDescriptor
        {
            TargetId = "inventor-2026-" + Self(), InventorYear = 2026, ProcessId = Self(), HostApp = "Inventor",
            Transport = "tcp", Port = 1, AuthToken = "t", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        using var writer = new TargetDescriptorWriter(_dir, d);
        writer.Start("A.ipt", @"C:\x\A.ipt", System.Threading.Timeout.Infinite);
        writer.UpdateDocument("B.ipt", @"C:\x\B.ipt");
        var listed = new TargetRegistry(_dir).List();
        Assert.Single(listed);
        Assert.Equal("B.ipt", listed[0].DocumentTitle);
        Assert.Equal(@"C:\x\B.ipt", listed[0].DocumentPath);
    }
}

public sealed class TargetDescriptorWriterHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inv-wr-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private TargetDescriptor Descriptor() => new()
    {
        TargetId = "inventor-2026-777", InventorYear = 2026, ProcessId = 777, HostApp = "Inventor",
        Transport = "tcp", Port = 1, AuthToken = "t", LastHeartbeatUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void WriteAfterDisposeDoesNotRecreateTheFile()
    {
        var writer = new TargetDescriptorWriter(_dir, Descriptor());
        writer.Start("A.ipt", null, System.Threading.Timeout.Infinite);
        var path = writer.Path_;
        Assert.True(File.Exists(path));
        writer.Dispose();
        Assert.False(File.Exists(path));
        writer.WriteOnce();
        writer.UpdateDocument("B.ipt", null);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void HeartbeatTimerAfterDisposeDoesNotRecreateTheFile()
    {
        var writer = new TargetDescriptorWriter(_dir, Descriptor());
        writer.Start("A.ipt", null, 20);
        var path = writer.Path_;
        System.Threading.Thread.Sleep(80);
        writer.Dispose();
        System.Threading.Thread.Sleep(120);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RepeatedWritesLeaveNoTempFilesAndAValidDescriptor()
    {
        using var writer = new TargetDescriptorWriter(_dir, Descriptor());
        writer.Start("A.ipt", null, System.Threading.Timeout.Infinite);
        for (int i = 0; i < 20; i++) writer.UpdateDocument("D" + i, null);
        var files = Directory.GetFiles(_dir);
        Assert.Single(files);
        var back = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(files[0]))!;
        Assert.Equal("D19", back.DocumentTitle);
    }
}

public sealed class ListenBackoffTests
{
    [Fact]
    public void DelayDoublesFrom250msToTwoSecondsAndResets()
    {
        var b = new ListenBackoff();
        Assert.Equal(250, b.NextDelayMs());
        Assert.Equal(500, b.NextDelayMs());
        Assert.Equal(1000, b.NextDelayMs());
        Assert.Equal(2000, b.NextDelayMs());
        Assert.Equal(2000, b.NextDelayMs());
        b.Reset();
        Assert.Equal(250, b.NextDelayMs());
    }

    [Fact]
    public void WaitReturnsEarlyWhenStopRequested()
    {
        var b = new ListenBackoff();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        b.Wait(() => false);
        Assert.True(sw.ElapsedMilliseconds < 200);
    }
}

public sealed class TcpTransportServerProtocolTests
{
    private const string Token = "tok-secret";

    private static (TcpTransportServer server, TcpClient client, StreamReader reader, StreamWriter writer) Connect()
    {
        var server = new TcpTransportServer(Token);
        server.Start((line, tcs) => tcs.TrySetResult("{\"ok\":true}"));
        var client = new TcpClient();
        client.Connect(System.Net.IPAddress.Loopback, server.Port);
        client.ReceiveTimeout = 3000;
        var stream = client.GetStream();
        return (server, client, new StreamReader(stream, Encoding.UTF8), new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
    }

    [Fact]
    public void WrongAuthTokenIsUnauthorized()
    {
        var (server, client, reader, writer) = Connect();
        using (server) using (client)
        {
            writer.WriteLine("{\"id\":\"" + Guid.NewGuid() + "\",\"auth_token\":\"wrong\",\"command\":\"x\"}");
            var reply = JObject.Parse(reader.ReadLine()!);
            Assert.False((bool)reply["ok"]!);
            Assert.Equal(InventorErrorCodes.UNAUTHORIZED, (string)reply["error"]!["code"]!);
        }
    }

    [Fact]
    public void MalformedJsonLineGetsInvalidArgumentReply()
    {
        var (server, client, reader, writer) = Connect();
        using (server) using (client)
        {
            writer.WriteLine("{ this is not json");
            var reply = JObject.Parse(reader.ReadLine()!);
            Assert.False((bool)reply["ok"]!);
            Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, (string)reply["error"]!["code"]!);
            Assert.Equal("malformed request", (string)reply["error"]!["message"]!);
            Assert.Equal(Guid.Empty, Guid.Parse((string)reply["id"]!));

            // The connection stays usable after a malformed line.
            writer.WriteLine("{\"id\":\"" + Guid.NewGuid() + "\",\"auth_token\":\"" + Token + "\",\"command\":\"x\"}");
            var ok = JObject.Parse(reader.ReadLine()!);
            Assert.True((bool)ok["ok"]!);
        }
    }
}
