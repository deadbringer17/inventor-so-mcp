using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using Bimwright.Ipt.Server.Imports;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class SketchImportAttachmentsTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private static ImportResourceStore Store(Func<DateTimeOffset>? clock = null) => new(
        new ImportResourceStorePolicy(100, 100, 8, 2048, 16, TimeSpan.FromMinutes(1), new[] { "bmp", "png" }), clock);
    private static JObject Metadata() => new()
    {
        ["kind"] = "Image", ["dependencies"] = new JArray(),
        ["image"] = new JObject { ["pixel_width"] = 2, ["pixel_height"] = 1 },
    };
    private static JObject Step(string id) => new()
    {
        ["command"] = "edit_sketch", ["arguments"] = new JObject
        {
            ["sketch_id"] = "sketch-owned", ["variant"] = "insert_image", ["options"] = new JObject
            {
                ["asset_id"] = id, ["position_mm"] = new JArray(0, 10), ["width_mm"] = 20,
                ["quarter_turns"] = 0, ["link"] = false,
            },
        },
    };
    private static PlanException Rejected(string code, Action action)
    {
        var error = Assert.Throws<PlanException>(action);
        Assert.Equal(code, error.Code); return error;
    }

    [Fact]
    public void OrdinaryOperationsNeedNeitherStoreNorImportOwner()
    {
        Assert.Null(SketchImportAttachments.Resolve(null, "", new JArray()));
        var ordinary = Step("not-an-asset"); ordinary["arguments"]!["variant"] = "line";
        Assert.Null(SketchImportAttachments.Resolve(null, "", new JArray(ordinary)));
    }

    [Fact]
    public void DuplicateAssetUsesOneLeaseWithDetachedFrozenMetadataAndBytes()
    {
        var store = Store(); byte[] bytes = { 1, 2, 3 };
        var record = store.Put("alice", bytes, "bmp", Metadata());
        var operations = new JArray(Step(record.Id), Step(record.Id));
        string original = operations.ToString();
        using var resolved = SketchImportAttachments.Resolve(store, "alice", operations)!;
        Assert.Single(resolved.Resources); Assert.Equal(original, operations.ToString());
        Assert.Equal(record.Sha256, resolved.Resources[0].Sha256);
        store.Put("alice", bytes, "png", new JObject());
        resolved.Resources[0].Metadata["image"]!["pixel_width"] = 999;
        Assert.Equal("bmp", resolved.Resources[0].Format);
        Assert.Equal(2, (int)resolved.Resources[0].Metadata["image"]!["pixel_width"]!);
        byte[] read = resolved.Read(record.Id); read[0] = 99;
        Assert.Equal(bytes, resolved.Read(record.Id));
    }

    [Fact]
    public void AcquiredBytesSurviveCatalogExpiryUntilDispose()
    {
        DateTimeOffset now = Epoch; var store = Store(() => now);
        var record = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        var resolved = SketchImportAttachments.Resolve(store, "alice", new JArray(Step(record.Id)))!;
        now += TimeSpan.FromMinutes(2);
        Assert.Null(store.Describe("alice", record.Id)); Assert.Equal(1, store.StoredBytes);
        Assert.Equal(new byte[] { 1 }, resolved.Read(record.Id));
        resolved.Dispose(); resolved.Dispose(); Assert.Equal(0, store.StoredBytes);
        Assert.Throws<ObjectDisposedException>(() => resolved.Read(record.Id));
    }

    [Fact]
    public void ForeignMissingExpiredAndAbsentStoreHaveSamePublicError()
    {
        DateTimeOffset now = Epoch; var store = Store(() => now);
        var resource = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        var operations = new JArray(Step(resource.Id));
        string absent = Rejected(InventorErrorCodes.ASSET_NOT_FOUND,
            () => SketchImportAttachments.Resolve(null, "alice", operations)).Message;
        Assert.Equal(absent, Rejected(InventorErrorCodes.ASSET_NOT_FOUND,
            () => SketchImportAttachments.Resolve(store, "bob", operations)).Message);
        now += TimeSpan.FromMinutes(2);
        Assert.Equal(absent, Rejected(InventorErrorCodes.ASSET_NOT_FOUND,
            () => SketchImportAttachments.Resolve(store, "alice", operations)).Message);
        Assert.Equal(absent, Rejected(InventorErrorCodes.ASSET_NOT_FOUND,
            () => SketchImportAttachments.Resolve(store, "alice", new JArray(Step("a_" + new string('0', 64))))).Message);
    }

    [Fact]
    public void MissingSecondAssetNeverPinsFirstAsset()
    {
        DateTimeOffset now = Epoch; var store = Store(() => now);
        var resource = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        Rejected(InventorErrorCodes.ASSET_NOT_FOUND, () => SketchImportAttachments.Resolve(store, "alice",
            new JArray(Step(resource.Id), Step("a_" + new string('0', 64)))));
        now += TimeSpan.FromMinutes(2); Assert.Equal(0, store.StoredBytes);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("kind")]
    [InlineData("dependency")]
    [InlineData("missing_width")]
    [InlineData("fractional_width")]
    [InlineData("huge_width")]
    [InlineData("geometry")]
    [InlineData("private_metadata")]
    public void InvalidInspectedMetadataReleasesEveryAcquiredResource(string defect)
    {
        DateTimeOffset now = Epoch; var store = Store(() => now);
        var first = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        var bad = Metadata(); string format = "bmp";
        switch (defect)
        {
            case "format": format = "png"; break;
            case "kind": bad["kind"] = "Acad"; break;
            case "dependency": bad["dependencies"] = new JArray(first.Id); break;
            case "missing_width": ((JObject)bad["image"]!).Remove("pixel_width"); break;
            case "fractional_width": bad["image"]!["pixel_width"] = 1.5; break;
            case "huge_width": bad["image"]!["pixel_width"] = new JValue(BigInteger.Pow(10, 80)); break;
            case "geometry": bad["geometry"] = new JObject(); break;
            case "private_metadata": bad["path"] = @"C:\private.bmp"; break;
        }
        var second = store.Put("alice", new byte[] { 2 }, format, bad);
        Rejected(InventorErrorCodes.INVALID_ARGUMENT, () => SketchImportAttachments.Resolve(store, "alice",
            new JArray(Step(first.Id), Step(second.Id))));
        now += TimeSpan.FromMinutes(2); Assert.Equal(0, store.StoredBytes);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("bytes")]
    [InlineData("owner")]
    [InlineData("invalid_id")]
    [InlineData("position")]
    [InlineData("infinite_width")]
    [InlineData("negative_width")]
    [InlineData("rotation")]
    [InlineData("huge_rotation")]
    [InlineData("link")]
    public void UntrustedOperationOptionsFailBeforeLeasing(string defect)
    {
        DateTimeOffset now = Epoch; var store = Store(() => now);
        var resource = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        var step = Step(resource.Id); var options = (JObject)step["arguments"]!["options"]!;
        switch (defect)
        {
            case "path": options["path"] = @"C:\private.bmp"; break;
            case "bytes": options["bytes"] = "AQ=="; break;
            case "owner": step["arguments"]!["owner"] = "alice"; break;
            case "invalid_id": options["asset_id"] = "https://host/private.bmp"; break;
            case "position": options["position_mm"] = new JArray(1, 2, 3); break;
            case "infinite_width": options["width_mm"] = double.PositiveInfinity; break;
            case "negative_width": options["width_mm"] = -1; break;
            case "rotation": options["quarter_turns"] = 4; break;
            case "huge_rotation": options["quarter_turns"] = new JValue(BigInteger.Pow(10, 80)); break;
            case "link": options["link"] = "false"; break;
        }
        Rejected(InventorErrorCodes.INVALID_ARGUMENT, () => SketchImportAttachments.Resolve(store, "alice", new JArray(step)));
        now += TimeSpan.FromMinutes(2); Assert.Equal(0, store.StoredBytes);
    }

    [Fact]
    public void CancellationAfterAcquireCleansTheLease()
    {
        DateTimeOffset now = Epoch; using var cts = new CancellationTokenSource(); bool cancelInAcquire = false;
        var store = Store(() => { if (cancelInAcquire) cts.Cancel(); return now; });
        var resource = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        cancelInAcquire = true;
        Assert.Throws<OperationCanceledException>(() => SketchImportAttachments.Resolve(store, "alice", new JArray(Step(resource.Id)), cts.Token));
        cancelInAcquire = false; now += TimeSpan.FromMinutes(2); Assert.Equal(0, store.StoredBytes);
    }

    [Fact]
    public void OwnerMustComeFromAuthenticatedIngressAndReadIsLeaseScoped()
    {
        var store = Store(); var resource = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        Rejected(InventorErrorCodes.INVALID_ARGUMENT, () => SketchImportAttachments.Resolve(store, "", new JArray(Step(resource.Id))));
        using var resolved = SketchImportAttachments.Resolve(store, "alice", new JArray(Step(resource.Id)))!;
        Assert.Throws<ArgumentException>(() => resolved.Read("a_" + new string('0', 64)));
    }

    [Fact]
    public void LinkedOptionResolutionDoesNotClaimPersistentStorage()
    {
        var store = Store(); var resource = store.Put("alice", new byte[] { 1 }, "bmp", Metadata());
        var step = Step(resource.Id); step["arguments"]!["options"]!["link"] = true;
        using var resolved = SketchImportAttachments.Resolve(store, "alice", new JArray(step))!;
        Assert.Single(resolved.Resources);
    }
}
