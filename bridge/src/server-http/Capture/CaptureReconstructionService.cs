using Inventor.So.RealityCapture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Capture;

public sealed partial class CaptureReconstructionService : IHostedService,IAsyncDisposable
{
    private readonly CaptureTransferStore _store;
    private readonly ICaptureWorker _worker;
    private readonly CaptureWorkerSettings _settings;
    private readonly object _gate=new();
    private readonly Dictionary<string,(CancellationTokenSource cancel,Task task)> _jobs=new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,CaptureSolveSummary> _statuses=new(StringComparer.Ordinal);
    private bool _busy,_stopping;
    private FileStream? _rootLease;
    private sealed record ConstraintSnapshot(int Version,MetricConstraint[] Constraints,CalibrationArtifact[] Artifacts);
    public static readonly string[] ArtifactNames={"pointcloud.ply","scan.obj","scan.glb","points.json","quality_report.json","constraints.json","optimized_cameras.json","units.json"};

    public CaptureReconstructionService(CaptureTransferStore store,ICaptureWorker worker,CaptureWorkerSettings settings)
    { _store=store; _worker=worker; _settings=settings; }

    public JObject GetConstraints(string owner,string id)
    {
        using var lease=_store.AcquireFinalized(owner,id);
        lock(_gate) { var c=ReadConstraints(lease.Directory); return JObject.FromObject(new {ok=true,version=c.Version,constraints=c.Constraints,artifacts=c.Artifacts}); }
    }
    public JObject SetConstraint(string owner,string id,MetricConstraint? constraint,string? removeId,int expectedVersion)
    {
        constraint?.Validate();
        if(removeId!=null && !MetricConstraint.ValidName(removeId)) throw new CaptureSolveException("CAPTURE_CONSTRAINT_INVALID");
        using var lease=_store.AcquireFinalized(owner,id);
        lock(_gate)
        {
            var snapshot=ReadConstraints(lease.Directory);
            if(snapshot.Version!=expectedVersion) throw new CaptureTransferException(409,"CAPTURE_CONSTRAINT_VERSION_CHANGED");
            var list=snapshot.Constraints.Where(c=>c.Id!=(constraint?.Id ?? removeId)).ToList();
            if(constraint!=null) list.Add(constraint);
            if(list.Count>256) throw new CaptureTransferException(413,"CAPTURE_CONSTRAINT_QUOTA_EXCEEDED");
            WriteConstraintSnapshot(lease.Directory,new(snapshot.Version+1,list.ToArray(),snapshot.Artifacts));
            return JObject.FromObject(new {ok=true,version=snapshot.Version+1,constraints=list});
        }
    }
    public JObject SetArtifact(string owner,string id,CalibrationArtifact artifact,int expectedVersion)
    {
        artifact.Validate(); using var lease=_store.AcquireFinalized(owner,id);
        lock(_gate)
        {
            var snapshot=ReadConstraints(lease.Directory);
            if(snapshot.Version!=expectedVersion) throw new CaptureTransferException(409,"CAPTURE_CONSTRAINT_VERSION_CHANGED");
            var artifacts=snapshot.Artifacts.Where(a=>a.Id!=artifact.Id).Append(artifact).ToArray();
            if(artifacts.Length>16 || artifacts.SelectMany(a=>a.Markers).Select(m=>m.MarkerId).Distinct().Count()!=artifacts.Sum(a=>a.Markers.Length))
                throw new CaptureSolveException("CAPTURE_MARKER_ID_CONFLICT");
            WriteConstraintSnapshot(lease.Directory,new(snapshot.Version+1,snapshot.Constraints,artifacts));
            return JObject.FromObject(new {ok=true,version=snapshot.Version+1,artifacts});
        }
    }

