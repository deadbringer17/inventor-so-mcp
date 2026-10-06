using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Tests.Backend;

public class AssetCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xrso-cache-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Id(char c) => "a_" + new string(c, 64);
    private static byte[] Bytes(int n) => new byte[n];

    [Fact]
    public void FileCacheEvictsTheLeastRecentlyUsedFirstWhenOverTheCap()
    {
        var cache = new FileAssetCache(_dir, maxBytes: 250);
        cache.Put(Id('1'), Bytes(100));
        cache.Put(Id('2'), Bytes(100));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, Id('1') + ".glb"), DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, Id('2') + ".glb"), DateTime.UtcNow.AddMinutes(-5));
        cache.Put(Id('3'), Bytes(100));   // 300 > 250: the oldest goes
        Assert.False(cache.TryGet(Id('1'), out _));
        Assert.True(cache.TryGet(Id('2'), out _));
        Assert.True(cache.TryGet(Id('3'), out _));
    }

    [Fact]
    public void TryGetRefreshesRecencySoAReadEntrySurvives()
    {
        var cache = new FileAssetCache(_dir, maxBytes: 250);
        cache.Put(Id('1'), Bytes(100));
        cache.Put(Id('2'), Bytes(100));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, Id('1') + ".glb"), DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, Id('2') + ".glb"), DateTime.UtcNow.AddMinutes(-5));
        Assert.True(cache.TryGet(Id('1'), out _));   // 1 is now the newest
        cache.Put(Id('3'), Bytes(100));
        Assert.True(cache.TryGet(Id('1'), out _));
        Assert.False(cache.TryGet(Id('2'), out _));
    }

    [Fact]
    public void TryGetMovesTheFileTimestampForward()
    {
        var cache = new FileAssetCache(_dir);
        cache.Put(Id('1'), Bytes(10));
        var path = Path.Combine(_dir, Id('1') + ".glb");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-3));
        Assert.True(cache.TryGet(Id('1'), out _));
        Assert.True(File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void AnEntryLargerThanTheCapIsKeptRatherThanDroppedAtOnce()
    {
        var cache = new FileAssetCache(_dir, maxBytes: 50);
        cache.Put(Id('1'), Bytes(100));
        Assert.True(cache.TryGet(Id('1'), out _));
    }

    [Fact]
    public void PutOfAnExistingIdIsANoOpAndLeavesNoTempFiles()
    {
        var cache = new FileAssetCache(_dir);
        cache.Put(Id('1'), Bytes(10));
        cache.Put(Id('1'), Bytes(10));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir, "*.glb"));
    }

    [Fact]
    public void ConcurrentPutOfTheSameIdNeverThrows()
    {
        var cache = new FileAssetCache(_dir);
        var payload = new byte[4096];
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        for (int round = 0; round < 20; round++)
        {
            var id = Id((char)('a' + round % 6));
            using var start = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
            {
                start.Wait();
                try { cache.Put(id, payload); } catch (Exception ex) { errors.Add(ex); }
            })).ToList();
            threads.ForEach(t => t.Start());
            start.Set();
            threads.ForEach(t => t.Join());
        }
        Assert.Empty(errors);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.True(cache.TryGet(Id('a'), out var bytes));
        Assert.Equal(payload.Length, bytes.Length);
    }

    [Fact]
    public void MemoryCacheEvictsLeastRecentlyUsedByEntryCount()
    {
        var cache = new MemoryAssetCache(maxEntries: 2);
        cache.Put("a", Bytes(1)); cache.Put("b", Bytes(1));
        Assert.True(cache.TryGet("a", out _));   // a is newer than b
        cache.Put("c", Bytes(1));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void MemoryCacheEvictsByBytesButKeepsTheNewestEntry()
    {
        var cache = new MemoryAssetCache(maxBytes: 100);
        cache.Put("a", Bytes(60)); cache.Put("b", Bytes(60));
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        cache.Put("big", Bytes(500));
        Assert.True(cache.TryGet("big", out _));
        Assert.Equal(1, cache.Count);
        Assert.Equal(500, cache.TotalBytes);
    }

    [Fact]
    public void UnboundedMemoryCacheKeepsEverythingAndReplacesInPlace()
    {
        var cache = new MemoryAssetCache();
        for (int i = 0; i < 100; i++) cache.Put("k" + i, Bytes(10));
        cache.Put("k0", Bytes(20));
        Assert.Equal(100, cache.Count);
        Assert.Equal(1010, cache.TotalBytes);
    }
}
