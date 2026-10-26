using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventor.So.Mcp.Http.Capture;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class CaptureTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"xrso-capture-transfer-" + Guid.NewGuid().ToString("N"));
    private readonly string _owner = CaptureTransferStore.TokenOwner("token-a");
    private static CaptureUpload Upload(byte[] bytes) => new(Guid.NewGuid().ToString("N"),new[] { new CaptureFile("capture_manifest.json",bytes.Length,CaptureTransferStore.Hash(bytes)) });
    private static JObject Json(object value) => JObject.FromObject(value);

    [Fact]
    public async Task ResumeSurvivesRestartAndFinalizationVerifiesRaw()
    {
        byte[] bytes = { 1,2,3,4 }; var upload = Upload(bytes); var store = new CaptureTransferStore(_root);
        store.Create(_owner,upload);
        await store.PutAsync(_owner,upload.ScanId,0,CaptureTransferStore.Hash(bytes),bytes.Length,new MemoryStream(bytes),CancellationToken.None);
        var reopened = new CaptureTransferStore(_root);
        Assert.Equal(0,(int)Json(reopened.Describe(_owner,upload.ScanId))["received"]![0]!);
        Assert.True((bool)Json(reopened.Finalize(_owner,upload.ScanId))["finalized"]!);
        Assert.True((bool)Json(reopened.Create(_owner,upload))["finalized"]!);
    }

    [Fact]
    public async Task HashMismatchNeverPublishesAndReplayDoesNotOverwrite()
    {
        byte[] bytes = { 1,2,3,4 }; var upload = Upload(bytes); var store = new CaptureTransferStore(_root); store.Create(_owner,upload);
        var ex = await Assert.ThrowsAsync<CaptureTransferException>(() => store.PutAsync(_owner,upload.ScanId,0,CaptureTransferStore.Hash(bytes),4,new MemoryStream(new byte[4]),CancellationToken.None));
        Assert.Equal("CAPTURE_HASH_MISMATCH",ex.Code);
        Assert.Empty(Json(store.Describe(_owner,upload.ScanId))["received"]!);
        for (int i = 0; i < 2; i++) await store.PutAsync(_owner,upload.ScanId,0,CaptureTransferStore.Hash(bytes),4,new MemoryStream(bytes),CancellationToken.None);
        Assert.Single(Json(store.Describe(_owner,upload.ScanId))["received"]!);
        Assert.Empty(Directory.EnumerateFiles(_root,"*.pending*",SearchOption.AllDirectories));
    }

    [Fact]
    public void ForeignTokenAndRotatedTokenCannotAccessOrDelete()
    {
        var store = new CaptureTransferStore(_root); var upload = Upload(new byte[] { 1 }); store.Create(_owner,upload);
        string foreign = CaptureTransferStore.TokenOwner("token-b");
        Assert.Equal(404,Assert.Throws<CaptureTransferException>(() => store.Describe(foreign,upload.ScanId)).Status);
        Assert.Equal(404,Assert.Throws<CaptureTransferException>(() => store.Delete(foreign,upload.ScanId)).Status);
        Assert.Equal(404,Assert.Throws<CaptureTransferException>(() => store.Finalize(foreign,upload.ScanId)).Status);
        Assert.False((bool)Json(store.Describe(_owner,upload.ScanId))["finalized"]!);
    }

    [Theory]
    [InlineData("../capture_manifest.json")]
    [InlineData("C:/sensitive.txt")]
    [InlineData("frames/00000000/../../secret")]
    public void ManifestPathsAreClosedVocabulary(string path)
    {
        var upload = Upload(new byte[] {1});
        upload = upload with { Files = new[] { upload.Files[0] with { Path = path } } };
        Assert.Throws<CaptureTransferException>(() => new CaptureTransferStore(_root).Create(_owner,upload));
    }

    [Fact]
    public async Task CancellationLeavesNoVisibleChunkAndRetrySucceeds()
    {
        byte[] bytes = {1,2,3}; var upload = Upload(bytes); var store = new CaptureTransferStore(_root); store.Create(_owner,upload);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PutAsync(_owner,upload.ScanId,0,CaptureTransferStore.Hash(bytes),3,new MemoryStream(bytes),cancellation.Token));
        Assert.Empty(Json(store.Describe(_owner,upload.ScanId))["received"]!);
        await store.PutAsync(_owner,upload.ScanId,0,CaptureTransferStore.Hash(bytes),3,new MemoryStream(bytes),CancellationToken.None);
        store.Finalize(_owner,upload.ScanId);
    }

    [Fact]
    public void RetentionAndExplicitDeletionRemoveOwnedRaw()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new CaptureTransferStore(_root,() => now); var upload = Upload(new byte[] { 1 });
        store.Create(_owner,upload); now += TimeSpan.FromDays(8);
        Assert.Equal(404,Assert.Throws<CaptureTransferException>(() => store.Describe(_owner,upload.ScanId)).Status);
        Assert.Empty(Directory.EnumerateFiles(_root,"*",SearchOption.AllDirectories));
        store.Create(_owner,upload); store.Delete(_owner,upload.ScanId);
        Assert.Equal(404,Assert.Throws<CaptureTransferException>(() => store.Describe(_owner,upload.ScanId)).Status);
    }

    [Fact]
    public void ManifestChangesAndIncompleteFinalizationAreRejected()
    {
        var store = new CaptureTransferStore(_root); var upload = Upload(new byte[] { 1 }); store.Create(_owner,upload);
        Assert.Equal("CAPTURE_INCOMPLETE",Assert.Throws<CaptureTransferException>(() => store.Finalize(_owner,upload.ScanId)).Code);
        var changed = upload with { Files = new[] { upload.Files[0] with { Sha256 = new string('0',64) } } };
        Assert.Equal("CAPTURE_MANIFEST_CONFLICT",Assert.Throws<CaptureTransferException>(() => store.Create(_owner,changed)).Code);
    }

    [Fact]
    public void SessionQuotaIsReservedBeforeAnyChunkArrives()
    {
        var store = new CaptureTransferStore(_root);
        for (int i = 0; i < 16; i++) store.Create(_owner,Upload(new byte[] { 1 }));
        Assert.Equal("CAPTURE_QUOTA_EXCEEDED",Assert.Throws<CaptureTransferException>(() => store.Create(_owner,Upload(new byte[] { 1 }))).Code);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
}
