using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Server.Imports;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor.So.Mcp.Http.Imports;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

// FakeAddIn accepts a future variant only to exercise server lifetime; no native insertion proof.
public sealed class ImportIngressLeaseTests
{
    private sealed class Caller : ICallerIdentity
    { public string Client => "alice"; public string? SessionId => "session"; }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly string Directory = Path.Combine(Path.GetTempPath(), "import-ingress-" + Guid.NewGuid().ToString("N"));
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public readonly ImportResourceStore Imports;
        public readonly ChangePlanStore Plans = new();
        public readonly FakeAddIn Fake;
        public readonly SketchImportStagingStore Staging = new(new SketchImportStagingPolicy(
            1024, 2048, 8, 1024, TimeSpan.FromMinutes(1), 256));
        public readonly InventorMcpConfig Config;
        public Func<InventorCommandEnvelope, InventorCommandResult?> Override
        {
            set => Fake.Override = request =>
            {
                if (request.Command == "stage_sketch_import")
                    return new StageSketchImportHandler().Execute(new InventorCommandContext
                        { AllowExperimental = true, SketchImports = Staging }, request.Params);
                if (request.Command == "atomic_batch" && request.Params["import_attachments"] != null)
                {
                    using var bound = SketchBatchImportAttachments.Resolve(Staging, request.Params["import_attachments"],
                        (JArray)request.Params["operations"]!, true)!;
                    Assert.NotNull(bound);
                    var resource = Assert.Single(bound.Resources);
                    Assert.Equal(58, bound.Read(resource.Key).Length);
                    return value(request);
                }
                return value(request);
            };
        }
        public Fixture()
        {
            Imports = new(new ImportResourceStorePolicy(1024, 2048, 8, 1024, 32, TimeSpan.FromSeconds(1), new[] { "bmp" }), () => Now);
            Fake = new(Path.Combine(Directory, "targets"));
            Config = new() { DescriptorDirectory = Fake.DescriptorDirectory, AssetDirectory = Path.Combine(Directory, "assets"),
                TimeoutMs = 5000, EnableExperimental = true };
        }
        public PlanningTools Planning() => new(new PluginClient(Config), Plans, Config, new Caller(), new AssetStore(Config), Imports);
        public ParameterTools Batch() => new(new PluginClient(Config), Imports, new Caller());
        public async Task<string> Put(string owner = "alice")
        {
            byte[] bytes = new byte[58];
            bytes[0] = 66; bytes[1] = 77; bytes[2] = 58; bytes[10] = 54; bytes[14] = 40;
            bytes[18] = 1; bytes[22] = 1; bytes[26] = 1; bytes[28] = 24; bytes[34] = 4;
            var metadata = await new BmpImportContentInspector(2, 2, 4).InspectAsync("bmp", bytes, CancellationToken.None);
            return Imports.Put(owner, bytes, "bmp", metadata).Id;
        }
        public void Expire() { Now = Now.AddSeconds(2); }
        public async ValueTask DisposeAsync()
        {
            Plans.Dispose(); Staging.Dispose(); await Fake.DisposeAsync();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
    private static AtomicOperation[] Operations(string id) => new[] { new AtomicOperation
    {
        Command = "edit_sketch", Arguments = System.Text.Json.JsonSerializer.SerializeToElement(new
        { sketch_id = "sketch", variant = "insert_image", options = new
            { asset_id = id, position_mm = new[] { 0, 0 }, width_mm = 20, quarter_turns = 0, link = false } })
    } };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ForeignImageIsRejectedBeforeEitherIngressSends(bool planning)
    {
        await using var f = new Fixture(); string id = await f.Put("bob");
        var json = JObject.Parse(planning
            ? await f.Planning().PlanChange("doc", "rev", Operations(id))
            : await f.Batch().AtomicBatch("doc", "rev", Operations(id)));
        Assert.Equal(InventorErrorCodes.ASSET_NOT_FOUND, (string?)json["error"]?["code"]);
        Assert.Empty(f.Fake.Commands); Assert.Empty(f.Plans.List("alice"));
    }

    [Fact]
    public async Task PreviewPinsImageThenPlanRetainsItThroughCommitAfterCatalogExpiry()
    {
        await using var f = new Fixture(); string id = await f.Put(); int sends = 0;
        f.Override = request =>
        {
            if (request.Command != "atomic_batch") return null;
            sends++; f.Expire(); Assert.Null(f.Imports.Describe("alice", id));
            Assert.Equal(58, f.Imports.StoredBytes);
            Assert.Equal(id, (string?)request.Params["operations"]?[0]?["arguments"]?["options"]?["asset_id"]);
            Assert.Null(request.Params["import_bytes"]);
            return InventorCommandResult.Success(request.Id, new JObject { ["status"] = "fake-only" }, new());
        };
        var tools = f.Planning(); var plan = JObject.Parse(await tools.PlanChange("doc", "rev", Operations(id)));
        string planId = (string)plan["plan_id"]!;
        Assert.Equal(58, f.Imports.StoredBytes); Assert.Single(f.Plans.List("alice"));
        Assert.Null(f.Plans.List("alice")[0].Attachment<SketchImportAttachments>());
        var result = JObject.Parse(await tools.CommitPlan(planId, "doc", "rev"));
        Assert.Equal("fake-only", (string?)result["status"]); Assert.Equal(2, sends);
        Assert.Equal(0, f.Imports.StoredBytes); Assert.Equal(0, f.Staging.ReservedBytes);
        Assert.Empty(f.Plans.List("alice"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BothIngressesReleaseAcquiredBytesOnGatewayRefusal(bool planning)
    {
        await using var f = new Fixture(); string id = await f.Put();
        f.Override = request =>
        {
            if (request.Command != "atomic_batch") return null;
            f.Expire(); Assert.Equal(58, f.Imports.StoredBytes);
            return InventorCommandResult.Fail(request.Id, InventorErrorCodes.INVALID_ARGUMENT, "Unsupported future image variant.", new());
        };
        var json = JObject.Parse(planning
            ? await f.Planning().PlanChange("doc", "rev", Operations(id))
            : await f.Batch().AtomicBatch("doc", "rev", Operations(id)));
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, (string?)json["error"]?["code"]);
        Assert.Equal(0, f.Imports.StoredBytes); Assert.Equal(0, f.Staging.ReservedBytes);
        Assert.Empty(f.Plans.List("alice"));
    }

    [Fact]
    public async Task DiscardOfDeliveredImagePlanReleasesBytes()
    {
        await using var f = new Fixture(); string id = await f.Put();
        f.Override = request =>
        {
            if (request.Command != "atomic_batch") return null;
            f.Expire(); return InventorCommandResult.Success(request.Id, new JObject(), new());
        };
        var plan = JObject.Parse(await f.Planning().PlanChange("doc", "rev", Operations(id)));
        Assert.Equal(58, f.Imports.StoredBytes);
        f.Plans.Discard((string)plan["plan_id"]!); Assert.Equal(0, f.Imports.StoredBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationDuringTransportReleasesImageAtBothIngresses(bool planning)
    {
        await using var f = new Fixture(); string id = await f.Put();
        using var cancellation = new CancellationTokenSource(); bool reached = false;
        f.Override = request =>
        {
            if (request.Command != "atomic_batch") return null;
            reached = true; f.Expire(); Assert.Equal(58, f.Imports.StoredBytes);
            cancellation.Cancel();
            return InventorCommandResult.Success(request.Id, new JObject(), new());
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planning
            ? f.Planning().PlanChange("doc", "rev", Operations(id), ct: cancellation.Token)
            : f.Batch().AtomicBatch("doc", "rev", Operations(id), ct: cancellation.Token));
        Assert.True(reached); Assert.Equal(0, f.Imports.StoredBytes); Assert.Empty(f.Plans.List("alice"));
    }

    [Fact]
    public async Task OnlyTakenPlanCanReadFrozenAttachmentAndDisposalRevokesIt()
    {
        await using var f = new Fixture(); string id = await f.Put();
        var attachments = SketchImportAttachments.Resolve(f.Imports, "alice", JArray.Parse(
            System.Text.Json.JsonSerializer.Serialize(Operations(id))))!;
        var created = f.Plans.Create("alice", "doc", "rev", new JArray(), null, null, attachments);
        Assert.Null(created.Attachment<SketchImportAttachments>());
        f.Expire(); Assert.Null(f.Imports.Describe("alice", id));
        using var taken = f.Plans.Take(created.Id, "alice", "doc", "rev");
        var owned = Assert.IsType<SketchImportAttachments>(taken.Attachment<SketchImportAttachments>());
        Assert.Equal(58, owned.Read(id).Length); taken.Dispose();
        Assert.Null(taken.Attachment<SketchImportAttachments>());
        Assert.Throws<ObjectDisposedException>(() => owned.Read(id)); Assert.Equal(0, f.Imports.StoredBytes);
    }
}
