using Inventor.So.Capture.Worker;
using Inventor.So.Mcp.Http.Capture;
using Inventor.So.RealityCapture;
using InventorXrSo.Core.Capture;
using Newtonsoft.Json.Linq;

namespace Capture.Worker.Tests;

public sealed class ReconstructionLifecycleTests
{
    private static async Task<(CaptureTransferStore store,string owner,string id)> Upload(SyntheticReconstructionTests fixture)
    {
        var input=fixture.MakeInput();
        string root=Path.GetDirectoryName(Path.GetDirectoryName(input.InputDirectory)!)!;
        var local=new LocalScanStore(Path.GetDirectoryName(input.InputDirectory)!,20000000,0,()=>long.MaxValue);
        var store=new CaptureTransferStore(Path.Combine(root,"uploaded")); string owner=CaptureTransferStore.TokenOwner("synthetic-owner");
        var files=local.DescribeTransfer(input.ScanId);
        store.Create(owner,new CaptureUpload(input.ScanId,files.Select(f=>new CaptureFile(f.Path,f.Size,f.Sha256)).ToArray()));
        for(int i=0;i<files.Count;i++)
        {
            byte[] bytes=local.ReadTransferFile(input.ScanId,files[i]);
            await store.PutAsync(owner,input.ScanId,i,files[i].Sha256,bytes.Length,new MemoryStream(bytes),CancellationToken.None);
        }
        store.Finalize(owner,input.ScanId); return(store,owner,input.ScanId);
    }
    [Fact]
    public async Task CancellationKeepsRawBlocksDeletionAndReleasesWorkerSlot()
    {
        using var fixture=new SyntheticReconstructionTests(); var (store,owner,id)=await Upload(fixture);
        var worker=new BlockingWorker(); var settings=new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(10));
        await using var service=new CaptureReconstructionService(store,worker,settings);
        service.StartSolve(owner,id,0); await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("CAPTURE_SOLVE_BUSY",Assert.Throws<CaptureTransferException>(()=>service.StartSolve(owner,id,0)).Code);
        Assert.Equal("CAPTURE_CHUNK_BUSY",Assert.Throws<CaptureTransferException>(()=>store.Delete(owner,id)).Code);
        Assert.Equal("CAPTURE_NOT_FOUND",Assert.Throws<CaptureTransferException>(()=>service.Cancel(CaptureTransferStore.TokenOwner("foreign"),id,1)).Code);
        service.Cancel(owner,id,1); await Wait(service,owner,id,1,"cancelled");
        Assert.True(worker.Cancelled);
        Assert.Throws<CaptureTransferException>(()=>service.ReadAsset(owner,id,1,"scan.glb"));
        Assert.False((bool)JObject.FromObject(store.Describe(owner,id))["reconstruction_available"]!);
        await service.StopAsync(CancellationToken.None);
        store.Delete(owner,id); // lease was released after cleanup
    }
    [Fact]
    public async Task CrashedOrMalformedWorkerNeverPublishesPartialResults()
    {
        using var fixture=new SyntheticReconstructionTests(); var (store,owner,id)=await Upload(fixture);
        await using var service=new CaptureReconstructionService(store,new BrokenWorker(),new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(10)));
        service.StartSolve(owner,id,0); var state=await Wait(service,owner,id,1,"failed");
        Assert.Equal("CAPTURE_SOLVE_FAILED",(string?)state["solution"]?["ErrorCode"]);
        Assert.Empty(state["assets"]!);
        Assert.Throws<CaptureTransferException>(()=>service.ReadAsset(owner,id,1,"scan.glb"));
        Assert.Throws<CaptureTransferException>(()=>service.ReadAsset(owner,id,1,"../../upload.json"));
        Assert.True((bool)JObject.FromObject(store.Describe(owner,id))["finalized"]!);
    }
    [Fact]
    public async Task RealProcessDeadlineKillsWorkerAndPreservesRaw()
    {
        using var fixture=new SyntheticReconstructionTests(); var (store,owner,id)=await Upload(fixture);
        var settings=new CaptureWorkerSettings(typeof(Program).Assembly.Location,TimeSpan.FromMilliseconds(1));
        await using var service=new CaptureReconstructionService(store,new CaptureWorkerProcess(settings),settings);
        service.StartSolve(owner,id,0); var state=await Wait(service,owner,id,1,"failed");
        Assert.Equal("CAPTURE_WORKER_TIMEOUT",(string?)state["solution"]?["ErrorCode"]);
        var disk=CaptureJson.Read<CaptureSolveSummary>(Path.Combine(fixture.Root,"uploaded",owner,id,"solves","r000001","status.json"));
        Assert.Equal("failed",disk.State); Assert.Equal("CAPTURE_WORKER_TIMEOUT",disk.ErrorCode);
        Assert.True((bool)JObject.FromObject(store.Describe(owner,id))["finalized"]!);
    }
    [Fact]
    public async Task ASecondHostCannotRecoverAnAlreadyOwnedCaptureRoot()
    {
        using var fixture=new SyntheticReconstructionTests();var (store,owner,id)=await Upload(fixture);
        await using var first=new CaptureReconstructionService(store,new BrokenWorker(),new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(10)));
        await first.StartAsync(CancellationToken.None);
        var otherStore=new CaptureTransferStore(Path.Combine(fixture.Root,"uploaded"));
        await using var second=new CaptureReconstructionService(otherStore,new BrokenWorker(),new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<IOException>(()=>second.StartAsync(CancellationToken.None));
        Assert.True((bool)JObject.FromObject(store.Describe(owner,id))["finalized"]!);
    }
    [Fact]
    public async Task RestartMarksQueuedRevisionFailedAndKeepsConstraintVersion()
    {
        using var fixture=new SyntheticReconstructionTests(); var (store,owner,id)=await Upload(fixture);
        string path;
        using(var lease=store.AcquireFinalized(owner,id))
        {
            // Acquire a genuine generated request through a controlled worker, then model a host-crash status after it drains.
            var worker=new BlockingWorker();
            await using var first=new CaptureReconstructionService(store,worker,new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(10)));
            first.StartSolve(owner,id,0); await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            path=Path.GetDirectoryName(worker.RequestPath)!;
            first.Cancel(owner,id,1); await first.StopAsync(CancellationToken.None);
            File.Delete(Path.Combine(path,"status.json"));
            CaptureJson.WriteNew(Path.Combine(path,"status.json"),new CaptureSolveSummary(id,1,"running",null,DateTimeOffset.UtcNow,null,0));
        }
        await using var restarted=new CaptureReconstructionService(store,new BrokenWorker(),new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(10)));
        await restarted.StartAsync(CancellationToken.None);
        Assert.Equal("CAPTURE_HOST_RESTARTED",(string?)restarted.GetSolution(owner,id,1)["solution"]?["ErrorCode"]);
        Assert.Equal(0,(int)restarted.GetConstraints(owner,id)["version"]!);
    }
    private static async Task<JObject> Wait(CaptureReconstructionService service,string owner,string id,int revision,string expected)
    {
        var deadline=DateTimeOffset.UtcNow.AddSeconds(30);
        while(DateTimeOffset.UtcNow<deadline)
        {
            var status=service.GetSolution(owner,id,revision);
            if(status["solution"]?.Value<string>("State") is not ("queued" or "running")) {Assert.Equal(expected,(string?)status["solution"]?["State"]);return status;}
            await Task.Delay(20);
        }
        throw new TimeoutException("Synthetic solve did not terminate: "+service.GetSolution(owner,id,revision));
    }
    private sealed class BlockingWorker : ICaptureWorker
    {
        public bool Available=>true;
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled;
        public string RequestPath="";
        public async Task RunAsync(string requestPath,string outputDirectory,CancellationToken ct)
        {RequestPath=requestPath;Started.TrySetResult();try {await Task.Delay(Timeout.Infinite,ct);} catch(OperationCanceledException) {Cancelled=true;throw;}}
    }
    private sealed class BrokenWorker : ICaptureWorker
    {
        public bool Available=>true;
        public Task RunAsync(string requestPath,string outputDirectory,CancellationToken ct)
        {Directory.CreateDirectory(outputDirectory);File.WriteAllText(Path.Combine(outputDirectory,"scan.glb"),"incomplete");throw new IOException("synthetic worker failure with sensitive local data");}
    }
}