    public JObject StartSolve(string owner,string id,int expectedVersion)
    {
        if(!_worker.Available) throw new CaptureTransferException(503,"CAPTURE_WORKER_UNAVAILABLE");
        var lease=_store.AcquireFinalized(owner,id);
        try
        {
            lock(_gate)
            {
                if(_busy || _stopping) throw new CaptureTransferException(409,"CAPTURE_SOLVE_BUSY");
                var snapshot=ReadConstraints(lease.Directory);
                if(snapshot.Version!=expectedVersion) throw new CaptureTransferException(409,"CAPTURE_CONSTRAINT_VERSION_CHANGED");
                string revisions=Path.Combine(lease.Directory,"solves"); Directory.CreateDirectory(revisions);
                var previous=RevisionDirectories(revisions);
                if(previous.Length>=4) throw new CaptureTransferException(413,"CAPTURE_REVISION_QUOTA_EXCEEDED");
                var sessions=_store.Sessions();
                long Used(string root)=>Directory.Exists(Path.Combine(root,"solves")) ? Directory.EnumerateFiles(Path.Combine(root,"solves"),"*",SearchOption.AllDirectories)
                    .Where(p=>p.Contains(Path.DirectorySeparatorChar+"output"+Path.DirectorySeparatorChar,StringComparison.Ordinal)).Sum(p=>new FileInfo(p).Length) : 0;
                if(sessions.Where(s=>s.Owner==owner).Sum(s=>Used(s.Directory))+_settings.OutputLimitBytes>256L*1024*1024 ||
                    sessions.Sum(s=>Used(s.Directory))+_settings.OutputLimitBytes>1024L*1024*1024) throw new CaptureTransferException(413,"CAPTURE_OUTPUT_QUOTA_EXCEEDED");
                int revision=previous.Length+1; string directory=Path.Combine(revisions,"r"+revision.ToString("D6")); Directory.CreateDirectory(directory);
                var input=new CaptureSolveInput(id,revision,Path.Combine(directory,"input"),Path.Combine(directory,"output"),snapshot.Constraints,snapshot.Artifacts);
                CaptureJson.WriteNew(Path.Combine(directory,"request.json"),input);
                var status=new CaptureSolveSummary(id,revision,"queued",null,DateTimeOffset.UtcNow,null,snapshot.Version); WriteStatus(directory,status);
                var cancel=new CancellationTokenSource(); _busy=true;
                string key=owner+"/"+id+"/"+revision;
                var task=Task.Run(()=>RunAsync(lease,directory,input,status,key,cancel.Token));
                _jobs.Add(key,(cancel,task));
                return JObject.FromObject(new {ok=true,solution=status});
            }
        }
        catch {lease.Dispose();throw;}
    }

