using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Named-pipe NDJSON listener for Inventor 2025-2027 (.NET 8 / .NET 10 add-ins). Avoids the
/// loopback-firewall prompt UX on modern Windows. Ported from rvt-mcp's PipeTransportServer with
/// the descriptor-based auth model: both the pipe name and the expected token are supplied to the
/// constructor (they live in the TargetDescriptor written by the add-in). Validates each envelope's
/// <c>auth_token</c> field, bounds each line to 1 MiB, rate-limits to 20 req / 10 s per connection,
/// and waits up to 60 s for the STA dispatch to complete.
/// </summary>
public sealed class PipeTransportServer : ITransportServer
{
    private readonly string _pipeName;
    private readonly string _authToken;
    private Thread? _listenThread;
    private volatile bool _running;
    private Action<string, TaskCompletionSource<string>>? _onRequest;
    private volatile bool _clientConnected;
    private readonly TimeSpan _idleTimeout;
    private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
    private volatile NamedPipeServerStream? _activePipe;

    private const int MaxLineBytes = TransportLimits.MaxLineBytes;

    /// <param name="pipeName">The pipe name the add-in wrote into its TargetDescriptor.</param>
    /// <param name="authToken">
    /// The token the add-in wrote into its TargetDescriptor. Every incoming envelope's
    /// <c>auth_token</c> must match this value.
    /// </param>
    public PipeTransportServer(string pipeName, string authToken)
        : this(pipeName, authToken, TimeSpan.FromMilliseconds(TransportLimits.IdleReadTimeoutMs))
    {
    }

    /// <param name="idleReadTimeout">
    /// Longest a client may take to deliver one complete line (idle or partial line); the connection is then dropped.
    /// Injectable for tests.
    /// </param>
    internal PipeTransportServer(string pipeName, string authToken, TimeSpan idleReadTimeout)
    {
        if (idleReadTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleReadTimeout));
        _idleTimeout = idleReadTimeout;
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
    }

    public bool IsRunning => _running;
    public string PipeName => _pipeName;
    public bool IsClientConnected => _clientConnected;
    public DateTime? LastCommandTime { get; private set; }
    public string ConnectionInfo => $"Pipe:{_pipeName}";

    public void Start(Action<string, TaskCompletionSource<string>> onRequest)
    {
        _onRequest = onRequest ?? throw new ArgumentNullException(nameof(onRequest));
        _running = true;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "BimwrightInventor.PipeTransportServer" };
        _listenThread.Start();
    }

    public void Stop()
    {
        _running = false;
        // Cancel a pending WaitForConnectionAsync and drop an idle-connected client so the listener thread exits.
        try { _stopCts.Cancel(); } catch { }
        try { _activePipe?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Stop();
    }

    private void ListenLoop()
    {
        var backoff = new ListenBackoff();
        while (_running)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1, // maxNumberOfServerInstances
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                pipe.WaitForConnectionAsync(_stopCts.Token).Wait();
                if (!_running) break;

                backoff.Reset();
                HandleClient(pipe);
            }
            catch when (!_running)
            {
                break; // clean shutdown (Stop cancelled the wait or closed the pipe)
            }
            catch
            {
                // listen error; keep listening, but not in a tight loop
                backoff.Wait(() => _running);
            }
            finally
            {
                try { pipe?.Disconnect(); } catch { }
                try { pipe?.Dispose(); } catch { }
            }
        }
    }

    private void HandleClient(NamedPipeServerStream pipe)
    {
        _clientConnected = true;
        _activePipe = pipe;
        if (!_running) { _activePipe = null; _clientConnected = false; return; }
        try
        {
            var reader = new StreamReader(pipe, Encoding.UTF8);
            var lines = new NdjsonLineBuffer(reader);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };

            var requestTimestamps = new Queue<DateTime>();
            const int RateLimitMax = 20;
            var rateLimitWindow = TimeSpan.FromSeconds(10);

            while (_running && pipe.IsConnected)
            {
                string? line;
                try
                {
                    var readTask = lines.ReadLineAsync(MaxLineBytes);
                    if (!readTask.Wait(_idleTimeout))
                    {
                        // Idle or partial line: free the single pipe instance. Closing the pipe aborts the pending read.
                        try { pipe.Dispose(); } catch { }
                        _ = readTask.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        break;
                    }
                    var read = readTask.Result;
                    bool overflow = read.Overflow;
                    line = read.Line;
                    if (overflow)
                    {
                        TryWrite(writer, ErrorJson(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Request exceeded 1 MiB size limit."));
                        break;
                    }
                    if (line == null) break; // client disconnected
                }
                catch (IOException)
                {
                    break; // broken pipe
                }
                catch
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                JObject request;
                try { request = JObject.Parse(line); }
                catch
                {
                    if (!TryWrite(writer, ErrorJson(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "malformed request"))) break;
                    continue;
                }

                var id = ParseId(request);
                var token = request.Value<string>("auth_token");
                if (!AuthToken.Verify(_authToken, token))
                {
                    TryWrite(writer, ErrorJson(id, InventorErrorCodes.UNAUTHORIZED, "Invalid or missing authorization token."));
                    break; // drop the connection on auth failure
                }

                var now = DateTime.UtcNow;
                while (requestTimestamps.Count > 0 && (now - requestTimestamps.Peek()) > rateLimitWindow)
                    requestTimestamps.Dequeue();
                if (requestTimestamps.Count >= RateLimitMax)
                {
                    TryWrite(writer, ErrorJson(id, InventorErrorCodes.API_ERROR, "Rate limit: 20 requests / 10 seconds per connection."));
                    break;
                }
                requestTimestamps.Enqueue(now);

                LastCommandTime = DateTime.Now;
                var tcs = new TaskCompletionSource<string>();
                _onRequest!(line, tcs);

                string response;
                if (tcs.Task.Wait(TimeSpan.FromSeconds(60)))
                    response = tcs.Task.Result;
                else
                    response = ErrorJson(id, InventorErrorCodes.TIMEOUT, "Request timed out (60s). Inventor may be in a modal dialog or busy.");

                if (!TryWrite(writer, response)) break;
            }
        }
        catch
        {
            // client disconnect or IO error
        }
        finally
        {
            _activePipe = null;
            _clientConnected = false;
        }
    }

    private static Guid ParseId(JObject request)
        => Guid.TryParse(request.Value<string>("id"), out var g) ? g : Guid.Empty;

    private static bool TryWrite(StreamWriter writer, string text)
    {
        try { writer.WriteLine(text); return true; }
        catch { return false; }
    }

    private static string ErrorJson(Guid id, string code, string message)
        => JsonConvert.SerializeObject(InventorCommandResult.Fail(id, code, message, new InventorResponseMeta()));
}
