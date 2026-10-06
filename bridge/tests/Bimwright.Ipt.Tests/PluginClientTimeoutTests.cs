using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Tests;

/// <summary>Deadline semantics of <see cref="PluginClient"/> against a stub pipe that stalls.</summary>
public sealed class PluginClientTimeoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-timeout-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipe = "BimwrightIptTimeout-" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();

    public PluginClientTimeoutTests()
    {
        Directory.CreateDirectory(_dir);
        var descriptor = new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, InventorYear = 2027, ProcessId = Environment.ProcessId,
            HostApp = "Inventor", Transport = "pipe", PipeName = _pipe, AuthToken = "t", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(Path.Combine(_dir, "target.json"), JsonConvert.SerializeObject(descriptor));
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Accepts connections, reads the request, answers after <paramref name="delayMs"/> (never when null).</summary>
    private void StartStub(int? delayMs)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { await server.WaitForConnectionAsync(_stop.Token); } catch { server.Dispose(); return; }
                _ = Task.Run(async () =>
                {
                    await using var stream = server;
                    try
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                        var line = await reader.ReadLineAsync(_stop.Token);
                        if (delayMs is null) { await Task.Delay(Timeout.Infinite, _stop.Token); return; }
                        await Task.Delay(delayMs.Value, _stop.Token);
                        var envelope = JsonConvert.DeserializeObject<InventorCommandEnvelope>(line!)!;
                        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                            InventorCommandResult.Success(envelope.Id, new Newtonsoft.Json.Linq.JObject { ["ok"] = true }, new InventorResponseMeta())) + "\n");
                        await stream.WriteAsync(bytes, _stop.Token);
                        await stream.FlushAsync(_stop.Token);
                    }
                    catch { }
                });
            }
        });
    }

    private PluginClient Client(int timeoutMs) => new(new InventorMcpConfig { DescriptorDirectory = _dir, TimeoutMs = timeoutMs });

    [Fact]
    public async Task Caller_cancellation_surfaces_as_cancellation_not_timeout()
    {
        StartStub(null);
        using var cts = new CancellationTokenSource();
        var pending = Client(30_000).SendAsync("get_document_info", new Newtonsoft.Json.Linq.JObject(), cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task A_stalled_add_in_times_out_promptly()
    {
        StartStub(null);
        var clock = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InventorGatewayException>(() => Client(300).SendAsync("get_document_info", new Newtonsoft.Json.Linq.JObject(), CancellationToken.None));
        Assert.Equal(InventorErrorCodes.TIMEOUT, ex.Code);
        Assert.True(clock.ElapsedMilliseconds < 3_300 + 3_000, "took " + clock.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public async Task An_answer_inside_the_grace_after_the_deadline_is_still_delivered()
    {
        StartStub(600);
        var result = await Client(300).SendAsync("get_document_info", new Newtonsoft.Json.Linq.JObject(), CancellationToken.None);
        Assert.True((bool)result["ok"]!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_timeout_is_replaced_by_the_default(int configured)
    {
        var config = InventorMcpConfig.Load(new[] { "--timeout-ms", configured.ToString() });
        Assert.True(config.TimeoutMs > 0);
        Assert.Equal(InventorMcpConfig.DefaultTimeoutMs, config.TimeoutMs);
    }
}
