using InventorXrSo.Core.Capture;
using Newtonsoft.Json;

namespace XrSo.Core.Tests;

public sealed class RealityCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xrso-capture-tests-" + Guid.NewGuid().ToString("N"));
    private static CaptureTransform Translated(double mm)
    {
        var matrix = CaptureTransform.Identity.Values; matrix[3] = mm;
        return new CaptureTransform(matrix);
    }
    private static ScanSession Session() => new(Guid.NewGuid().ToString("N"), CaptureMode.Object, "test-device", "1.0",
        DateTimeOffset.UtcNow, new CaptureVolume(CaptureTransform.Identity, 600, 600, 600), CaptureTransform.Identity);
    private static CaptureFrame Frame(long timestamp = 1000000, double x = 0, bool tracked = true, CaptureQuality quality = null) =>
        new(timestamp, "left", new CaptureIntrinsics(16, 16, 12, 12, 8, 8), Translated(x), Translated(x), CaptureTransform.Identity,
            quality ?? new CaptureQuality(120, 0.01, 100, 0.3), tracked);
    private LocalScanStore Store(long quota = 1024*1024, long free = 1024*1024) => new(_root, quota, 0, () => free);
    private static byte[] Pixels => Enumerable.Range(0, 16*16*3).Select(i => (byte)(i%256)).ToArray();

    [Fact]
    public void MatricesKeepMillimetresAndDefensivelyCopy()
    {
        var values = Translated(1000).Values;
        var transform = new CaptureTransform(values);
        values[3] = 123;
        var copy = transform.Values; copy[3] = 42;
        Assert.Equal(1000, transform.DistanceMm(CaptureTransform.Identity));
        Assert.Equal(0, transform.AngleDegrees(CaptureTransform.Identity));
        Assert.Equal(1000, JsonConvert.DeserializeObject<CaptureTransform>(JsonConvert.SerializeObject(transform)).Values[3]);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(0)] [InlineData(-1)]
    public void IntrinsicsRejectUnusableFocalLength(double focal) => Assert.Throws<ArgumentException>(() => new CaptureIntrinsics(16,16,focal,12,8,8));

    [Fact]
    public void MatricesRejectScaleReflectionAndInvalidHomogeneousRow()
    {
        foreach (int index in new[] { 0, 12, 15 })
        {
            var values = CaptureTransform.Identity.Values; values[index] = 2;
            Assert.Throws<ArgumentException>(() => new CaptureTransform(values));
        }
        var reflection = CaptureTransform.Identity.Values; reflection[0] = -1;
        Assert.Throws<ArgumentException>(() => new CaptureTransform(reflection));
    }

    [Fact]
    public void SelectorCommitsOnlyAcceptedPersistedFrames()
    {
        var selector = new CaptureFrameSelector();
        Assert.Null(selector.Evaluate(Frame()));
        selector.Commit(Frame());
        Assert.Equal("redundant", selector.Evaluate(Frame(2000000, 10)));
        Assert.Null(selector.Evaluate(Frame(2000000, 100)));
        Assert.Equal("timestamp_not_increasing", selector.Evaluate(Frame()));
        Assert.Equal("motion", selector.Evaluate(Frame(2000000, 2000)));
        Assert.Equal("tracking_lost", selector.Evaluate(Frame(tracked:false)));
    }

    [Theory]
    [InlineData(5, 0, 100, 0.3, "exposure")]
    [InlineData(120, 0.9, 100, 0.3, "exposure")]
    [InlineData(120, 0.01, 1, 0.3, "low_sharpness")]
    [InlineData(120, 0.01, 100, 0.001, "low_texture")]
    public void SelectorReportsObservableQuality(double luminance, double clipped, double sharpness, double texture, string reason) =>
        Assert.Equal(reason, new CaptureFrameSelector().Evaluate(Frame(quality: new CaptureQuality(luminance, clipped, sharpness, texture))));

    [Fact]
    public void FlatDarkImagesHaveNoInventedTextureOrSharpness()
    {
        var quality = CaptureQualityEvaluator.EvaluateRgb(new byte[16*16*3],16,16);
        Assert.Equal(0, quality.MeanLuminance); Assert.Equal(1, quality.ClippedFraction);
        Assert.Equal(0, quality.TextureFraction); Assert.Equal(0, quality.LaplacianVariance);
    }

    [Fact]
    public void RawSessionReopensWithHashesAndOriginalMetadata()
    {
        var store = Store(); var session = Session(); var recorder = new ScanRecorder(store, session);
        recorder.Start(); Assert.True(recorder.Record(Frame(), Pixels)); recorder.Stop();
        var reopened = Store();
        Assert.Equal(session.SessionId, reopened.Open(session.SessionId).SessionId);
        Assert.Equal(600, reopened.Open(session.SessionId).Volume.WidthMm);
        Assert.Equal(Pixels, reopened.ReadImage(session.SessionId,0));
        Assert.Equal(LocalScanStore.Hash(Pixels), Assert.Single(reopened.ReadFrames(session.SessionId)).Sha256);
        Assert.Equal(CaptureState.Completed, reopened.GetStatus(session.SessionId).State);
        Assert.Single(reopened.List());
    }

    [Fact]
    public void PauseAndResumePreserveTimestampBaselineAndRawData()
    {
        var store = Store(); var recorder = new ScanRecorder(store, Session()); recorder.Start();
        Assert.True(recorder.Record(Frame(), Pixels)); recorder.Pause();
        Assert.False(recorder.Record(Frame(2000000,100),Pixels)); recorder.Start();
        Assert.False(recorder.Record(Frame(),Pixels));
        Assert.True(recorder.Record(Frame(2000000,100),Pixels));
        Assert.Equal(2,store.ReadFrames(recorder.Session.SessionId).Count);
        Assert.Equal(Pixels,store.ReadImage(recorder.Session.SessionId,0));
    }

    [Fact]
    public void TrackingLossEndsSessionAndDisallowsResume()
    {
        var store = Store(); var recorder = new ScanRecorder(store,Session()); recorder.Start();
        Assert.False(recorder.Record(Frame(tracked:false),Pixels));
        Assert.Equal(CaptureState.Interrupted,recorder.State);
        Assert.Equal("tracking_lost",store.GetStatus(recorder.Session.SessionId).Reason);
        Assert.Throws<InvalidOperationException>(() => recorder.Start());
    }

    [Fact]
    public void SpaceExhaustionStopsWithoutPublishingHalfFrame()
    {
        long free = 1000000;
        var store = new LocalScanStore(_root,1000000,0,() => free);
        var recorder = new ScanRecorder(store,Session()); recorder.Start();
        Assert.True(recorder.Record(Frame(),Pixels)); free = 1;
        Assert.False(recorder.Record(Frame(2000000,100),Pixels));
        Assert.Equal(CaptureState.Interrupted,recorder.State); Assert.Equal("storage_full",recorder.LastReason);
        Assert.Single(store.ReadFrames(recorder.Session.SessionId));
        Assert.Equal(Pixels,store.ReadImage(recorder.Session.SessionId,0));
        Assert.Equal(CaptureState.Interrupted,store.GetStatus(recorder.Session.SessionId).State);
    }

    [Fact]
    public void CorruptRawIsDetectedInsteadOfReturned()
    {
        var store = Store(); var recorder = new ScanRecorder(store,Session()); recorder.Start(); recorder.Record(Frame(),Pixels);
        File.WriteAllBytes(Path.Combine(_root,recorder.Session.SessionId,"frames","00000000","image.rgb"),new byte[16*16*3]);
        Assert.Throws<InvalidDataException>(() => store.ReadImage(recorder.Session.SessionId,0));
    }

    [Fact]
    public void UnpublishedPartialFramesAreExcludedFromRecovery()
    {
        var store = Store(); var recorder = new ScanRecorder(store,Session()); recorder.Start(); recorder.Record(Frame(),Pixels);
        string pending = Path.Combine(_root,recorder.Session.SessionId,"frames","00000001.pending-test");
        Directory.CreateDirectory(pending); File.WriteAllText(Path.Combine(pending,"image.rgb"),"incomplete");
        Assert.Single(Store().ReadFrames(recorder.Session.SessionId));
    }

    [Fact]
    public void DuplicateSessionsAndTraversalCannotOverwriteRaw()
    {
        var store = Store(); var session = Session(); store.Create(session);
        Assert.Throws<IOException>(() => store.Create(session));
        Assert.Throws<ArgumentException>(() => store.Open("../outside"));
        Assert.Equal(session.SessionId, store.Open(session.SessionId).SessionId);
    }

    [Fact]
    public void StartupRecoveryMakesInterruptedRawAvailableForTransferWithoutResumingTracking()
    {
        var store = Store(); var recorder = new ScanRecorder(store,Session()); recorder.Start(); recorder.Record(Frame(),Pixels);
        var reopened = Store(); reopened.RecoverInterruptedSessions();
        Assert.Equal(CaptureState.Interrupted,reopened.GetStatus(recorder.Session.SessionId).State);
        Assert.Equal("application_restarted",reopened.GetStatus(recorder.Session.SessionId).Reason);
        Assert.Equal(3,reopened.DescribeTransfer(recorder.Session.SessionId).Count);
        Assert.Equal(1,reopened.Verify(recorder.Session.SessionId));
    }

    [Fact]
    public void FutureSchemaIsRejectedInsteadOfSilentlyReadAsCurrentVersion()
    {
        var store = Store(); var session = Session(); store.Create(session);
        string path = Path.Combine(_root,session.SessionId,"capture_manifest.json");
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 99"));
        Assert.Throws<InvalidDataException>(() => store.Open(session.SessionId));
    }

    [Fact]
    public void StoreRejectsWritesAfterCompletionAndDuplicateTimestamps()
    {
        var store = Store(); var session = Session(); store.Create(session);
        store.SetStatus(session.SessionId,CaptureState.Recording,"recording"); store.Append(session.SessionId,Frame(),Pixels);
        Assert.Throws<ArgumentException>(() => store.Append(session.SessionId,Frame(),Pixels));
        store.SetStatus(session.SessionId,CaptureState.Completed,"operator_stop");
        Assert.Throws<InvalidOperationException>(() => store.Append(session.SessionId,Frame(2000000,100),Pixels));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
}
