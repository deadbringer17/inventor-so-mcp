using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;

namespace Inventor.So.Pairing.Control;

public sealed record ControlRequest(int Version = 1, string Operation = "status", string? Host = null,
    string? WindowId = null, string? TargetId = null);
public sealed record NetworkAddress(string Address, string Adapter)
{
    public override string ToString() => $"{Address} — {Adapter}";
}
public sealed record InventorTarget(string Id, string? Document)
{
    public override string ToString() => $"{Id} — {Document ?? "nessun documento"}";
}
public sealed record ControlResponse
{
    public int Version { get; init; } = 1;
    public bool Ok { get; init; } = true;
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public int ServerPid { get; init; }
    public string? TargetRule { get; init; }
    public string? TargetId { get; init; }
    public string? Document { get; init; }
    public InventorTarget[] Targets { get; init; } = [];
    public NetworkAddress[] Addresses { get; init; } = [];
    public int Port { get; init; }
    public string? CertSha256 { get; init; }
    public string State { get; init; } = "ready";
    public string? WindowId { get; init; }
    public DateTimeOffset? ExpiresUtc { get; init; }
    public double RemainingSeconds { get; init; }
    public string? ClientName { get; init; }
    // Secrets appear only in an explicit open response, never status.
    public string? Code { get; init; }
    public string? QrPayload { get; init; }
    public static ControlResponse Error(string code, string message) => new() { Ok = false, ErrorCode = code, Message = message };
}

public static class ControlNames
{
    public static string UserSuffix { get; } = ComputeSuffix();
    private static string ComputeSuffix()
    {
        string identity;
        if (OperatingSystem.IsWindows())
        {
            using var current = WindowsIdentity.GetCurrent();
            identity = current.User!.Value;
        }
        else identity = Environment.UserName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }
    public static string HostPipe => "InventorSO.Pairing.Host." + UserSuffix;
    public static string DesktopPipe => "InventorSO.Pairing.Desktop." + UserSuffix;
    public static string DesktopMutex => "Local\\InventorSO.Pairing.Desktop." + UserSuffix;
}

public static class ControlWire
{
    public const int MaxFrameBytes = 32768;
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var next = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(next, ct) == 0) throw new IOException("Incomplete control frame.");
            if (next[0] == '\n') break;
            if (buffer.Length >= MaxFrameBytes) throw new InvalidDataException("Control frame too large.");
            buffer.WriteByte(next[0]);
        }
        return JsonSerializer.Deserialize<T>(buffer.ToArray()) ?? throw new InvalidDataException("Empty control frame.");
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaxFrameBytes) throw new InvalidDataException("Control frame too large.");
        await stream.WriteAsync(bytes, ct);
        await stream.WriteAsync(new byte[] { (byte)'\n' }, ct);
        await stream.FlushAsync(ct);
    }
    public static async Task<ControlResponse> CallAsync(string pipeName, ControlRequest request, CancellationToken ct,
        int timeoutMs = 3000)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeoutMs);
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        await WriteAsync(pipe, request, deadline.Token);
        var response = await ReadAsync<ControlResponse>(pipe, deadline.Token);
        if (response.Version != 1) throw new InvalidDataException("Unsupported control protocol.");
        return response;
    }
}

/// <summary>One bounded request per connection; only the same OS user may connect.</summary>
public sealed class ControlPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public ControlPipeServer(string name, Func<ControlRequest, ControlResponse> handle)
    {
        // Reserve synchronously so duplicate hosts fail before claiming readiness.
        var first = Create(name);
        _loop = RunAsync(name, handle, first);
    }
    private static NamedPipeServerStream Create(string name)
    {
        if (!OperatingSystem.IsWindows()) return new(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var current = WindowsIdentity.GetCurrent();
        var owner = current.User!;
        var acl = new PipeSecurity();
        acl.SetOwner(owner);
        acl.SetAccessRuleProtection(true, false);
        // Also deny network logons, even if they authenticate as this same Windows user over SMB.
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, acl);
    }
    private async Task RunAsync(string name, Func<ControlRequest, ControlResponse> handle, NamedPipeServerStream first)
    {
        var pipe = first;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using (pipe)
                {
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    deadline.CancelAfter(3000);
                    try
                    {
                        var request = await ControlWire.ReadAsync<ControlRequest>(pipe, deadline.Token).ConfigureAwait(false);
                        var response = request.Version == 1 ? handle(request) : ControlResponse.Error("PROTOCOL_VERSION", "Versione non supportata.");
                        await ControlWire.WriteAsync(pipe, response, deadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or JsonException)
                    { /* Disconnected, malformed or stalled client; never log frame contents. */ }
                }
                if (!_stop.IsCancellationRequested) pipe = Create(name);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { pipe.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _loop;
        _stop.Dispose();
    }
}
