using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SketchImageAssetsTests
{
    private sealed class Caller(string owner) : ICallerIdentity
    { public string Client => owner; public string? SessionId => "untrusted-session-not-used"; }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "s1-image-assets-" + Guid.NewGuid().ToString("N"));
        internal readonly FakeAddIn Fake;
        internal readonly InventorMcpConfig Config;
        internal readonly PluginClient Client;
        internal readonly AssetStore Assets;
        internal readonly SketchImageAssets Service;
        internal readonly List<(string Command, JObject Params, bool ReadOnly)> Requests = new();
        internal string Revision = "r7";
        internal JObject Frame = new()
        {
            ["schema"] = 1, ["provenance"] = "native_boundary_roles",
            ["corner_order"] = new JArray("top_left", "top_right", "bottom_right", "bottom_left"),
            ["corners_mm"] = new JArray(new JArray(0, 10), new JArray(20, 10), new JArray(20, 0), new JArray(0, 0))
        };
        internal Func<JObject, JObject, JObject>? Transform;
        internal Action? BeforeRevision;
        internal bool SwitchOnDiscard;
        internal readonly byte[] Source = new byte[70];
        internal Fixture(long maxAssetBytes = 4096)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(Source, 0x4d42);
            void I32(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(Source.AsSpan(at, 4), value);
            I32(2, 70); I32(10, 54); I32(14, 40); I32(18, 2); I32(22, -2); Source[26] = 1; Source[28] = 32; I32(34, 16);
            new byte[] { 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0 }.CopyTo(Source, 54);
            Fake = new FakeAddIn(Path.Combine(_directory, "targets"));
            Config = new InventorMcpConfig
            {
                DescriptorDirectory = Fake.DescriptorDirectory, AssetDirectory = Path.Combine(_directory, "assets"),
                AssetMaxBytes = maxAssetBytes, AssetTotalBytes = maxAssetBytes * 2, EnableExperimental = true,
                ReadOnly = true, TimeoutMs = 5000, PublicBaseUrl = "https://test.invalid/base/"
            };
            Client = new PluginClient(Config, null, null, TimeSpan.Zero); Assets = new AssetStore(Config);
            Service = new SketchImageAssets(Client, Assets, Config);
            Fake.Override = envelope =>
            {
                if (envelope.Command is not ("get_sketch_image_payload" or "get_visual_revision")) return null;
                var p = (JObject)envelope.Params.DeepClone(); Requests.Add((envelope.Command, p, envelope.ReadOnly));
                JObject reply;
                if (envelope.Command == "get_visual_revision")
                {
                    BeforeRevision?.Invoke();
                    reply = new JObject { ["document_id"] = "part", ["revision"] = Revision };
                }
                else
                {
                    string action = (string)p["action"]!;
                    reply = new JObject
                    {
                        ["transfer_id"] = "native_image", ["sha256"] = Convert.ToHexString(SHA256.HashData(Source)).ToLowerInvariant(),
                        ["byte_length"] = Source.Length
                    };
                    if (action == "begin")
                    {
                        reply["source_mode"] = "native_bitmap"; reply["format"] = "bmp"; reply["pixel_width"] = 2; reply["pixel_height"] = 2;
                        reply["max_chunk_bytes"] = 192 * 1024; reply["image_frame"] = Frame.DeepClone();
                        reply["width_mm"] = 20; reply["height_mm"] = 10; reply["visible"] = true; reply["transparent"] = false;
                    }
                    else if (action == "chunk")
                    {
                        reply["offset"] = p["offset"]!.DeepClone();
                        reply["data_base64"] = Convert.ToBase64String(Source, (int)p["offset"]!, (int)p["count"]!);
                    }
                    else
                    {
                        if (SwitchOnDiscard) SwitchAway();
                        reply = new JObject { ["discarded"] = true };
                    }
                    reply = Transform?.Invoke(p, reply) ?? reply;
                }
                return InventorCommandResult.Success(envelope.Id, reply, new());
            };
        }
        internal XrTools Tools(string owner = "alice") => new(Client, Assets, Config, new Caller(owner), new MeshCache(), Service);
        internal Task<JObject> Get(string owner = "alice", CancellationToken ct = default)
            => Service.GetAsync(owner, "part", "r7", "ent_sketch", "ent_image", ct);
        internal void SwitchAway()
        {
            string id = "inventor-2027-" + Environment.ProcessId + "-other";
            File.WriteAllText(Path.Combine(Fake.DescriptorDirectory, "other.json"), JsonConvert.SerializeObject(new TargetDescriptor
            {
                TargetId = id, InventorYear = 2027, ProcessId = Environment.ProcessId, HostApp = "Inventor", Transport = "pipe",
                PipeName = "absent-" + Guid.NewGuid().ToString("N"), AuthToken = "synthetic-test", LastHeartbeatUtc = DateTimeOffset.UtcNow
            }));
            Assert.True(Client.SwitchTarget(id));
        }
        public async ValueTask DisposeAsync()
        { await Fake.DisposeAsync(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private static byte[] DecodeScanlines(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < png.Length;)
        {
            int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            Assert.InRange(count, 0, png.Length - offset - 12);
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT") compressed.Write(png, offset + 8, count);
            offset += count + 12;
        }
        compressed.Position = 0; using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream(); zlib.CopyTo(decoded); return decoded.ToArray();
    }

    [Fact]
    public async Task ToolPublishesActualNativePngForAuthenticatedOwnerWithRevisionScopedFrame()
    {
        await using var f = new Fixture();
        var response = JObject.Parse(await f.Tools().SketchImage("part", "r7", "ent_sketch", "ent_image"));
        Assert.Equal("native_bitmap", (string?)response["source_kind"]);
        Assert.Equal("part", (string?)response["document_id"]); Assert.Equal("r7", (string?)response["revision"]);
        Assert.Equal(20, (double)response["width_mm"]!); Assert.Equal(10, (double)response["height_mm"]!);
        Assert.True(JToken.DeepEquals(f.Frame, response["image_frame"])); Assert.False((bool)response["transparent"]!);
        string id = (string)response["asset"]!["asset_id"]!;
        Assert.Equal("/assets/" + id, (string?)response["asset"]!["asset_url"]);
        Assert.True(f.Assets.TryRead(id, "alice", out var asset, out var png));
        Assert.False(f.Assets.TryRead(id, "foreign", out _, out _));
        Assert.Equal("image/png", asset!.MimeType);
        Assert.Equal(new[] { "kind", "pixel_height", "pixel_width" }, asset.Metadata.Properties().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 255 }, DecodeScanlines(png!));
        Assert.All(f.Requests, request => Assert.True(request.ReadOnly));
        Assert.All(f.Requests.Where(r => r.Command == "get_sketch_image_payload"), r =>
        { Assert.Equal("alice", (string?)r.Params["owner"]); Assert.Equal("native_bitmap", (string?)r.Params["source_mode"]); });
        Assert.Equal("get_visual_revision", f.Requests.Last().Command);
        Assert.DoesNotContain(typeof(XrTools).GetMethod(nameof(XrTools.SketchImage))!.GetParameters(), p => p.Name == "owner");
    }

    [Fact]
    public async Task DeduplicatedPngNeverStoresAnotherOwnersDocumentOrFrameMetadata()
    {
        await using var f = new Fixture(); JObject first = await f.Get("alice");
        f.Frame["corners_mm"] = new JArray(new JArray(100, 10), new JArray(120, 10), new JArray(120, 0), new JArray(100, 0));
        JObject second = await f.Get("bob");
        string id = (string)first["asset"]!["asset_id"]!;
        Assert.Equal(id, (string?)second["asset"]!["asset_id"]);
        Assert.NotNull(f.Assets.Describe(id, "alice")); Assert.NotNull(f.Assets.Describe(id, "bob"));
        Assert.False(JToken.DeepEquals(first["image_frame"], second["image_frame"]));
        Assert.Null(f.Assets.Describe(id, "alice")!.Metadata["image_frame"]);
        Assert.Null(f.Assets.Describe(id, "alice")!.Metadata["document_id"]);
    }

    [Fact]
    public async Task StaleRevisionAfterEncodingPublishesNoAsset()
    {
        await using var f = new Fixture(); f.BeforeRevision = () => f.Revision = "r8";
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Get());
        Assert.Equal(InventorErrorCodes.STALE_REVISION, error.Code);
        Assert.Empty(Directory.EnumerateFiles(f.Assets.StorageDirectory));
        Assert.Equal("get_visual_revision", f.Requests.Last().Command);
    }

    [Fact]
    public async Task PinChangeBeforeEncodingCannotPublishOnAnotherInstance()
    {
        await using var f = new Fixture { SwitchOnDiscard = true };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Get());
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, error.Code);
        Assert.Empty(Directory.EnumerateFiles(f.Assets.StorageDirectory));
        Assert.DoesNotContain(f.Requests, r => r.Command == "get_visual_revision");
    }

    [Fact]
    public async Task InvalidMetadataAndPngBudgetRefusePublication()
    {
        await using var malformed = new Fixture();
        malformed.Transform = (p, r) => { if ((string?)p["action"] == "begin") r["width_mm"] = 0; return r; };
        var protocol = await Assert.ThrowsAsync<InventorGatewayException>(() => malformed.Get()); Assert.Equal(InventorErrorCodes.API_ERROR, protocol.Code);
        Assert.Equal("discard", (string?)malformed.Requests.Last().Params["action"]);
        Assert.Empty(Directory.EnumerateFiles(malformed.Assets.StorageDirectory));
        await using var tiny = new Fixture(16);
        var budget = await Assert.ThrowsAsync<InventorGatewayException>(() => tiny.Get()); Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, budget.Code);
        Assert.DoesNotContain(tiny.Requests, r => r.Command == "get_visual_revision");
        Assert.Empty(Directory.EnumerateFiles(tiny.Assets.StorageDirectory));
    }

    [Fact]
    public async Task CancelledDownloadDiscardsAndReleasesServiceForNextCall()
    {
        await using var f = new Fixture(); using var cancel = new CancellationTokenSource();
        f.Transform = (p, r) => { if ((string?)p["action"] == "chunk") cancel.Cancel(); return r; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get(ct: cancel.Token));
        Assert.Equal("discard", (string?)f.Requests.Last().Params["action"]);
        Assert.Empty(Directory.EnumerateFiles(f.Assets.StorageDirectory));
        f.Transform = null; Assert.NotNull((await f.Get())["asset"]);
    }

    [Fact]
    public async Task OptInAndMissingIdentityRefuseBeforeNativeCall()
    {
        await using var f = new Fixture(); f.Config.EnableExperimental = false;
        var disabled = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Get()); Assert.Equal(InventorErrorCodes.EXPERIMENTAL_DISABLED, disabled.Code);
        f.Config.EnableExperimental = true;
        var invalid = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Get(" ")); Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, invalid.Code);
        Assert.Empty(f.Requests);
    }

    [Fact]
    public async Task OptionalToolConstructorDoesNotCreateAnUnboundedFallbackService()
    {
        await using var f = new Fixture();
        var tool = new XrTools(f.Client, f.Assets, f.Config, new Caller("alice"), new MeshCache());
        var response = JObject.Parse(await tool.SketchImage("part", "r7", "ent_sketch", "ent_image"));
        Assert.Equal(InventorErrorCodes.API_ERROR, (string?)response["error"]!["code"]); Assert.Empty(f.Requests);
    }

    [Fact]
    public async Task WaitingCallerCanCancelWithoutStartingASecondNativeDownload()
    {
        await using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.Transform = (p, r) =>
        {
            if ((string?)p["action"] == "begin")
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release its first download.");
            }
            return r;
        };
        Task<JObject> first = f.Get();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancel = new CancellationTokenSource();
            Task<JObject> waiting = f.Get("bob", cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Single(f.Requests);
        }
        finally { release.Set(); }
        Assert.NotNull((await first)["asset"]);
        Assert.Single(f.Requests.Where(r => (string?)r.Params["action"] == "begin"));
    }
}
