using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.Mcp.Http.Voice;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class FakeSpeechEngine : ISpeechEngine
{
    public string Name => "fake";
    public bool IsEnabled => true;
    public Func<byte[], Task<string>> Handler { get; set; } = _ => Task.FromResult("ciao mondo");
    public byte[]? LastAudio;
    public int LastRate;
    public ReadOnlyMemory<byte> LastBuffer;
    public async Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16, int sampleRate, CancellationToken ct)
    {
        LastAudio = pcm16.ToArray(); LastRate = sampleRate; LastBuffer = pcm16;
        return await Handler(LastAudio);
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
{
    public readonly ConcurrentQueue<string> Lines = new();
    public ILogger CreateLogger(string categoryName) => this;
    public void Dispose() { }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Enqueue(formatter(state, exception) + exception);
}

/// <summary>L2: POST /voice/transcribe on the real remote host with a fake engine.</summary>
public sealed class VoiceEndpointTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-voice-" + Guid.NewGuid().ToString("N"));
    private readonly string _token = TokenRegistry.Generate();
    private readonly FakeSpeechEngine _fake = new();
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplication? _app;
    private string _base = "";

    private async Task Start(bool withEngine = true, ISpeechEngine? engine = null, bool diagnostics = false, string? voiceCommand = null)
    {
        var config = new InventorMcpConfig
        {
            DescriptorDirectory = Path.Combine(_root, "targets"),
            AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"),
            HttpRateLimitPerMinute = 1000,
            VoiceDiagnostics = diagnostics,
            VoiceCommand = voiceCommand,
        };
        config.HttpUrls.Clear();
        config.HttpUrls.Add("http://127.0.0.1:0");
        var tokens = new TokenRegistry();
        tokens.Add("quest", _token);
        var options = new HostOptions
        {
            ConfigureServices = s =>
            {
                s.AddLogging(l => l.AddProvider(_logs));
                if (withEngine) s.AddSingleton<ISpeechEngine>(engine ?? _fake);
            },
        };
        _app = HttpHost.Build(Array.Empty<string>(), config, tokens, options);
        await _app.StartAsync();
        _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        try { Directory.Delete(_root, true); } catch { }
    }

    private HttpClient Client(string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(_base) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static ByteArrayContent Audio(byte[] bytes, string type = "audio/L16;rate=16000")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", type);
        return content;
    }

    private static byte[] Pcm(int bytes) => Enumerable.Range(0, bytes).Select(i => (byte)(i % 251 + 1)).ToArray();

    private static async Task<(HttpStatusCode status, JObject body)> Read(HttpResponseMessage r) =>
        (r.StatusCode, JObject.Parse(await r.Content.ReadAsStringAsync()));

    [Fact]
    public async Task RequiresAValidToken()
    {
        await Start();
        foreach (var token in new string?[] { null, "wrong-token-but-long-enough-to-try-0000" })
        {
            using var client = Client(token);
            var response = await client.PostAsync("/voice/transcribe", Audio(Pcm(3200)));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Null(_fake.LastAudio);
    }

    [Fact]
    public async Task IsDisabledByDefault()
    {
        await Start(withEngine: false);
        using var client = Client(_token);
        var (status, body) = await Read(await client.PostAsync("/voice/transcribe", Audio(Pcm(3200))));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("VOICE_DISABLED", (string?)body["error"]!["code"]);
        Assert.Equal("VOICE_DISABLED", (string?)body["code"]);
    }

    [Fact]
    public async Task ConfiguredCommandSelectsTheExternalEngineOtherwiseDisabled()
    {
        Assert.IsType<DisabledSpeechEngine>(ExternalProcessSpeechEngine.FromConfig(new InventorMcpConfig()));
        Assert.IsType<ExternalProcessSpeechEngine>(ExternalProcessSpeechEngine.FromConfig(new InventorMcpConfig { VoiceCommand = "python serve_once.py" }));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TranscribesAndReportsEngineAndTimeWithoutInterpretingText()
    {
        await Start();
        var pcm = Pcm(32000);
        using var client = Client(_token);
        _fake.Handler = _ => Task.FromResult("annulla e applica");
        var (status, body) = await Read(await client.PostAsync("/voice/transcribe", Audio(pcm)));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("annulla e applica", (string?)body["transcript"]);
        Assert.Equal("fake", (string?)body["engine"]);
        Assert.True((long)body["elapsed_ms"]! >= 0);
        Assert.Equal(pcm, _fake.LastAudio);
        Assert.Equal(16000, _fake.LastRate);
    }

    [Fact]
    public async Task OctetStreamNeedsTheSampleRateHeader()
    {
        await Start();
        using var client = Client(_token);
        var without = await client.PostAsync("/voice/transcribe", Audio(Pcm(3200), "application/octet-stream"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, without.StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, "/voice/transcribe") { Content = Audio(Pcm(3200), "application/octet-stream") };
        request.Headers.Add("X-Audio-Sample-Rate", "16000");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("audio/L16;rate=44100")]
    [InlineData("audio/L16;rate=16000;channels=2")]
    [InlineData("audio/wav")]
    [InlineData("text/plain")]
    public async Task WrongFormatIsRefused(string type)
    {
        await Start();
        using var client = Client(_token);
        var (status, body) = await Read(await client.PostAsync("/voice/transcribe", Audio(Pcm(3200), type)));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Equal("VOICE_UNSUPPORTED_FORMAT", (string?)body["code"]);
        Assert.Null(_fake.LastAudio);
    }

    [Fact]
    public async Task EmptyAndOddBodiesAreBadRequests()
    {
        await Start();
        using var client = Client(_token);
        var (s1, b1) = await Read(await client.PostAsync("/voice/transcribe", Audio(Array.Empty<byte>())));
        Assert.Equal(HttpStatusCode.BadRequest, s1);
        Assert.Equal("VOICE_EMPTY", (string?)b1["code"]);
        var (s2, b2) = await Read(await client.PostAsync("/voice/transcribe", Audio(Pcm(3201))));
        Assert.Equal(HttpStatusCode.BadRequest, s2);
        Assert.Equal("VOICE_BAD_AUDIO", (string?)b2["code"]);
    }

    [Fact]
    public async Task TenSecondsPassAndMoreIsRefusedWith413()
    {
        await Start();
        using var client = Client(_token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/voice/transcribe", Audio(Pcm(VoiceEndpoint.MaxBytes)))).StatusCode);
        var (status, body) = await Read(await client.PostAsync("/voice/transcribe", Audio(Pcm(VoiceEndpoint.MaxBytes + 2))));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
        Assert.Equal("VOICE_TOO_LARGE", (string?)body["code"]);
        Assert.Equal(VoiceEndpoint.MaxBytes, _fake.LastAudio!.Length);   // the second body never reached the engine
    }

    [Fact]
    public async Task ChunkedOversizeBodyIsRefusedToo()
    {
        await Start();
        using var client = Client(_token);
        var content = new StreamContent(new MemoryStream(Pcm(VoiceEndpoint.MaxBytes * 3)));   // no Content-Length: chunked
        content.Headers.TryAddWithoutValidation("Content-Type", "audio/L16;rate=16000");
        var response = await client.PostAsync("/voice/transcribe", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Null(_fake.LastAudio);
    }

    [Theory]
    [InlineData(SpeechEngineException.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData(SpeechEngineException.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(SpeechEngineException.Failed, HttpStatusCode.BadGateway)]
    public async Task EngineFailuresBecomeCodedErrors(string code, HttpStatusCode expected)
    {
        await Start();
        _fake.Handler = _ => throw new SpeechEngineException(code, "engine said no");
        using var client = Client(_token);
        var (status, body) = await Read(await client.PostAsync("/voice/transcribe", Audio(Pcm(3200))));
        Assert.Equal(expected, status);
        Assert.Equal(code, (string?)body["error"]!["code"]);
    }

    [Fact]
    public async Task UnexpectedEngineExceptionDoesNotLeakItsMessage()
    {
        await Start();
        _fake.Handler = _ => throw new InvalidOperationException("secret transcript text");
        using var client = Client(_token);
        var response = await client.PostAsync("/voice/transcribe", Audio(Pcm(3200)));
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("secret transcript", text);
    }

    [Fact]
    public async Task SecondConcurrentRequestGetsBusy()
    {
        await Start();
        var gate = new TaskCompletionSource<string>();
        var started = new TaskCompletionSource();
        _fake.Handler = _ => { started.TrySetResult(); return gate.Task; };
        using var client = Client(_token);
        var first = client.PostAsync("/voice/transcribe", Audio(Pcm(3200)));
        await started.Task;
        var (status, body) = await Read(await client.PostAsync("/voice/transcribe", Audio(Pcm(3200))));
        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.Equal("VOICE_BUSY", (string?)body["code"]);
        gate.SetResult("ok");
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }

    [Fact]
    public async Task BufferIsZeroedAfterTheRequestAndNothingSensitiveIsLogged()
    {
        await Start(diagnostics: true);
        _fake.Handler = _ => Task.FromResult("dodici virgola cinque millimetri");
        using var client = Client(_token);
        var pcm = Pcm(3200);
        var response = await client.PostAsync("/voice/transcribe", Audio(pcm));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_fake.LastBuffer.Span.ToArray().All(b => b == 0), "audio buffer must be cleared when the request ends");
        var all = string.Join("\n", _logs.Lines);
        Assert.Contains("voice client=quest bytes=3200", all);       // opt-in metadata only
        Assert.DoesNotContain("dodici", all);
        Assert.DoesNotContain("millimetri", all);
        Assert.DoesNotContain(_token, all);
    }

    [Fact]
    public async Task DiagnosticsAreOffByDefault()
    {
        await Start();
        using var client = Client(_token);
        await client.PostAsync("/voice/transcribe", Audio(Pcm(3200)));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("voice client="));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("ciao mondo"));
    }

    [Fact]
    public async Task VoiceIsNeverAnAuditedMcpTool()
    {
        await Start();
        using var client = Client(_token);
        await client.PostAsync("/voice/transcribe", Audio(Pcm(3200)));
        var audit = Path.Combine(_root, "audit");
        if (Directory.Exists(audit))
            foreach (var file in Directory.GetFiles(audit, "*", SearchOption.AllDirectories))
                Assert.DoesNotContain("ciao mondo", File.ReadAllText(file));
    }
}

public sealed class ExternalProcessSpeechEngineTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "so-ext-" + Guid.NewGuid().ToString("N"));
    public ExternalProcessSpeechEngineTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static bool Posix => !OperatingSystem.IsWindows() && File.Exists("/bin/sh");

    [Fact]
    public void TokenizerHonoursQuotes()
    {
        Assert.Equal(new[] { "python", "a b.py", "--x", "y z" },
            ExternalProcessSpeechEngine.Tokenize("python \"a b.py\" --x 'y z'"));
    }

    [Fact]
    public async Task PassesAWavFileAndDeletesItAfterwards()
    {
        if (!Posix) return;
        var engine = new ExternalProcessSpeechEngine("/bin/sh -c 'head -c 4 \"$0\"' {wav}", TimeSpan.FromSeconds(10), _tmp);
        var text = await engine.TranscribeAsync(new byte[3200], 16000, CancellationToken.None);
        Assert.Equal("RIFF", text);
        Assert.Empty(Directory.GetFiles(_tmp));
    }

    [Fact]
    public async Task TimeoutKillsTheCommandAndCleansUp()
    {
        if (!Posix) return;
        var engine = new ExternalProcessSpeechEngine("/bin/sh -c 'sleep 30' x", TimeSpan.FromMilliseconds(500), _tmp);
        var ex = await Assert.ThrowsAsync<SpeechEngineException>(() => engine.TranscribeAsync(new byte[3200], 16000, CancellationToken.None));
        Assert.Equal(SpeechEngineException.Timeout, ex.Code);
        Assert.Empty(Directory.GetFiles(_tmp));
    }

    [Fact]
    public async Task NonZeroExitAndMissingBinaryAreCodedAndStderrIsNotEchoed()
    {
        if (!Posix) return;
        var failing = new ExternalProcessSpeechEngine("/bin/sh -c 'echo secret-words >&2; exit 1' x", TimeSpan.FromSeconds(10), _tmp);
        var ex = await Assert.ThrowsAsync<SpeechEngineException>(() => failing.TranscribeAsync(new byte[3200], 16000, CancellationToken.None));
        Assert.Equal(SpeechEngineException.Failed, ex.Code);
        Assert.DoesNotContain("secret-words", ex.Message);
        var missing = new ExternalProcessSpeechEngine("/no/such/binary-xyz", TimeSpan.FromSeconds(10), _tmp);
        var ex2 = await Assert.ThrowsAsync<SpeechEngineException>(() => missing.TranscribeAsync(new byte[3200], 16000, CancellationToken.None));
        Assert.Equal(SpeechEngineException.Unavailable, ex2.Code);
        Assert.Empty(Directory.GetFiles(_tmp));
    }

    [Fact]
    public void ConfigReadsVoiceOptionsFromCliAndEnvironmentNames()
    {
        var config = InventorMcpConfig.Load(new[] { "--voice-command", "python x.py", "--voice-timeout-ms", "9000", "--voice-diagnostics" });
        Assert.Equal("python x.py", config.VoiceCommand);
        Assert.Equal(9000, config.VoiceTimeoutMs);
        Assert.True(config.VoiceDiagnostics);
        Assert.Null(InventorMcpConfig.Load(Array.Empty<string>()).VoiceCommand);
    }
}
