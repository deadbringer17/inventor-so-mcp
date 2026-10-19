using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SketchBatchImportAttachmentsTests
{
    private static SketchImportStagingStore Store(Func<DateTimeOffset>? clock = null) => new(
        new SketchImportStagingPolicy(100, 200, 4, 100, TimeSpan.FromMinutes(1), 32), clock);
    private static (string Id, JObject Entry) Upload(SketchImportStagingStore store, byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string transfer = store.Begin("owner", "session", bytes.Length);
        store.Append("owner", "session", transfer, 0, bytes); store.Seal("owner", "session", transfer, hash);
        return ("a_" + hash, new JObject { ["asset_id"] = "a_" + hash, ["transfer_id"] = transfer,
            ["sha256"] = hash, ["byte_length"] = bytes.Length, ["format"] = "bmp", ["metadata"] = new JObject {
                ["kind"] = "Image", ["dependencies"] = new JArray(), ["image"] = new JObject { ["pixel_width"] = 2, ["pixel_height"] = 3 } } });
    }
    private static JObject Descriptor(params JObject[] entries) => new() { ["owner"] = "owner", ["session"] = "session", ["resources"] = new JArray(entries) };
    private static JArray Operations(params string[] ids) => new(ids.Select(id => new JObject {
        ["command"] = "edit_sketch", ["arguments"] = new JObject { ["sketch_id"] = "sketch", ["variant"] = "insert_image", ["options"] = new JObject {
            ["asset_id"] = id, ["position_mm"] = new JArray(0, 0), ["width_mm"] = 10, ["quarter_turns"] = 0, ["link"] = false } } }));
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<CodedFailureException>(action).Code);

    [Fact]
    public void NumericOptionsUseWireCultureEvenWhenDesktopUsesCommaDecimal()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            using var store = Store(); var asset = Upload(store, new byte[] { 1 });
            var ops = Operations(asset.Id); ops[0]!["arguments"]!["options"]!["width_mm"] = 10.5;
            ops[0]!["arguments"]!["options"]!["position_mm"] = new JArray(0.5, 1.25);
            using var resolved = SketchBatchImportAttachments.Resolve(store, Descriptor(asset.Entry), ops, true)!;
            Assert.Equal(new byte[] { 1 }, resolved.Read(asset.Id));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void ResolvesDistinctReferencesWithoutMutatingOperationsAndFreezesMetadataAndBytes()
    {
        using var store = Store(); var asset = Upload(store, new byte[] { 1, 2 });
        var descriptor = Descriptor(asset.Entry); var operations = Operations(asset.Id, asset.Id); string before = operations.ToString();
        using var resolved = SketchBatchImportAttachments.Resolve(store, descriptor, operations, true)!;
        Assert.Single(resolved.Resources); Assert.Equal(before, operations.ToString());
        asset.Entry["metadata"]!["image"]!["pixel_width"] = 99;
        resolved.Resources[asset.Id].Metadata["image"]!["pixel_width"] = 88;
        Assert.Equal(2, (int)resolved.Resources[asset.Id].Metadata["image"]!["pixel_width"]!);
        var bytes = resolved.Read(asset.Id); bytes[0] = 99; Assert.Equal(new byte[] { 1, 2 }, resolved.Read(asset.Id));
        Assert.Equal("bmp", resolved.Resources[asset.Id].Format); Assert.Equal(2, resolved.Resources[asset.Id].ByteLength);
    }

    [Fact]
    public void LeaseSurvivesDiscardAndStoreDisposalUntilBatchDisposal()
    {
        var store = Store(); var asset = Upload(store, new byte[] { 1, 2 });
        var resolved = SketchBatchImportAttachments.Resolve(store, Descriptor(asset.Entry), Operations(asset.Id), true)!;
        store.Discard("owner", "session", (string)asset.Entry["transfer_id"]!); store.Dispose();
        Assert.Equal(2, store.ReservedBytes); Assert.Equal(new byte[] { 1, 2 }, resolved.Read(asset.Id));
        resolved.Dispose(); resolved.Dispose(); Assert.Equal(0, store.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => resolved.Read(asset.Id));
    }

    [Fact]
    public void NonImageBatchRequiresNoDescriptor()
    {
        Assert.Null(SketchBatchImportAttachments.Resolve(null, null, new JArray(), false));
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(null, new JObject(), new JArray(), true));
    }

    [Fact]
    public void ImageBatchRequiresExperimentalGateDescriptorAndAvailableStore()
    {
        using var store = Store(); var asset = Upload(store, new byte[] { 1 }); var ops = Operations(asset.Id);
        Error(InventorErrorCodes.EXPERIMENTAL_DISABLED, () => SketchBatchImportAttachments.Resolve(store, Descriptor(asset.Entry), ops, false));
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store, null, ops, true));
        Error(InventorErrorCodes.ASSET_NOT_FOUND, () => SketchBatchImportAttachments.Resolve(null, Descriptor(asset.Entry), ops, true));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("session")]
    public void ForeignIdentityCannotAcquireTransfers(string field)
    {
        using var store = Store(); var asset = Upload(store, new byte[] { 1 }); var descriptor = Descriptor(asset.Entry); descriptor[field] = "foreign";
        Error(InventorErrorCodes.ASSET_NOT_FOUND, () => SketchBatchImportAttachments.Resolve(store, descriptor, Operations(asset.Id), true));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void DescriptorMatchesAllDistinctReferencesExactly(string failure)
    {
        using var store = Store(); var first = Upload(store, new byte[] { 1 }); var second = Upload(store, new byte[] { 2 });
        var descriptor = failure == "extra" ? Descriptor(first.Entry, second.Entry) : failure == "missing" ? Descriptor() : Descriptor(first.Entry, first.Entry);
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store, descriptor, Operations(first.Id), true));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("entry")]
    [InlineData("metadata")]
    [InlineData("image")]
    [InlineData("format")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("pixels")]
    [InlineData("dependencies")]
    [InlineData("geometry")]
    [InlineData("options")]
    public void InvalidDeclarationsAndPublicPayloadSmugglingAreRefused(string failure)
    {
        using var store = Store(); var asset = Upload(store, new byte[] { 1 }); var descriptor = Descriptor(asset.Entry); var ops = Operations(asset.Id);
        var entry = (JObject)descriptor["resources"]![0]!; var metadata = (JObject)entry["metadata"]!;
        switch (failure)
        {
            case "root": descriptor["path"] = "private"; break;
            case "entry": entry["bytes"] = "private"; break;
            case "metadata": metadata["path"] = "private"; break;
            case "image": metadata["image"]!["path"] = "private"; break;
            case "format": entry["format"] = "png"; break;
            case "size": entry["byte_length"] = 2; break;
            case "hash": entry["sha256"] = new string('0', 64); break;
            case "pixels": metadata["image"]!["pixel_width"] = 0; break;
            case "dependencies": metadata["dependencies"] = new JArray("external"); break;
            case "geometry": metadata["geometry"] = new JObject(); break;
            case "options": ops[0]!["arguments"]!["options"]!["path"] = "private"; break;
        }
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store, descriptor, ops, true));
    }

    [Fact]
    public void PartialAcquisitionFailureReleasesEveryPreviousLease()
    {
        using var store = Store(); var first = Upload(store, new byte[] { 1 }); var second = Upload(store, new byte[] { 2 });
        store.Discard("owner", "session", (string)second.Entry["transfer_id"]!);
        Error(InventorErrorCodes.ASSET_NOT_FOUND, () => SketchBatchImportAttachments.Resolve(store, Descriptor(first.Entry, second.Entry), Operations(first.Id, second.Id), true));
        store.Discard("owner", "session", (string)first.Entry["transfer_id"]!); Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void EqualCountCannotHideDuplicateOrUnreferencedResources()
    {
        using var store = Store(); var first = Upload(store, new byte[] { 1 }); var second = Upload(store, new byte[] { 2 });
        var third = Upload(store, new byte[] { 3 });
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store,
            Descriptor(first.Entry, first.Entry), Operations(first.Id, second.Id), true));
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store,
            Descriptor(first.Entry, third.Entry), Operations(first.Id, second.Id), true));
    }

    [Fact]
    public void HashIdentityConsistentButDifferentFromStagedBytesIsRefusedAndUnpinned()
    {
        using var store = Store(); var asset = Upload(store, new byte[] { 1 });
        string fakeHash = new string('0', 64), fakeId = "a_" + fakeHash;
        asset.Entry["sha256"] = fakeHash; asset.Entry["asset_id"] = fakeId;
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store,
            Descriptor(asset.Entry), Operations(fakeId), true));
        store.Discard("owner", "session", (string)asset.Entry["transfer_id"]!); Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void TimeoutAfterAcquiringLeaseReleasesIt()
    {
        using var store = Store(); var asset = Upload(store, new byte[] { 1 }); int calls = 0;
        Error(InventorErrorCodes.TIMEOUT, () => SketchBatchImportAttachments.Resolve(store, Descriptor(asset.Entry), Operations(asset.Id), true, () => ++calls == 5));
        store.Discard("owner", "session", (string)asset.Entry["transfer_id"]!); Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void UnsealedAndExpiredTransfersAreRefused()
    {
        var now = DateTimeOffset.UtcNow; using var store = Store(() => now); var asset = Upload(store, new byte[] { 1 });
        string unsealed = store.Begin("owner", "session", 1); asset.Entry["transfer_id"] = unsealed;
        Error(InventorErrorCodes.INVALID_ARGUMENT, () => SketchBatchImportAttachments.Resolve(store, Descriptor(asset.Entry), Operations(asset.Id), true));
        now = now.AddMinutes(1);
        Error(InventorErrorCodes.ASSET_NOT_FOUND, () => SketchBatchImportAttachments.Resolve(store, Descriptor(asset.Entry), Operations(asset.Id), true));
        Assert.Equal(0, store.ReservedBytes);
    }
}
