using System.Diagnostics;
using Inventor.So.RealityCapture;

namespace Inventor.So.Mcp.Http.Capture;

public sealed record CaptureWorkerSettings(string WorkerPath,TimeSpan Timeout,long MemoryLimitBytes = 1073741824,long OutputLimitBytes = 33554432)
{
    public static CaptureWorkerSettings Default()=>new(Environment.GetEnvironmentVariable("INVENTOR_SO_CAPTURE_WORKER") ??
        Path.Combine(AppContext.BaseDirectory,"capture-worker","Inventor.So.Capture.Worker.dll"),TimeSpan.FromMinutes(5));
    public bool Available=>Path.IsPathFullyQualified(WorkerPath) && File.Exists(WorkerPath) && Path.GetExtension(WorkerPath).ToLowerInvariant() is ".dll" or ".exe";
}

public interface ICaptureWorker
{
    bool Available {get;}
    Task RunAsync(string requestPath,string outputDirectory,CancellationToken ct);
}

public sealed class CaptureWorkerProcess(CaptureWorkerSettings settings) : ICaptureWorker
{
    public bool Available=>settings.Available;
    public async Task RunAsync(string requestPath,string outputDirectory,CancellationToken ct)
    {
        if(!Available) throw new CaptureSolveException("CAPTURE_WORKER_UNAVAILABLE");
        bool dll=Path.GetExtension(settings.WorkerPath).Equals(".dll",StringComparison.OrdinalIgnoreCase);
        var start=new ProcessStartInfo(dll ? DotnetHost() : settings.WorkerPath) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,
            WorkingDirectory=Path.GetDirectoryName(requestPath)!
        };
        if(dll) start.ArgumentList.Add(settings.WorkerPath);
        start.ArgumentList.Add(requestPath);
        start.Environment["DOTNET_GCHeapHardLimit"]=(settings.MemoryLimitBytes/2).ToString("X",System.Globalization.CultureInfo.InvariantCulture);
        using var process=new Process {StartInfo=start};
        try { process.Start(); } catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new CaptureSolveException("CAPTURE_WORKER_UNAVAILABLE"); }
        process.StandardInput.Close();
        var stdout=Drain(process.StandardOutput,true); var stderr=Drain(process.StandardError,false);
        using var timeout=new CancellationTokenSource(settings.Timeout);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,timeout.Token);
        try
        {
            var exit=process.WaitForExitAsync(linked.Token);
            while(!exit.IsCompleted)
            {
                await Task.WhenAny(exit,Task.Delay(200,linked.Token)); linked.Token.ThrowIfCancellationRequested();
                if(!process.HasExited)
                {
                    process.Refresh();
                    if(process.WorkingSet64>settings.MemoryLimitBytes) throw new CaptureSolveException("CAPTURE_WORKER_MEMORY_LIMIT");
                }
                if(Directory.Exists(outputDirectory) && Directory.EnumerateFiles(outputDirectory,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length)>settings.OutputLimitBytes)
                    throw new CaptureSolveException("CAPTURE_OUTPUT_QUOTA_EXCEEDED");
            }
            await exit; await Task.WhenAll(stdout,stderr).WaitAsync(linked.Token);
            if(process.ExitCode!=0)
            {
                string code=(await stdout).Trim();
                if(!System.Text.RegularExpressions.Regex.IsMatch(code,@"\ACAPTURE_[A-Z0-9_]{1,72}\z")) code="CAPTURE_WORKER_FAILED";
                throw new CaptureSolveException(code);
            }
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new CaptureSolveException("CAPTURE_WORKER_TIMEOUT"); }
        finally
        {
            try { if(!process.HasExited) process.Kill(entireProcessTree:true); } catch(InvalidOperationException) { }
            // Both pipes are drained without retaining image-related diagnostics or unbounded output in memory.
            try { await Task.WhenAll(stdout,stderr).WaitAsync(TimeSpan.FromSeconds(2)); } catch(Exception) { }
        }
    }
    private static async Task<string> Drain(StreamReader reader,bool capture)
    {
        var text=new System.Text.StringBuilder(); var buffer=new char[4096]; int n;
        while((n=await reader.ReadAsync(buffer.AsMemory()))>0) if(capture && text.Length<256) text.Append(buffer,0,Math.Min(n,256-text.Length));
        return text.ToString();
    }
    private static string DotnetHost()
    {
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase)) return Environment.ProcessPath!;
        return OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe") : "dotnet";
    }
}
