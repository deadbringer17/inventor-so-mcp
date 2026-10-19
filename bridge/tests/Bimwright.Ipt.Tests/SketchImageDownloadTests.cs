using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class SketchImageDownloadTests
{
    private static byte[] Bmp(int width = 2, int height = 2, int bits = 24, bool topDown = false)
    {
        int stride = ((width * bits + 31) / 32) * 4;
        var bytes = new byte[54 + stride * height];
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M'; I32(2, bytes.Length); I32(10, 54); I32(14, 40);
        I32(18, width); I32(22, topDown ? -height : height); bytes[26] = 1; bytes[28] = (byte)bits; I32(34, stride * height);
        for (int i = 54; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static JObject NativeFrame() => new()
    {
        ["schema"] = 1, ["provenance"] = "native_boundary_roles",
        ["corner_order"] = new JArray("top_left", "top_right", "bottom_right", "bottom_left"),
        ["corners_mm"] = new JArray(new JArray(3, 4), new JArray(15, 20), new JArray(23, 14), new JArray(11, -2))
    };
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "s1-image-download-" + Guid.NewGuid().ToString("N"));
        internal readonly FakeAddIn Fake;
        internal readonly PluginClient Client;
        internal readonly List<JObject> Requests = new();
        internal byte[] Source;
        internal Func<JObject, JObject, JObject>? Transform;
        internal Action<JObject>? Before;
        internal bool FailDiscard;
        internal int ChunkLimit = SketchImageDownload.MaxChunkBytes;
        internal JObject RevisionReply = new() { ["document_id"] = "document", ["revision"] = "revision" };
        internal int RevisionRequests;
        internal Fixture(byte[]? source = null)
        {
            Source = source ?? Bmp();
            Fake = new FakeAddIn(Path.Combine(_directory, "targets"));
            Client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = Fake.DescriptorDirectory, TimeoutMs = 5000 }, null, null, TimeSpan.Zero);
            Fake.Override = envelope =>
            {
                if (envelope.Command == "get_visual_revision")
                {
                    RevisionRequests++;
                    return InventorCommandResult.Success(envelope.Id, RevisionReply.DeepClone(), new());
                }
                if (envelope.Command != "get_sketch_image_payload") return null;
                var p = (JObject)envelope.Params.DeepClone(); Requests.Add(p); Before?.Invoke(p);
                string action = (string)p["action"]!;
                if (action == "discard" && FailDiscard) return InventorCommandResult.Fail(envelope.Id, InventorErrorCodes.API_ERROR, "Scripted cleanup failure.", new());
                var reply = new JObject { ["transfer_id"] = "download_fixture", ["sha256"] = Hash(Source), ["byte_length"] = Source.Length };
                switch (action)
                {
                    case "begin":
                        if (p["source_mode"] != null)
                        {
                            reply["source_mode"] = p["source_mode"]!.DeepClone();
                            reply["image_frame"] = NativeFrame(); reply["width_mm"] = 20; reply["height_mm"] = 10;
                            reply["visible"] = true; reply["transparent"] = false;
                        }
                        reply["format"] = "bmp"; reply["pixel_width"] = BinaryPrimitives.ReadInt32LittleEndian(Source.AsSpan(18, 4));
                        reply["pixel_height"] = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(Source.AsSpan(22, 4)));
                        reply["max_chunk_bytes"] = ChunkLimit; break;
                    case "chunk":
                        int offset = (int)p["offset"]!, count = (int)p["count"]!;
                        reply["offset"] = offset; reply["data_base64"] = Convert.ToBase64String(Source, offset, count); break;
                    case "discard": reply = new JObject { ["discarded"] = true }; break;
                    default: throw new InvalidOperationException();
                }
                return InventorCommandResult.Success(envelope.Id, Transform?.Invoke(p, reply) ?? reply, new());
            };
        }
        internal Task<SketchImageDownload.Record> Download(CancellationToken ct = default)
            => SketchImageDownload.DownloadAsync(Client, "alice", "document", "revision", "sketch", "image", ct);
        internal Task<SketchImageDownload.Record> DownloadNative(CancellationToken ct = default)
            => SketchImageDownload.DownloadNativeAsync(Client, "alice", "document", "revision", "sketch", "image", ct);
        internal void SwitchAway()
        {
            string id = "inventor-2027-" + Environment.ProcessId + "-other";
            File.WriteAllText(Path.Combine(Fake.DescriptorDirectory, "other.json"), JsonConvert.SerializeObject(new TargetDescriptor
            {
                TargetId = id, InventorYear = 2027, ProcessId = Environment.ProcessId, HostApp = "Inventor", Transport = "pipe",
                PipeName = "absent-" + Guid.NewGuid().ToString("N"), AuthToken = "synthetic-test", LastHeartbeatUtc = DateTimeOffset.UtcNow,
            }));
            Assert.True(Client.SwitchTarget(id));
        }
        public async ValueTask DisposeAsync()
        { await Fake.DisposeAsync(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    [Fact]
    public async Task RealPipeReadsMultipleBoundedChunksAndDisposesOwnedBytes()
    {
        await using var f = new Fixture(Bmp(512, 256));
        var result = await f.Download();
        Assert.Equal(f.Source, result.Bytes); Assert.Equal(Hash(f.Source), result.Sha256);
        Assert.Equal("a_" + result.Sha256, result.AssetId); Assert.Equal(512, result.PixelWidth); Assert.Equal(256, result.PixelHeight);
        Assert.Equal(f.Source.Length, result.ByteLength);
        var chunks = f.Requests.Where(p => (string?)p["action"] == "chunk").ToArray(); Assert.Equal(3, chunks.Length);
        int received = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(received, (int)chunk["offset"]!); int count = (int)chunk["count"]!;
            Assert.InRange(count, 1, SketchImageDownload.MaxChunkBytes); received += count;
            Assert.Null(chunk["data_base64"]);
        }
        Assert.Equal(f.Source.Length, received);
        var begin = f.Requests[0]; Assert.Equal("document", (string?)begin["document_id"]);
        Assert.Equal("revision", (string?)begin["expected_revision"]); Assert.Equal("sketch", (string?)begin["sketch_id"]);
        Assert.Equal("image", (string?)begin["image_id"]);
        Assert.All(f.Requests, p =>
        {
            Assert.Equal("alice", (string?)p["owner"]); Assert.Equal((string?)begin["session"], (string?)p["session"]);
            Assert.Equal("document", (string?)p["document_id"]); Assert.Equal("revision", (string?)p["expected_revision"]);
            Assert.Equal("sketch", (string?)p["sketch_id"]); Assert.Equal("image", (string?)p["image_id"]);
        });
        Assert.True(Guid.TryParseExact((string?)begin["session"], "N", out _));
        Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
        byte[] owned = result.Bytes; result.Dispose(); result.Dispose();
        Assert.All(owned, value => Assert.Equal(0, value)); Assert.Throws<ObjectDisposedException>(() => result.Bytes);
        Assert.Contains((byte)71, f.Source);
    }

    [Fact]
    public async Task SmallerNativeChunkLimitIsRespectedAndTopDownBmpIsValid()
    {
        byte[] bytes = Bmp(); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), -2);
        await using var f = new Fixture(bytes) { ChunkLimit = 7 };
        using var result = await f.Download(); Assert.Equal(bytes, result.Bytes);
        Assert.All(f.Requests.Where(p => (string?)p["action"] == "chunk"), p => Assert.InRange((int)p["count"]!, 1, 7));
    }

    [Theory]
    [InlineData("format", "\"png\"")]
    [InlineData("byte_length", "33554433")]
    [InlineData("byte_length", "53")]
    [InlineData("byte_length", "999999999999999999999999999999")]
    [InlineData("byte_length", "\"70\"")]
    [InlineData("pixel_width", "2147483647")]
    [InlineData("pixel_height", "0")]
    [InlineData("sha256", "\"invalid\"")]
    [InlineData("max_chunk_bytes", "196609")]
    [InlineData("max_chunk_bytes", "0")]
    public async Task InvalidBeginMetadataIsRejectedBeforeDownloadAndKnownTransferIsDiscarded(string field, string value)
    {
        await using var f = new Fixture();
        f.Transform = (p, r) => { if ((string?)p["action"] == "begin") r[field] = JToken.Parse(value); return r; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code);
        Assert.Equal(new[] { "begin", "discard" }, f.Requests.Select(p => (string)p["action"]!).ToArray());
    }

    [Theory]
    [InlineData("offset", "1")]
    [InlineData("offset", "\"0\"")]
    [InlineData("byte_length", "69")]
    [InlineData("transfer_id", "\"foreign\"")]
    [InlineData("sha256", "\"0000000000000000000000000000000000000000000000000000000000000000\"")]
    [InlineData("data_base64", "\"invalid!\"")]
    [InlineData("data_base64", "null")]
    public async Task InvalidChunkEnvelopeIsRejectedAndDiscarded(string field, string value)
    {
        await using var f = new Fixture();
        f.Transform = (p, r) => { if ((string?)p["action"] == "chunk") r[field] = JToken.Parse(value); return r; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code); Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("short")]
    [InlineData("corrupted")]
    [InlineData("whitespace")]
    public async Task InvalidChunkBytesAreRejectedWithoutAcceptingUnverifiedPayload(string mode)
    {
        await using var f = new Fixture();
        f.Transform = (p, r) =>
        {
            if ((string?)p["action"] != "chunk") return r;
            string encoded = (string)r["data_base64"]!;
            if (mode == "oversized") r["data_base64"] = new string('A', SketchImageDownload.MaxEncodedCharacters + 4);
            else if (mode == "short") r["data_base64"] = encoded.Substring(0, encoded.Length - 4);
            else if (mode == "whitespace") r["data_base64"] = " " + encoded.Substring(1);
            else { byte[] bytes = Convert.FromBase64String(encoded); bytes[bytes.Length - 1] ^= 1; r["data_base64"] = Convert.ToBase64String(bytes); }
            return r;
        };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code); Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
    }

    [Fact]
    public async Task HashValidButMalformedBmpLayoutIsRejected()
    {
        byte[] bytes = Bmp(); bytes[28] = 8;
        await using var f = new Fixture(bytes);
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code); Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
    }

    [Fact]
    public async Task CancellationDuringChunkStillDiscardsWithIndependentToken()
    {
        await using var f = new Fixture(); using var cancel = new CancellationTokenSource();
        f.Transform = (p, r) => { if ((string?)p["action"] == "chunk") cancel.Cancel(); return r; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Download(cancel.Token));
        Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
    }

    [Fact]
    public async Task TargetChangeRefusesFurtherChunksAndNeverRetriesOnAnotherTarget()
    {
        await using var f = new Fixture(Bmp(512, 256));
        f.Transform = (p, r) => { if ((string?)p["action"] == "chunk") f.SwitchAway(); return r; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, error.Code);
        Assert.Equal(new[] { "begin", "chunk" }, f.Requests.Select(p => (string)p["action"]!).ToArray());
    }

    [Fact]
    public async Task CleanupFailureDoesNotReplaceSuccessOrPrimaryProtocolFailure()
    {
        await using var f = new Fixture { FailDiscard = true };
        using var result = await f.Download(); Assert.Equal(f.Source, result.Bytes);
        f.Transform = (p, r) => { if ((string?)p["action"] == "chunk") r["offset"] = -1; return r; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code);
    }

    [Fact]
    public async Task NativeBitmapModeIsBoundToEveryRequestAndAcceptsTopDownBgrx()
    {
        await using var f = new Fixture(Bmp(512, 256, bits: 32, topDown: true));
        using var result = await f.DownloadNative();
        Assert.Equal("native_bitmap", result.SourceKind); Assert.Equal(f.Source, result.Bytes);
        Assert.Equal(512, result.PixelWidth); Assert.Equal(256, result.PixelHeight);
        Assert.Equal("a_" + Hash(f.Source), result.AssetId);
        Assert.Equal(3, f.Requests.Count(p => (string?)p["action"] == "chunk"));
        Assert.All(f.Requests, p =>
        {
            Assert.Equal("native_bitmap", (string?)p["source_mode"]);
            Assert.Equal("alice", (string?)p["owner"]);
            Assert.Equal((string?)f.Requests[0]["session"], (string?)p["session"]);
            Assert.Equal("document", (string?)p["document_id"]); Assert.Equal("revision", (string?)p["expected_revision"]);
            Assert.Equal("sketch", (string?)p["sketch_id"]); Assert.Equal("image", (string?)p["image_id"]);
        });
        Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("managed_original")]
    [InlineData("foreign")]
    [InlineData("nonstring")]
    public async Task NativeBitmapRequiresMatchingBeginOriginAndCleanupKeepsRequestedMode(string responseMode)
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true));
        f.Transform = (p, r) =>
        {
            if ((string?)p["action"] == "begin")
            {
                if (responseMode == "missing") r.Remove("source_mode");
                else r["source_mode"] = responseMode == "nonstring" ? new JValue(1) : new JValue(responseMode);
            }
            return r;
        };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.DownloadNative());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code);
        Assert.Equal(new[] { "begin", "discard" }, f.Requests.Select(p => (string)p["action"]!).ToArray());
        Assert.All(f.Requests, p => Assert.Equal("native_bitmap", (string?)p["source_mode"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedDefaultAcceptsAbsentOrMatchingSourceModeWithoutChangingLegacyRequests(bool explicitMode)
    {
        await using var f = new Fixture();
        f.Transform = (p, r) => { if (explicitMode && (string?)p["action"] == "begin") r["source_mode"] = "managed_original"; return r; };
        using var result = await f.Download();
        Assert.Equal("managed_original", result.SourceKind); Assert.Equal(f.Source, result.Bytes);
        Assert.All(f.Requests, p => Assert.Null(p["source_mode"]));
    }

    [Fact]
    public async Task ManagedDefaultRejectsResponseClaimingNativeBitmap()
    {
        await using var f = new Fixture();
        f.Transform = (p, r) => { if ((string?)p["action"] == "begin") r["source_mode"] = "native_bitmap"; return r; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.Download());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code);
        Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
        Assert.All(f.Requests, p => Assert.Null(p["source_mode"]));
    }

    [Fact]
    public async Task NativeBitmapCancellationKeepsIndependentCleanupAndModeBinding()
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true)); using var cancel = new CancellationTokenSource();
        f.Transform = (p, r) => { if ((string?)p["action"] == "chunk") cancel.Cancel(); return r; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.DownloadNative(cancel.Token));
        Assert.Equal("discard", (string?)f.Requests.Last()["action"]);
        Assert.All(f.Requests, p => Assert.Equal("native_bitmap", (string?)p["source_mode"]));
    }

    [Fact]
    public async Task NativeFrameUsesSemanticRotatedGeometryAndReturnsDetachedMetadata()
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true));
        using var result = await f.DownloadNative();
        Assert.Equal(20, result.WidthMm); Assert.Equal(10, result.HeightMm);
        Assert.True(result.Visible); Assert.False(result.Transparent);
        Assert.Equal("native_boundary_roles", (string?)result.Frame!["provenance"]);
        JObject frame = result.Frame!; frame["corners_mm"]![0]![0] = 999;
        Assert.Equal(3, (double)result.Frame!["corners_mm"]![0]![0]!);
        Assert.Equal(f.Client.CurrentTarget!.TargetId, result.Target.TargetId);
        await result.RecheckRevisionAsync(f.Client, "document", "revision");
        Assert.Equal(1, f.RevisionRequests);
    }

    [Fact]
    public async Task ManagedDefaultDoesNotInventFrameDimensionsOrVisibility()
    {
        await using var f = new Fixture(); using var result = await f.Download();
        Assert.Null(result.Frame); Assert.Equal(0, result.WidthMm); Assert.Equal(0, result.HeightMm);
        Assert.False(result.Visible); Assert.False(result.Transparent);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("provenance")]
    [InlineData("order")]
    [InlineData("order_missing")]
    [InlineData("corners_count")]
    [InlineData("coordinate_dimension")]
    [InlineData("coordinate_type")]
    [InlineData("nonfinite")]
    [InlineData("overflow")]
    [InlineData("zero_side")]
    [InlineData("closure")]
    [InlineData("skew")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("visible")]
    [InlineData("transparent")]
    [InlineData("transparent_true")]
    public async Task MalformedNativeGeometryIsRefusedBeforeChunkAllocationAndDiscarded(string invalid)
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true));
        f.Transform = (p, r) =>
        {
            if ((string?)p["action"] != "begin") return r;
            var frame = (JObject)r["image_frame"]!; var corners = (JArray)frame["corners_mm"]!;
            switch (invalid)
            {
                case "missing": r.Remove("image_frame"); break;
                case "schema": frame["schema"] = "1"; break;
                case "provenance": frame["provenance"] = "native_corner_reference_keys"; break;
                case "order": frame["corner_order"] = new JArray("top_left", "top_right", "bottom_left", "bottom_right"); break;
                case "order_missing": frame.Remove("corner_order"); break;
                case "corners_count": corners.RemoveAt(3); break;
                case "coordinate_dimension": corners[0] = new JArray(3, 4, 0); break;
                case "coordinate_type": corners[0] = new JArray("3", 4); break;
                case "nonfinite": corners[0] = new JArray(double.NaN, 4); break;
                case "overflow": corners[0] = new JArray(double.MaxValue, double.MaxValue); break;
                case "zero_side": corners[1] = corners[0].DeepClone(); break;
                case "closure": corners[2] = new JArray(23.01, 14); break;
                case "skew":
                    r["height_mm"] = Math.Sqrt(101);
                    frame["corners_mm"] = new JArray(new JArray(0, 0), new JArray(20, 0), new JArray(21, -10), new JArray(1, -10)); break;
                case "width": r["width_mm"] = 0; break;
                case "height": r["height_mm"] = "10"; break;
                case "visible": r["visible"] = 1; break;
                case "transparent": r["transparent"] = "false"; break;
                case "transparent_true": r["transparent"] = true; break;
            }
            return r;
        };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => f.DownloadNative());
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code);
        Assert.Equal(new[] { "begin", "discard" }, f.Requests.Select(p => (string)p["action"]!).ToArray());
        Assert.All(f.Requests, p => Assert.Equal("native_bitmap", (string?)p["source_mode"]));
    }

    [Theory]
    [InlineData("document_id", "other", InventorErrorCodes.DOCUMENT_CHANGED)]
    [InlineData("revision", "other", InventorErrorCodes.STALE_REVISION)]
    public async Task RecheckRejectsChangedDocumentOrRevisionAfterEncoding(string field, string value, string code)
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true)); using var result = await f.DownloadNative();
        f.RevisionReply[field] = value;
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => result.RecheckRevisionAsync(f.Client, "document", "revision"));
        Assert.Equal(code, error.Code); Assert.Equal(1, f.RevisionRequests);
    }

    [Fact]
    public async Task RecheckNeverFollowsTargetSwitchAfterDownloadCompleted()
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true)); using var result = await f.DownloadNative();
        f.SwitchAway();
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => result.RecheckRevisionAsync(f.Client, "document", "revision"));
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, error.Code); Assert.Equal(0, f.RevisionRequests);
    }

    [Fact]
    public async Task RecheckRespectsCancellationAndDisposedResultBeforeSending()
    {
        await using var f = new Fixture(Bmp(bits: 32, topDown: true)); var result = await f.DownloadNative();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.RecheckRevisionAsync(f.Client, "document", "revision", cancel.Token));
        result.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => result.RecheckRevisionAsync(f.Client, "document", "revision"));
        Assert.Equal(0, f.RevisionRequests);
    }
}
