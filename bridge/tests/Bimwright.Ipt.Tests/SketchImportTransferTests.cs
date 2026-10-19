using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Imports;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

// Real pipe + staging store, without Inventor. No native insertion or rollback evidence.
public sealed class SketchImportTransferTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "s1-transfer-" + Guid.NewGuid().ToString("N"));
        internal readonly FakeAddIn Fake;
        internal readonly PluginClient Client;
        internal readonly ImportResourceStore Imports = new(new ImportResourceStorePolicy(2_000_000, 4_000_000, 8, 1024, 16, TimeSpan.FromMinutes(1), new[] { "bmp" }));
        internal readonly SketchImportStagingStore Staging = new(new SketchImportStagingPolicy(2_000_000, 4_000_000, 8, SketchImportTransfer.ChunkBytes, TimeSpan.FromMinutes(1), 128));
        internal readonly List<JObject> Requests = new();
        private readonly Dictionary<string, int> _received = new();
        internal Func<JObject, JObject, JObject>? Transform;
        internal Action<JObject>? Before;
        internal string? FailAction;
        internal bool FailDiscard;
        internal Fixture()
        {
            Fake = new(Path.Combine(_directory, "targets"));
            Client = new(new InventorMcpConfig { DescriptorDirectory = Fake.DescriptorDirectory, TimeoutMs = 5000 }, null, null, TimeSpan.Zero);
            Fake.Override = envelope =>
            {
                if (envelope.Command != "stage_sketch_import" && envelope.Command != "atomic_batch") return null;
                var p = (JObject)envelope.Params.DeepClone();
                p["wire_command"] = envelope.Command; Requests.Add(p);
                Before?.Invoke(p);
                string action = (string?)p["action"] ?? "batch";
                if (action == FailAction || action == "discard" && FailDiscard)
                    return InventorCommandResult.Fail(envelope.Id, InventorErrorCodes.VALIDATION_FAILED, "Scripted staging failure.", new());
                string owner = (string?)p["owner"] ?? "", session = (string?)p["session"] ?? "";
                string id = (string?)p["transfer_id"] ?? "";
                JObject reply;
                switch (action)
                {
                    case "begin":
                        id = Staging.Begin(owner, session, (int)p["byte_length"]!); _received[id] = 0;
                        reply = new JObject { ["transfer_id"] = id }; break;
                    case "chunk":
                        byte[] bytes = Convert.FromBase64String((string)p["data_base64"]!);
                        Staging.Append(owner, session, id, (int)p["offset"]!, bytes); _received[id] += bytes.Length;
                        reply = new JObject { ["transfer_id"] = id, ["received_bytes"] = _received[id] }; break;
                    case "seal":
                        Staging.Seal(owner, session, id, (string)p["sha256"]!);
                        reply = new JObject { ["transfer_id"] = id, ["sealed"] = true }; break;
                    case "discard": reply = new JObject { ["discarded"] = Staging.Discard(owner, session, id) }; break;
                    default: reply = new JObject { ["applied"] = true }; break;
                }
                return InventorCommandResult.Success(envelope.Id, Transform?.Invoke(p, reply) ?? reply, new());
            };
        }
        internal SketchImportAttachments Attach(params byte[][] sources)
        {
            var operations = new JArray();
            foreach (var bytes in sources)
            {
                var record = Imports.Put("alice", bytes, "bmp", new JObject { ["kind"] = "Image", ["dependencies"] = new JArray(),
                    ["image"] = new JObject { ["pixel_width"] = 2, ["pixel_height"] = 1 } });
                operations.Add(new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject
                {
                    ["sketch_id"] = "sketch", ["variant"] = "insert_image", ["options"] = new JObject
                    { ["asset_id"] = record.Id, ["position_mm"] = new JArray(0, 0), ["width_mm"] = 20, ["quarter_turns"] = 0, ["link"] = false },
                } });
            }
            return SketchImportAttachments.Resolve(Imports, "alice", operations)!;
        }
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
        { Staging.Dispose(); await Fake.DisposeAsync(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    [Fact]
    public async Task MultiChunkTransferSealsExactBytesAndSendsOnlyDescriptorsInBatch()
    {
        await using var f = new Fixture();
        byte[] bytes = Enumerable.Range(0, SketchImportTransfer.ChunkBytes * 2 + 17).Select(n => (byte)(n % 251)).ToArray();
        using var attachments = f.Attach(bytes, bytes);
        var operations = new JArray(new JObject { ["command"] = "unchanged", ["arguments"] = new JObject { ["value"] = 1 } });
        var request = new JObject { ["operations"] = operations };
        string before = operations.ToString();
        await using (var batch = await SketchImportTransfer.StageAsync(f.Client, attachments, "alice"))
        {
            Assert.Single(f.Requests.Where(p => (string?)p["action"] == "begin"));
            var chunks = f.Requests.Where(p => (string?)p["action"] == "chunk").ToArray(); Assert.Equal(3, chunks.Length);
            Assert.All(chunks, p => Assert.InRange(Convert.FromBase64String((string)p["data_base64"]!).Length, 1, SketchImportTransfer.ChunkBytes));
            batch.ApplyTo(request);
            var manifest = (JObject)request["import_attachments"]!;
            var resource = Assert.IsType<JObject>(Assert.Single((JArray)manifest["resources"]!));
            using var staged = f.Staging.Acquire((string)manifest["owner"]!, (string)manifest["session"]!, (string)resource["transfer_id"]!);
            Assert.NotNull(staged); Assert.Equal(bytes, staged!.ReadBytes());
            Assert.Equal(attachments.Resources[0].Sha256, (string?)resource["sha256"]);
            Assert.True(JToken.DeepEquals(attachments.Resources[0].Metadata, resource["metadata"]));
            Assert.DoesNotContain("data_base64", request.ToString()); Assert.Equal(before, operations.ToString());
            var reply = await batch.SendBatchAsync(request); Assert.True((bool)reply["applied"]!);
        }
        Assert.Equal(0, f.Staging.ReservedBytes);
    }

    [Theory]
    [InlineData("chunk")]
    [InlineData("seal")]
    public async Task FailureCleansEveryAcquiredTransferWithoutMaskingPrimary(string action)
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 }, new byte[] { 2 });
        int begins = 0; f.Before = p => { if ((string?)p["action"] == "begin" && ++begins == 2) f.FailAction = action; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => SketchImportTransfer.StageAsync(f.Client, attachments, "alice"));
        Assert.Equal(InventorErrorCodes.VALIDATION_FAILED, error.Code);
        var begun = f.Requests.Where(p => (string?)p["action"] == "seal").Select(p => (string)p["transfer_id"]!).First();
        var discards = f.Requests.Where(p => (string?)p["action"] == "discard").ToArray();
        Assert.Equal(2, discards.Length); Assert.Equal(begun, (string?)discards[1]["transfer_id"]);
        Assert.Equal(0, f.Staging.ReservedBytes);
    }

    [Theory]
    [InlineData("received_bytes")]
    [InlineData("sealed")]
    [InlineData("transfer_id")]
    public async Task InvalidRepliesAreRejectedAndKnownTransferIsDiscarded(string property)
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 });
        f.Transform = (p, response) =>
        {
            if ((string?)p["action"] == "chunk" && property != "sealed") response[property] = property == "received_bytes" ? new JValue("1") : new JValue("wrong-id");
            if ((string?)p["action"] == "seal" && property == "sealed") response[property] = false;
            return response;
        };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => SketchImportTransfer.StageAsync(f.Client, attachments, "alice"));
        Assert.Equal(InventorErrorCodes.API_ERROR, error.Code); Assert.Equal(0, f.Staging.ReservedBytes);
    }

    [Fact]
    public async Task CallerCancellationStillUsesIndependentCleanupToken()
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 }); using var cts = new CancellationTokenSource();
        f.Transform = (p, response) => { if ((string?)p["action"] == "chunk") cts.Cancel(); return response; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SketchImportTransfer.StageAsync(f.Client, attachments, "alice", cts.Token));
        Assert.Contains(f.Requests, p => (string?)p["action"] == "discard"); Assert.Equal(0, f.Staging.ReservedBytes);
    }

    [Fact]
    public async Task CleanupFailureDoesNotReplaceStagingFailure()
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 }); f.FailAction = "chunk"; f.FailDiscard = true;
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => SketchImportTransfer.StageAsync(f.Client, attachments, "alice"));
        Assert.Equal(InventorErrorCodes.VALIDATION_FAILED, error.Code); Assert.Equal("Scripted staging failure.", error.Message);
        Assert.Contains(f.Requests, p => (string?)p["action"] == "discard");
    }

    [Fact]
    public async Task TargetSwitchBetweenBeginAndChunkRefusesWithoutFollowingNewTarget()
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 });
        f.Transform = (p, response) => { if ((string?)p["action"] == "begin") f.SwitchAway(); return response; };
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => SketchImportTransfer.StageAsync(f.Client, attachments, "alice"));
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, error.Code);
        Assert.Single(f.Requests); Assert.Equal("begin", (string?)f.Requests[0]["action"]);
    }

    [Fact]
    public async Task TargetSwitchAfterStagingRefusesBatchAndDisposedBatchCannotSend()
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 });
        var batch = await SketchImportTransfer.StageAsync(f.Client, attachments, "alice"); f.SwitchAway();
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => batch.SendBatchAsync(new JObject()));
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, error.Code); Assert.DoesNotContain(f.Requests, p => (string?)p["wire_command"] == "atomic_batch");
        await batch.DisposeAsync(); await batch.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => batch.SendBatchAsync(new JObject()));
    }

    [Fact]
    public async Task SamePlanAttachmentsCannotRestageOnAnotherTargetAfterPreview()
    {
        await using var f = new Fixture(); using var attachments = f.Attach(new byte[] { 1 });
        var preview = await SketchImportTransfer.StageAsync(f.Client, attachments, "alice");
        await preview.DisposeAsync(); int before = f.Requests.Count;
        f.SwitchAway();
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => SketchImportTransfer.StageAsync(f.Client, attachments, "alice"));
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, error.Code); Assert.Equal(before, f.Requests.Count);
    }
}
