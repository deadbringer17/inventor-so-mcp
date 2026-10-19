using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Imports;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class ImportResourceStoreTests
{
    private static ImportResourceStorePolicy Policy(long perResource = 10, long total = 20, int records = 10,
        int metadataBytes = 1024, TimeSpan? ttl = null)
        => new(perResource, total, records, metadataBytes, 32, ttl ?? TimeSpan.FromMinutes(1), new[] { "png", "dwg" });
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-11T00:00:00Z");

    [Fact]
    public void ContentIsHashedAndOwnerCatalogsRemainIndependent()
    {
        var store = new ImportResourceStore(Policy());
        byte[] bytes = { 1, 2, 3 };
        var alice = store.Put("alice", bytes, "png", new JObject { ["label"] = "alice" });
        var bob = store.Put("bob", bytes, "dwg", new JObject { ["label"] = "bob" });
        Assert.Equal("a_" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), alice.Id);
        Assert.Equal(alice.Id, bob.Id); Assert.Equal(3, store.StoredBytes);
        Assert.Equal("png", store.Describe("alice", alice.Id)!.Format);
        Assert.Equal("dwg", store.Describe("bob", alice.Id)!.Format);
        Assert.Equal("alice", (string?)store.Describe("alice", alice.Id)!.Metadata["label"]);
        Assert.Equal("bob", (string?)store.Describe("bob", alice.Id)!.Metadata["label"]);
        Assert.Null(store.Describe("charlie", alice.Id)); Assert.Empty(store.List("charlie"));
        Assert.Null(store.Acquire("charlie", new[] { alice.Id }));
        Assert.False(store.TryRead("charlie", alice.Id, out var absent)); Assert.Null(absent);
    }

    [Fact]
    public void PutAndReadAndMetadataAreDefensiveCopies()
    {
        var store = new ImportResourceStore(Policy());
        byte[] input = { 1, 2, 3 };
        var metadata = new JObject { ["nested"] = new JObject { ["value"] = 1 } };
        var record = store.Put("alice", input, "png", metadata);
        input[0] = 99; metadata["nested"]!["value"] = 99;
        record.Metadata["nested"]!["value"] = 98;
        var listed = Assert.Single(store.List("alice")); listed.Metadata["nested"]!["value"] = 97;
        Assert.True(store.TryRead("alice", record.Id, out var bytes)); Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        bytes![0] = 96;
        Assert.True(store.TryRead("alice", record.Id, out bytes)); Assert.Equal(1, bytes![0]);
        Assert.Equal(1, (int)store.Describe("alice", record.Id)!.Metadata["nested"]!["value"]!);
    }

    [Fact]
    public void LeaseFreezesOwnerMetadataWhileCatalogCanBeUpdated()
    {
        var store = new ImportResourceStore(Policy());
        var old = store.Put("alice", new byte[] { 1 }, "png", new JObject { ["version"] = 1 });
        using var lease = store.Acquire("alice", new[] { old.Id }); Assert.NotNull(lease);
        store.Put("alice", new byte[] { 1 }, "dwg", new JObject { ["version"] = 2 });
        Assert.Equal("png", Assert.Single(lease!.Records).Format);
        Assert.Equal(1, (int)lease.Records[0].Metadata["version"]!);
        Assert.Equal("dwg", store.Describe("alice", old.Id)!.Format);
        Assert.Equal(2, (int)store.Describe("alice", old.Id)!.Metadata["version"]!);
    }

    [Fact]
    public void MetadataNormalizesClrByteArraysToJsonAndNeverSharesTheirBackingBuffers()
    {
        var store = new ImportResourceStore(Policy());
        byte[] original = { 1, 2, 3 };
        var input = new JObject { ["payload"] = new JValue(original),
            ["nested"] = new JArray(new JValue(original)) };
        var record = store.Put("alice", new byte[] { 9 }, "png", input);
        using var lease = store.Acquire("alice", new[] { record.Id });
        original[0] = 99;
        Assert.Equal(JTokenType.String, record.Metadata["payload"]!.Type);
        Assert.Equal("AQID", (string?)record.Metadata["payload"]);
        Assert.Equal("AQID", (string?)record.Metadata["nested"]![0]);
        var getter = record.Metadata;
        byte[] replacement = { 4, 5, 6 };
        getter["payload"] = new JValue(replacement);
        replacement[0] = 88;
        Assert.Equal("AQID", (string?)record.Metadata["payload"]);
        Assert.Equal("AQID", (string?)store.Describe("alice", record.Id)!.Metadata["payload"]);
        Assert.Equal("AQID", (string?)Assert.Single(lease!.Records).Metadata["payload"]);
        byte[] decoded = Convert.FromBase64String((string)record.Metadata["payload"]!);
        decoded[0] = 77;
        Assert.Equal("AQID", (string?)record.Metadata["payload"]);
    }

    [Fact]
    public void MetadataByteBudgetAppliesToTheNormalizedJsonRepresentation()
    {
        var store = new ImportResourceStore(Policy(metadataBytes: 10));
        Assert.Throws<ArgumentException>(() => store.Put("alice", new byte[] { 1 }, "png",
            new JObject { ["payload"] = new JValue(new byte[] { 1, 2, 3 }) }));
        Assert.Empty(store.List("alice")); Assert.Equal(0, store.StoredBytes);
    }

    [Fact]
    public void ExpiryIsOwnerSpecificAndExactAtTheBoundary()
    {
        var now = Epoch; var store = new ImportResourceStore(Policy(), () => now);
        var record = store.Put("alice", new byte[] { 1 }, "png");
        now = now.AddSeconds(30); store.Put("bob", new byte[] { 1 }, "png");
        now = Epoch.AddMinutes(1);
        Assert.Null(store.Describe("alice", record.Id)); Assert.NotNull(store.Describe("bob", record.Id));
        Assert.Equal(1, store.StoredBytes);
        now = now.AddSeconds(30); Assert.Empty(store.List("bob")); Assert.Equal(0, store.StoredBytes);
    }

    [Fact]
    public void LeasePinsBytesPastCatalogExpiryUntilDisposed()
    {
        var now = Epoch; var store = new ImportResourceStore(Policy(), () => now);
        var record = store.Put("alice", new byte[] { 1, 2, 3 }, "png");
        var lease = store.Acquire("alice", new[] { record.Id }); Assert.NotNull(lease);
        now = now.AddMinutes(2);
        Assert.Null(store.Describe("alice", record.Id)); Assert.Null(store.Acquire("alice", new[] { record.Id }));
        Assert.Equal(3, store.StoredBytes); Assert.Equal(new byte[] { 1, 2, 3 }, lease!.Read(record.Id));
        lease.Dispose(); lease.Dispose();
        Assert.Equal(0, store.StoredBytes); Assert.Throws<ObjectDisposedException>(() => lease.Read(record.Id));
    }

    [Fact]
    public void ByteEvictionUsesLeastRecentlyAccessedUnleasedBlob()
    {
        var store = new ImportResourceStore(Policy(3, 6));
        var a = store.Put("alice", new byte[] { 1, 1, 1 }, "png");
        var b = store.Put("alice", new byte[] { 2, 2, 2 }, "png");
        Assert.NotNull(store.Describe("alice", a.Id));
        var c = store.Put("alice", new byte[] { 3, 3, 3 }, "png");
        Assert.NotNull(store.Describe("alice", a.Id)); Assert.Null(store.Describe("alice", b.Id));
        Assert.NotNull(store.Describe("alice", c.Id)); Assert.Equal(6, store.StoredBytes);
    }

    [Fact]
    public void LeasesPreventEvictionAndFailedPutDoesNotPartiallyEvict()
    {
        var store = new ImportResourceStore(Policy(4, 6));
        var a = store.Put("alice", new byte[] { 1, 1, 1 }, "png");
        var b = store.Put("alice", new byte[] { 2, 2 }, "png");
        using var lease = store.Acquire("alice", new[] { a.Id });
        Assert.Throws<InvalidOperationException>(() => store.Put("alice", new byte[] { 3, 3, 3, 3 }, "png"));
        Assert.NotNull(store.Describe("alice", b.Id)); Assert.Equal(5, store.StoredBytes);
        Assert.Equal(new byte[] { 1, 1, 1 }, lease!.Read(a.Id));
    }

    [Fact]
    public void DisposingLeaseMakesBlobEvictableAgain()
    {
        var store = new ImportResourceStore(Policy(2, 2));
        var a = store.Put("alice", new byte[] { 1, 1 }, "png");
        var lease = store.Acquire("alice", new[] { a.Id });
        Assert.Throws<InvalidOperationException>(() => store.Put("alice", new byte[] { 2, 2 }, "png"));
        lease!.Dispose(); store.Put("alice", new byte[] { 2, 2 }, "png");
        Assert.Null(store.Describe("alice", a.Id)); Assert.Equal(2, store.StoredBytes);
    }

    [Fact]
    public void FailedBundleAcquirePinsNothingAndMultipleLeasesReleaseIndependently()
    {
        var store = new ImportResourceStore(Policy(2, 2));
        var a = store.Put("alice", new byte[] { 1, 1 }, "png");
        string absent = "a_" + new string('0', 64);
        Assert.Null(store.Acquire("alice", new[] { a.Id, absent }));
        var first = store.Acquire("alice", new[] { a.Id });
        var second = store.Acquire("alice", new[] { a.Id });
        first!.Dispose(); Assert.Throws<InvalidOperationException>(() => store.Put("alice", new byte[] { 2, 2 }, "png"));
        second!.Dispose(); store.Put("alice", new byte[] { 2, 2 }, "png");
        Assert.Null(store.Describe("alice", a.Id));
    }

    [Fact]
    public void OwnerRecordsHaveAnExplicitCapacityEvenWhenBytesAreDeduplicated()
    {
        var store = new ImportResourceStore(Policy(records: 1));
        var a = store.Put("alice", new byte[] { 1 }, "png");
        Assert.Throws<InvalidOperationException>(() => store.Put("bob", new byte[] { 1 }, "png"));
        Assert.NotNull(store.Describe("alice", a.Id)); Assert.Empty(store.List("bob"));
        store.Put("alice", new byte[] { 2 }, "png"); Assert.Null(store.Describe("alice", a.Id));
    }

    [Theory]
    [InlineData("C:\\bad.png")] [InlineData("/sdcard/bad")] [InlineData("a_bad")]
    [InlineData("")] [InlineData(null)]
    public void MalformedIdsNeverResolveToAResource(string? id)
    {
        var store = new ImportResourceStore(Policy());
        Assert.Null(store.Describe("alice", id)); Assert.False(store.TryRead("alice", id, out _));
    }

    [Fact]
    public void PolicyAndInputValidationAreExplicit()
    {
        Assert.Throws<ArgumentException>(() => new ImportResourceStorePolicy(0, 1, 1, 2, 3, TimeSpan.FromMinutes(1), new[] { "png" }));
        Assert.Throws<ArgumentException>(() => new ImportResourceStorePolicy(2, 1, 1, 2, 3, TimeSpan.FromMinutes(1), new[] { "png" }));
        Assert.Throws<ArgumentException>(() => new ImportResourceStorePolicy(1, 1, 1, 2, 3, TimeSpan.Zero, new[] { "png" }));
        Assert.Throws<ArgumentException>(() => new ImportResourceStorePolicy(1, 1, 1, 2, 3, TimeSpan.FromMinutes(1), new[] { "png", "png" }));
        var store = new ImportResourceStore(Policy(metadataBytes: 2));
        Assert.Throws<ArgumentException>(() => store.Put(" ", new byte[] { 1 }, "png"));
        Assert.Throws<ArgumentException>(() => store.Put("alice", Array.Empty<byte>(), "png"));
        Assert.Throws<ArgumentException>(() => store.Put("alice", new byte[11], "png"));
        Assert.Throws<ArgumentException>(() => store.Put("alice", new byte[] { 1 }, "jpeg"));
        Assert.Throws<ArgumentException>(() => store.Put("alice", new byte[] { 1 }, "png", new JObject { ["x"] = 1 }));
        var a = store.Put("alice", new byte[] { 1 }, "png");
        Assert.Throws<ArgumentException>(() => store.Acquire("alice", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => store.Acquire("alice", new[] { a.Id, a.Id }));
    }

    private static void CorruptForTest(ImportResourceStore store, string id)
    {
        // Deliberate memory fault through reflection, not a production mutation API.
        var blobs = (IDictionary)typeof(ImportResourceStore).GetField("_blobs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        object blob = blobs[id]!;
        var bytes = (byte[])blob.GetType().GetField("Bytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(blob)!;
        bytes[0] ^= 255;
    }

    [Fact]
    public void IntegrityFaultInvalidatesAllOwnersAndActiveLeaseWithoutFreeingPinnedBudgetEarly()
    {
        var store = new ImportResourceStore(Policy());
        var a = store.Put("alice", new byte[] { 1, 2 }, "png");
        store.Put("bob", new byte[] { 1, 2 }, "dwg");
        var lease = store.Acquire("alice", new[] { a.Id });
        CorruptForTest(store, a.Id);
        Assert.Null(store.Describe("bob", a.Id)); Assert.Empty(store.List("alice"));
        Assert.Equal(2, store.StoredBytes);
        Assert.Throws<InvalidOperationException>(() => lease!.Read(a.Id));
        Assert.Throws<InvalidOperationException>(() => store.Put("alice", new byte[] { 1, 2 }, "png"));
        lease!.Dispose(); Assert.Equal(0, store.StoredBytes);
        Assert.Equal(a.Id, store.Put("alice", new byte[] { 1, 2 }, "png").Id);
    }

    [Fact]
    public void UnleasedCorruptContentCannotBeReadAndCanBeRegisteredAgain()
    {
        var store = new ImportResourceStore(Policy());
        var a = store.Put("alice", new byte[] { 1 }, "png"); CorruptForTest(store, a.Id);
        Assert.False(store.TryRead("alice", a.Id, out _)); Assert.Equal(0, store.StoredBytes);
        Assert.Equal(a.Id, store.Put("alice", new byte[] { 1 }, "png").Id);
    }

    [Fact]
    public async Task ConcurrentOwnersAndLeasesDoNotExposeMutableSharedBuffers()
    {
        var store = new ImportResourceStore(Policy(records: 100));
        await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(() =>
        {
            string owner = "owner" + index;
            var record = store.Put(owner, new byte[] { 1, 2, 3 }, index % 2 == 0 ? "png" : "dwg", new JObject { ["owner"] = index });
            using var lease = store.Acquire(owner, new[] { record.Id }); Assert.NotNull(lease);
            var copy = lease!.Read(record.Id); copy[0] = 99;
            Assert.Equal(1, lease.Read(record.Id)[0]);
            Assert.Equal(index, (int)Assert.Single(store.List(owner)).Metadata["owner"]!);
        })));
        Assert.Equal(3, store.StoredBytes);
    }
}
