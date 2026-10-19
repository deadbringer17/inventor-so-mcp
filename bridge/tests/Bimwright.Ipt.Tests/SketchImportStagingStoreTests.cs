using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class SketchImportStagingStoreTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private static SketchImportStagingPolicy Policy(int size = 8, long total = 16, int records = 2, int chunk = 4)
        => new(size, total, records, chunk, TimeSpan.FromMinutes(1), 32);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Upload(SketchImportStagingStore store, byte[] bytes)
    {
        string id = store.Begin("owner", "session", bytes.Length);
        for (int i = 0; i < bytes.Length; i += 4)
            store.Append("owner", "session", id, i, bytes.Skip(i).Take(4).ToArray());
        store.Seal("owner", "session", id, Hash(bytes)); return id;
    }

    [Fact]
    public void ChunkUploadSealsAndReadsDetachedBytes()
    {
        using var store = new SketchImportStagingStore(Policy());
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        string id = store.Begin("owner", "session", 5);
        Assert.StartsWith("st_", id); Assert.Equal(35, id.Length);
        byte[] chunk = { 1, 2, 3, 4 };
        store.Append("owner", "session", id, 0, chunk); chunk[0] = 99;
        store.Append("owner", "session", id, 4, new byte[] { 5 });
        store.Seal("owner", "session", id, Hash(bytes).ToUpperInvariant());
        using var lease = store.Acquire("owner", "session", id)!;
        Assert.Equal(id, lease.Id); Assert.Equal(5, lease.Size); Assert.Equal(Hash(bytes), lease.Sha256);
        byte[] read = lease.ReadBytes(); Assert.Equal(bytes, read); read[0] = 99;
        Assert.Equal(bytes, lease.ReadBytes()); Assert.Equal(5, store.ReservedBytes);
    }

    [Theory]
    [InlineData("foreign", "session")]
    [InlineData("owner", "foreign")]
    [InlineData("OWNER", "session")]
    [InlineData("owner", "SESSION")]
    public void ForeignIdentityCannotReadAppendSealOrDiscard(string owner, string session)
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = Upload(store, new byte[] { 1, 2 });
        Assert.Null(store.Acquire(owner, session, id));
        Assert.False(store.Discard(owner, session, id));
        Assert.Throws<KeyNotFoundException>(() => store.Append(owner, session, id, 0, new byte[] { 1 }));
        Assert.Throws<KeyNotFoundException>(() => store.Seal(owner, session, id, Hash(new byte[] { 1, 2 })));
        using var lease = store.Acquire("owner", "session", id)!;
        Assert.Equal(new byte[] { 1, 2 }, lease.ReadBytes());
    }

    [Fact]
    public void FailedChunksNeverAdvanceOffsetOrOverwriteBytes()
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = store.Begin("owner", "session", 5);
        Assert.Throws<ArgumentException>(() => store.Append("owner", "session", id, 1, new byte[] { 9 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Append("owner", "session", id, 0, Array.Empty<byte>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Append("owner", "session", id, 0, new byte[5]));
        store.Append("owner", "session", id, 0, new byte[] { 1, 2, 3, 4 });
        Assert.Throws<ArgumentException>(() => store.Append("owner", "session", id, 0, new byte[] { 9 }));
        Assert.Throws<ArgumentException>(() => store.Append("owner", "session", id, 4, new byte[] { 9, 9 }));
        store.Append("owner", "session", id, 4, new byte[] { 5 });
        store.Seal("owner", "session", id, Hash(new byte[] { 1, 2, 3, 4, 5 }));
        using var lease = store.Acquire("owner", "session", id)!;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, lease.ReadBytes());
    }

    [Fact]
    public void UnsealedReadAndIncompleteSealAreRefusedButCanContinue()
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = store.Begin("owner", "session", 2);
        store.Append("owner", "session", id, 0, new byte[] { 1 });
        Assert.Throws<InvalidOperationException>(() => store.Acquire("owner", "session", id));
        Assert.Throws<InvalidOperationException>(() => store.Seal("owner", "session", id, Hash(new byte[] { 1, 2 })));
        store.Append("owner", "session", id, 1, new byte[] { 2 });
        store.Seal("owner", "session", id, Hash(new byte[] { 1, 2 }));
        Assert.Throws<InvalidOperationException>(() => store.Append("owner", "session", id, 2, new byte[] { 3 }));
        Assert.Throws<InvalidOperationException>(() => store.Seal("owner", "session", id, Hash(new byte[] { 1, 2 })));
    }

    [Fact]
    public void DigestMismatchRetiresAndReleasesTheTransfer()
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = store.Begin("owner", "session", 2);
        store.Append("owner", "session", id, 0, new byte[] { 1, 2 });
        Assert.Throws<ArgumentException>(() => store.Seal("owner", "session", id, "not a digest"));
        Assert.Equal(2, store.ReservedBytes);
        Assert.Throws<InvalidOperationException>(() => store.Seal("owner", "session", id, Hash(new byte[] { 2, 1 })));
        Assert.Null(store.Acquire("owner", "session", id)); Assert.Equal(0, store.ReservedBytes);
        Assert.Equal(0, store.ResidentTransfers);
    }

    [Fact]
    public void FullDeclaredSizeAndRecordCountAreReservedBeforeAnyChunk()
    {
        using var store = new SketchImportStagingStore(Policy(total: 10));
        string first = store.Begin("owner", "session", 8);
        Assert.Throws<InvalidOperationException>(() => store.Begin("other", "session", 3));
        store.Begin("other", "session", 2);
        Assert.Throws<InvalidOperationException>(() => store.Begin("owner", "session", 1));
        Assert.Equal(10, store.ReservedBytes); Assert.Equal(2, store.ResidentTransfers);
        Assert.True(store.Discard("owner", "session", first));
        Assert.NotEqual(first, store.Begin("owner", "session", 8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetiredLeasesKeepTheirBudgetAndBytesUntilLastDisposal(bool expire)
    {
        var now = Epoch;
        using var store = new SketchImportStagingStore(Policy(size: 8, total: 8, records: 1), () => now);
        string id = Upload(store, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var first = store.Acquire("owner", "session", id)!;
        var second = store.Acquire("owner", "session", id)!;
        if (expire) { now = now.AddMinutes(1); store.SweepExpired(); }
        else Assert.True(store.Discard("owner", "session", id));
        Assert.Null(store.Acquire("owner", "session", id));
        Assert.Throws<InvalidOperationException>(() => store.Begin("owner", "session", 1));
        Assert.Equal(8, store.ReservedBytes); Assert.Equal(1, store.ResidentTransfers);
        first.Dispose(); first.Dispose(); Assert.Equal(8, second.ReadBytes().Length);
        Assert.Throws<ObjectDisposedException>(() => first.ReadBytes());
        second.Dispose(); Assert.Equal(0, store.ReservedBytes); Assert.Equal(0, store.ResidentTransfers);
        Assert.NotNull(store.Begin("owner", "session", 8));
    }

    [Fact]
    public void PartialUploadExpiresAtCreationDeadlineWithoutSlidingOnAppend()
    {
        var now = Epoch;
        using var store = new SketchImportStagingStore(Policy(), () => now);
        string id = store.Begin("owner", "session", 2);
        now = now.AddSeconds(59); store.Append("owner", "session", id, 0, new byte[] { 1 });
        now = now.AddSeconds(1);
        Assert.Throws<KeyNotFoundException>(() => store.Append("owner", "session", id, 1, new byte[] { 2 }));
        Assert.Null(store.Acquire("owner", "session", id)); Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void StoreDisposalRetiresCatalogWhileDetachedLeaseRemainsUsable()
    {
        var store = new SketchImportStagingStore(Policy());
        string id = Upload(store, new byte[] { 1, 2 });
        store.Begin("owner", "session", 4);
        var lease = store.Acquire("owner", "session", id)!;
        store.Dispose(); store.Dispose();
        Assert.Equal(2, store.ReservedBytes); Assert.Equal(new byte[] { 1, 2 }, lease.ReadBytes());
        Assert.Throws<ObjectDisposedException>(() => store.Begin("owner", "session", 1));
        Assert.Throws<ObjectDisposedException>(() => store.Acquire("owner", "session", id));
        Assert.Throws<ObjectDisposedException>(() => store.Discard("owner", "session", id));
        lease.Dispose(); Assert.Equal(0, store.ReservedBytes); Assert.Equal(0, store.ResidentTransfers);
    }

    [Fact]
    public void ConcurrentFinalAppendsPermitExactlyOneWriter()
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = store.Begin("owner", "session", 1);
        var outcomes = new bool[16];
        Parallel.For(0, outcomes.Length, i =>
        {
            try { store.Append("owner", "session", id, 0, new byte[] { 1 }); outcomes[i] = true; }
            catch (ArgumentException) { }
        });
        Assert.Equal(1, outcomes.Count(success => success));
        store.Seal("owner", "session", id, Hash(new byte[] { 1 }));
        var lease = store.Acquire("owner", "session", id)!;
        store.Discard("owner", "session", id);
        Parallel.For(0, 16, _ => lease.Dispose());
        Assert.Equal(0, store.ReservedBytes); Assert.Equal(0, store.ResidentTransfers);
    }

    [Fact]
    public void InvalidLimitsAndIdentitiesCannotReserveMemory()
    {
        Assert.Throws<ArgumentException>(() => new SketchImportStagingPolicy(0, 1, 1, 1, TimeSpan.FromMinutes(1), 32));
        Assert.Throws<ArgumentException>(() => new SketchImportStagingPolicy(8, 7, 1, 1, TimeSpan.FromMinutes(1), 32));
        Assert.Throws<ArgumentException>(() => new SketchImportStagingPolicy(8, 8, 0, 1, TimeSpan.FromMinutes(1), 32));
        Assert.Throws<ArgumentException>(() => new SketchImportStagingPolicy(8, 8, 1, 9, TimeSpan.FromMinutes(1), 32));
        Assert.Throws<ArgumentException>(() => new SketchImportStagingPolicy(8, 8, 1, 1, TimeSpan.Zero, 32));
        using var store = new SketchImportStagingStore(Policy());
        Assert.Throws<ArgumentException>(() => store.Begin(" ", "session", 1));
        Assert.Throws<ArgumentException>(() => store.Begin("owner", new string('s', 33), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Begin("owner", "session", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Begin("owner", "session", 9));
        Assert.Equal(0, store.ReservedBytes);
    }

    [Fact]
    public void ChunkReadsReturnOnlyDetachedRequestedRangeIncludingExactEnd()
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = Upload(store, new byte[] { 1, 2, 3, 4, 5, 6 });
        using var lease = store.Acquire("owner", "session", id)!;
        byte[] first = lease.ReadChunk(0, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first);
        Assert.Equal(new byte[] { 5, 6 }, lease.ReadChunk(4, 2));
        Assert.Equal(new byte[] { 6 }, lease.ReadChunk(5, 1));
        first[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, lease.ReadChunk(0, 4));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, lease.ReadBytes());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 5)]
    [InlineData(0, int.MaxValue)]
    [InlineData(-1, 1)]
    [InlineData(6, 1)]
    [InlineData(5, 2)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MaxValue, 4)]
    [InlineData(int.MinValue, 1)]
    public void ChunkReadsRefuseInvalidCountRangeAndOverflow(int offset, int count)
    {
        using var store = new SketchImportStagingStore(Policy());
        string id = Upload(store, new byte[] { 1, 2, 3, 4, 5, 6 });
        using var lease = store.Acquire("owner", "session", id)!;
        Assert.Throws<ArgumentOutOfRangeException>(() => lease.ReadChunk(offset, count));
        Assert.Equal(new byte[] { 3, 4 }, lease.ReadChunk(2, 2));
    }

    [Theory]
    [InlineData("expire")]
    [InlineData("discard")]
    [InlineData("dispose")]
    public void ChunkReadsRemainPinnedUntilLeaseDisposal(string retirement)
    {
        var now = Epoch;
        using var store = new SketchImportStagingStore(Policy(), () => now);
        string id = Upload(store, new byte[] { 1, 2, 3, 4, 5 });
        var lease = store.Acquire("owner", "session", id)!;
        if (retirement == "expire") { now = now.AddMinutes(1); store.SweepExpired(); }
        else if (retirement == "discard") Assert.True(store.Discard("owner", "session", id));
        else store.Dispose();
        byte[] detached = lease.ReadChunk(2, 3);
        Assert.Equal(new byte[] { 3, 4, 5 }, detached);
        Assert.Equal(5, store.ReservedBytes);
        lease.Dispose();
        Assert.Equal(0, store.ReservedBytes);
        Assert.Equal(new byte[] { 3, 4, 5 }, detached);
        Assert.Throws<ObjectDisposedException>(() => lease.ReadChunk(0, 1));
        Assert.Throws<ObjectDisposedException>(() => lease.ReadChunk(-1, 0));
    }
}