    private async Task RunAsync(CaptureTransferStore.SnapshotLease lease,string directory,CaptureSolveInput input,CaptureSolveSummary status,string key,CancellationToken ct)
    {
        try
        {
            WriteStatus(directory,status with {State="running"});
            await lease.CopyToAsync(input.InputDirectory,ct);
            await _worker.RunAsync(Path.Combine(directory,"request.json"),input.OutputDirectory,ct);
            ct.ThrowIfCancellationRequested(); ValidateOutput(input);
            Directory.Delete(input.InputDirectory,true);
            WriteStatus(directory,status with {State="succeeded",FinishedAt=DateTimeOffset.UtcNow});
        }
        catch(OperationCanceledException) {TryWriteStatus(directory,status with {State="cancelled",ErrorCode="CAPTURE_SOLVE_CANCELLED",FinishedAt=DateTimeOffset.UtcNow});}
        catch(CaptureSolveException ex) {TryWriteStatus(directory,status with {State="failed",ErrorCode=ex.Code,FinishedAt=DateTimeOffset.UtcNow});}
        catch(Exception) {TryWriteStatus(directory,status with {State="failed",ErrorCode="CAPTURE_SOLVE_FAILED",FinishedAt=DateTimeOffset.UtcNow});}
        finally
        {
            try { if(Directory.Exists(input.InputDirectory)) Directory.Delete(input.InputDirectory,true); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            { TryWriteStatus(directory,status with {State="failed",ErrorCode="CAPTURE_CLEANUP_FAILED",FinishedAt=DateTimeOffset.UtcNow}); }
            finally
            {
                lease.Dispose();
                lock(_gate) { _busy=false; if(_jobs.Remove(key,out var job)) job.cancel.Dispose(); }
            }
        }
    }

    public JObject GetSolution(string owner,string id,int revision)
    {
        using var lease=_store.AcquireFinalized(owner,id);
        string directory=FindRevision(lease.Directory,revision); var status=ReadStatus(directory);
        var files=new List<object>();
        if(status.State=="succeeded") foreach(string name in ArtifactNames)
        {
            string path=Path.Combine(directory,"output",name);
            using var file=File.OpenRead(path);
            files.Add(new {name,size=file.Length,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant(),
                url=$"/capture/v1/sessions/{id}/solutions/{revision}/assets/{name}"});
        }
        return JObject.FromObject(new {ok=true,solution=status,assets=files});
    }
    public JObject GetQuality(string owner,string id,int revision)
    {
        using var lease=_store.AcquireFinalized(owner,id); string directory=FindRevision(lease.Directory,revision);
        var status=ReadStatus(directory);
        if(status.State!="succeeded") throw new CaptureTransferException(409,"CAPTURE_SOLUTION_NOT_READY");
        return new JObject { ["ok"]=true,["quality"]=CaptureJson.Read<JObject>(Path.Combine(directory,"output","quality_report.json"),1024*1024) };
    }
    public byte[] ReadAsset(string owner,string id,int revision,string asset)
    {
        if(!ArtifactNames.Contains(asset,StringComparer.Ordinal)) throw new CaptureTransferException(404,"CAPTURE_ASSET_NOT_FOUND");
        using var lease=_store.AcquireFinalized(owner,id); string directory=FindRevision(lease.Directory,revision);
        if(ReadStatus(directory).State!="succeeded") throw new CaptureTransferException(409,"CAPTURE_SOLUTION_NOT_READY");
        string path=Path.Combine(directory,"output",asset);
        if(new FileInfo(path).Length>_settings.OutputLimitBytes) throw new CaptureTransferException(413,"CAPTURE_OUTPUT_QUOTA_EXCEEDED");
        return File.ReadAllBytes(path);
    }
    public JObject Cancel(string owner,string id,int revision)
    {
        using var lease=_store.AcquireFinalized(owner,id); FindRevision(lease.Directory,revision);
        lock(_gate) {if(_jobs.TryGetValue(owner+"/"+id+"/"+revision,out var job)) job.cancel.Cancel();}
        return new JObject { ["ok"]=true };
    }
    private void ValidateOutput(CaptureSolveInput input)
    {
        var marker=CaptureJson.Read<JObject>(Path.Combine(input.OutputDirectory,"result.json"),65536);
        if(marker.Value<bool?>("ok")!=true || marker.Value<string>("scan_id")!=input.ScanId || marker.Value<int?>("revision")!=input.Revision ||
            marker["artifacts"] is not JArray names || !names.Values<string>().SequenceEqual(ArtifactNames)) throw new CaptureSolveException("CAPTURE_RESULT_INVALID");
        var files=Directory.EnumerateFiles(input.OutputDirectory,"*",SearchOption.AllDirectories).ToArray();
        if(files.Length!=ArtifactNames.Length+1 || files.Sum(p=>new FileInfo(p).Length)>_settings.OutputLimitBytes ||
            files.Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0 || Path.GetDirectoryName(p)!=input.OutputDirectory)) throw new CaptureSolveException("CAPTURE_RESULT_INVALID");
        foreach(string name in ArtifactNames) if(!File.Exists(Path.Combine(input.OutputDirectory,name))) throw new CaptureSolveException("CAPTURE_RESULT_INVALID");
    }
    private static ConstraintSnapshot ReadConstraints(string directory)
    {
        string root=Path.Combine(directory,"constraints");
        string? latest=Directory.Exists(root) ? Directory.EnumerateFiles(root,"v*.json").OrderBy(p=>p,StringComparer.Ordinal).LastOrDefault() : null;
        return latest==null ? new(0,Array.Empty<MetricConstraint>(),Array.Empty<CalibrationArtifact>()) : CaptureJson.Read<ConstraintSnapshot>(latest,1024*1024);
    }
    private static void WriteConstraintSnapshot(string directory,ConstraintSnapshot snapshot)
    {
        if(snapshot.Version>256) throw new CaptureTransferException(413,"CAPTURE_CONSTRAINT_REVISION_QUOTA_EXCEEDED");
        string root=Path.Combine(directory,"constraints"); Directory.CreateDirectory(root);
        long bytes=System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(snapshot,Formatting.Indented,CaptureJson.Settings));
        if(bytes>1024*1024 || Directory.EnumerateFiles(root,"*.json").Sum(p=>new FileInfo(p).Length)+bytes>4L*1024*1024)
            throw new CaptureTransferException(413,"CAPTURE_CONSTRAINT_QUOTA_EXCEEDED");
        CaptureJson.WriteNew(Path.Combine(root,"v"+snapshot.Version.ToString("D6")+".json"),snapshot);
    }
    private static string[] RevisionDirectories(string root)=>Directory.EnumerateDirectories(root).Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"\Ar[0-9]{6}\z")).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
    private static string FindRevision(string root,int revision)
    {
        if(revision is <1 or >4) throw new CaptureTransferException(404,"CAPTURE_SOLUTION_NOT_FOUND");
        string directory=Path.Combine(root,"solves","r"+revision.ToString("D6"));
        if(!File.Exists(Path.Combine(directory,"status.json"))) throw new CaptureTransferException(404,"CAPTURE_SOLUTION_NOT_FOUND");
        return directory;
    }
    private CaptureSolveSummary ReadStatus(string directory)=>_statuses.TryGetValue(directory,out var status)?status:
        CaptureJson.Read<CaptureSolveSummary>(Path.Combine(directory,"status.json"),65536);
    private void WriteStatus(string directory,CaptureSolveSummary status)
    {
        string pending=Path.Combine(directory,"status-"+Guid.NewGuid().ToString("N")+".pending"); CaptureJson.WriteNew(pending,status);
        File.Move(pending,Path.Combine(directory,"status.json"),true);
        _statuses[directory]=status;
    }
    private void TryWriteStatus(string directory,CaptureSolveSummary status)
    {try {WriteStatus(directory,status);} catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {_statuses[directory]=status with {State="failed",ErrorCode="CAPTURE_STORAGE_ERROR"};
         Console.Error.WriteLine("Capture status persistence failed: "+ex.GetType().Name+" "+ex.HResult.ToString("X"));}}
    public Task StartAsync(CancellationToken ct)
    {
        // Recovery may change persisted job states: no second host may recover or prune the same root concurrently.
        _rootLease=new FileStream(Path.Combine(_store.Root,".host.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        _store.PruneExpired();
        foreach(var session in _store.Sessions())
        {
            string root=Path.Combine(session.Directory,"solves"); if(!Directory.Exists(root)) continue;
            foreach(string revision in RevisionDirectories(root))
            {
                string path=Path.Combine(revision,"status.json"); if(!File.Exists(path)) continue;
                var status=CaptureJson.Read<CaptureSolveSummary>(path,65536);
                if(status.State is "running" or "queued") WriteStatus(revision,status with {State="failed",ErrorCode="CAPTURE_HOST_RESTARTED",FinishedAt=DateTimeOffset.UtcNow});
                string input=Path.Combine(revision,"input"); if(Directory.Exists(input)) Directory.Delete(input,true);
            }
        }
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken ct)
    {
        Task[] tasks; lock(_gate) {_stopping=true;foreach(var job in _jobs.Values) job.cancel.Cancel();tasks=_jobs.Values.Select(j=>j.task).ToArray();}
        await Task.WhenAll(tasks).WaitAsync(ct);
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        foreach(var job in _jobs.Values) job.cancel.Dispose();
        Interlocked.Exchange(ref _rootLease,null)?.Dispose();
    }
}
