using Bimwright.Ipt.Server;
using Bimwright.Ipt.Tests;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.Mcp.Http.Voice;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;
using InventorXrSo.Core.Voice;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace XrSo.Core.Tests.Voice;

public sealed class FakeVoiceEngine : ISpeechEngine
{
    public string Name => "fake";
    public bool IsEnabled { get; set; } = true;
    public Func<byte[], CancellationToken, Task<string>> Handler { get; set; } = (_, _) => Task.FromResult("flangia");
    public byte[] LastAudio;
    public int Calls;
    public async Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16, int sampleRate, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        LastAudio = pcm16.ToArray();
        return await Handler(LastAudio, ct);
    }
}

/// <summary>Records every request the recognizer sends, then delegates.</summary>
internal sealed class RecordingTransport : IHttpTransport
{
    private readonly IHttpTransport _inner;
    public List<TransportRequest> Requests { get; } = new();
    public RecordingTransport(IHttpTransport inner) { _inner = inner; }
    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct) { Requests.Add(request); return _inner.SendAsync(request, ct); }
    public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => _inner.StreamLinesAsync(request, onLine, ct);
}

/// <summary>Real paired HTTPS host (self-signed, pinned) with a fake speech engine. One class: the endpoint serves one request at a time.</summary>
public sealed class HttpSpeechRecognizerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xrso-voice-" + Guid.NewGuid().ToString("N"));
    private readonly string _token = TokenRegistry.Generate();
    private readonly FakeVoiceEngine _engine = new();
    private readonly TaskCompletionSource _release = new();
    private WebApplication _app;
    private FakeAddIn _addIn;
    private string _certSha;
    private Uri _base;

    public async Task InitializeAsync()
    {
        _addIn = new FakeAddIn(Path.Combine(_root, "targets"));
        var tokens = new TokenRegistry();
        tokens.Add("editor", _token);
        var config = new InventorMcpConfig
        {
            HttpSelfSignedCertificate = true,
            HttpSelfSignedPath = Path.Combine(_root, "server.pfx"),
            HttpTokenFile = Path.Combine(_root, "tokens.txt"),
            DescriptorDirectory = _addIn.DescriptorDirectory,
            AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"),
            HttpRateLimitPerMinute = 1000,
        };
        config.HttpUrls = new List<string> { "https://127.0.0.1:0" };
        var certificate = PairingSetup.ResolveCertificate(config);
        _certSha = SelfSignedCertificate.Sha256Hex(certificate);
        var pairing = new PairingStore(() => DateTimeOffset.UtcNow);
        _app = HttpHost.Build(Array.Empty<string>(), config, tokens, new HostOptions
        {
            Certificate = certificate,
            Pairing = new PairingEndpoint(pairing, tokens, config.HttpTokenFile),
            ConfigureServices = s => s.AddSingleton<ISpeechEngine>(_engine),
        });
        await _app.StartAsync();
        _base = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/'));
    }

    public async Task DisposeAsync()
    {
        _release.TrySetResult();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _addIn.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private PairedServer Server(string token = null, string certSha = null) =>
        new PairedServer(_base.Host, _base.Port, certSha ?? _certSha, "editor", token ?? _token);
    private SystemHttpTransport Transport(string certSha = null) => new SystemHttpTransport(ServerTrust.Pinned(certSha ?? _certSha));
    private HttpSpeechRecognizer Recognizer(IHttpTransport transport = null, PairedServer server = null, TimeSpan? timeout = null) =>
        new HttpSpeechRecognizer(transport ?? Transport(), server ?? Server(), timeout);
    private static short[] Audio(int samples = 16000) => Enumerable.Range(0, samples).Select(i => (short)(i % 200 - 100)).ToArray();

    private static async Task<VoiceRecognitionException> Fail(Task<string> task) => await Assert.ThrowsAsync<VoiceRecognitionException>(() => task);

    [Fact]
    public async Task Sends_exact_pcm16_little_endian_to_the_paired_host_and_returns_the_transcript()
    {
        var transport = new RecordingTransport(Transport());
        var pcm = new short[] { 0, 1, -1, 32767, -32768, 258 };
        pcm = pcm.Concat(Audio(8000)).ToArray();
        var text = await Recognizer(transport).RecognizeAsync(pcm, CancellationToken.None);
        Assert.Equal("flangia", text);
        var request = Assert.Single(transport.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(_base.GetLeftPart(UriPartial.Authority) + "/voice/transcribe", request.Url);
        Assert.Equal("audio/L16;rate=16000", request.Headers["Content-Type"]);
        Assert.Equal("Bearer " + _token, request.Headers["Authorization"]);
        Assert.Equal(pcm.Length * 2, _engine.LastAudio.Length);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 0xFF, 0xFF, 0xFF, 0x7F, 0x00, 0x80, 0x02, 0x01 }, _engine.LastAudio.Take(12).ToArray());
    }

    [Fact]
    public async Task Request_buffer_is_cleared_after_the_call()
    {
        var transport = new RecordingTransport(Transport());
        await Recognizer(transport).RecognizeAsync(Audio(), CancellationToken.None);
        Assert.All(transport.Requests[0].Body, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task A_wrong_pin_never_sends_audio_and_reports_a_changed_certificate()
    {
        var wrong = new string('a', 64);
        var ex = await Fail(Recognizer(Transport(wrong), Server(certSha: wrong)).RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal("CERTIFICATE_CHANGED", ex.Code);
        Assert.Equal(0, _engine.Calls);
    }

    [Fact]
    public async Task A_wrong_token_is_reported_as_unauthorized_without_calling_the_engine()
    {
        var ex = await Fail(Recognizer(server: Server(token: "not-a-token")).RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal("UNAUTHORIZED", ex.Code);
        Assert.Contains("Associa di nuovo", ex.Message);
        Assert.Equal(0, _engine.Calls);
    }

    [Fact]
    public async Task Disabled_engine_maps_to_an_italian_message()
    {
        _engine.IsEnabled = false;
        var ex = await Fail(Recognizer().RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal("VOICE_DISABLED", ex.Code);
        Assert.Contains("non e abilitato", ex.Message);
    }

    [Theory]
    [InlineData(SpeechEngineException.Unavailable, "VOICE_ENGINE_UNAVAILABLE", "non e disponibile")]
    [InlineData(SpeechEngineException.Timeout, "VOICE_ENGINE_TIMEOUT", "troppo tempo")]
    [InlineData(SpeechEngineException.Failed, "VOICE_ENGINE_FAILED", "non riuscito")]
    public async Task Engine_errors_map_to_codes_and_never_leak_the_server_text(string engineCode, string expected, string italian)
    {
        _engine.Handler = (_, _) => throw new SpeechEngineException(engineCode, "SEGRETO C:\\percorso\\motore.exe");
        var ex = await Fail(Recognizer().RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal(expected, ex.Code);
        Assert.Contains(italian, ex.Message);
        Assert.DoesNotContain("SEGRETO", ex.Message);
        Assert.DoesNotContain("motore.exe", ex.Message);
    }

    [Fact]
    public async Task A_generic_engine_crash_maps_to_a_generic_failure()
    {
        _engine.Handler = (_, _) => throw new InvalidOperationException("SEGRETO");
        var ex = await Fail(Recognizer().RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal("VOICE_ENGINE_FAILED", ex.Code);
        Assert.DoesNotContain("SEGRETO", ex.Message);
    }

    [Fact]
    public async Task A_second_concurrent_request_is_busy()
    {
        var entered = new TaskCompletionSource();
        _engine.Handler = async (_, _) => { entered.TrySetResult(); await _release.Task; return "flangia"; };
        var first = Recognizer().RecognizeAsync(Audio(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ex = await Fail(Recognizer().RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal("VOICE_BUSY", ex.Code);
        Assert.Contains("Riprova", ex.Message);
        _release.SetResult();
        Assert.Equal("flangia", await first);
    }

    [Fact]
    public async Task A_slow_server_times_out_with_a_readable_message()
    {
        _engine.Handler = async (_, _) => { await _release.Task; return "flangia"; };
        var ex = await Fail(Recognizer(timeout: TimeSpan.FromMilliseconds(300)).RecognizeAsync(Audio(), CancellationToken.None));
        Assert.Equal("TIMEOUT", ex.Code);
        Assert.Contains("troppo tempo", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_recognition_error()
    {
        _engine.Handler = async (_, _) => { await _release.Task; return "flangia"; };
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Recognizer().RecognizeAsync(Audio(), cts.Token));
    }

    [Fact]
    public async Task Audio_over_ten_seconds_is_refused_locally()
    {
        var transport = new RecordingTransport(Transport());
        var ex = await Fail(Recognizer(transport).RecognizeAsync(new short[HttpSpeechRecognizer.MaxSamples + 1], CancellationToken.None));
        Assert.Equal("VOICE_TOO_LARGE", ex.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task An_unreachable_host_is_a_network_error()
    {
        var server = new PairedServer("127.0.0.1", 1, _certSha, "editor", _token);
        var ex = await Fail(Recognizer(server: server, timeout: TimeSpan.FromSeconds(3)).RecognizeAsync(Audio(), CancellationToken.None));
        Assert.True(ex.Code == "NETWORK" || ex.Code == "TIMEOUT");
    }

    // ---- end to end: microfono simulato -> controller -> HTTP -> router ----

    private sealed class Availability : ICommandAvailability
    {
        public CommandAvailability GetAvailability(string id) => CommandAvailability.Available;
    }

    private async Task<PushToTalkController> Speak(string transcript)
    {
        _engine.Handler = (_, _) => Task.FromResult(transcript);
        var now = DateTimeOffset.UtcNow;
        var controller = new PushToTalkController(Recognizer(), new VoiceCommandRouter(), new Availability(), clock: () => now);
        controller.Press();
        now += TimeSpan.FromMilliseconds(200);
        controller.Tick();
        Assert.True(controller.IsListening);
        controller.AppendAudio(Audio(16000), 16000);
        controller.Release();
        for (int i = 0; i < 200 && controller.State == PushToTalkState.Processing; i++) await Task.Delay(50);
        return controller;
    }

    [Fact]
    public async Task EndToEnd_flangia_proposes_the_flange_command()
    {
        using var controller = await Speak("flangia");
        Assert.Equal(PushToTalkState.Result, controller.State);
        Assert.Equal("flangia", controller.Transcript);
        Assert.Equal(CommandIds.Flange, controller.ProposedCommand.CommandId);
        Assert.True(controller.ProposedCommand.CanInvoke);
        Assert.False(controller.MicrophoneOpen);
    }

    [Fact]
    public async Task EndToEnd_applica_only_shows_the_confirmation_never_a_commit()
    {
        using var controller = await Speak("applica");
        var r = controller.ProposedCommand;
        Assert.Equal(VoiceRouteKind.ShowApplyConfirmation, r.Kind);
        Assert.False(r.CanInvoke);
        Assert.True(r.RequiresPhysicalConfirmation);
    }

    [Fact]
    public async Task EndToEnd_server_error_becomes_a_readable_controller_error()
    {
        _engine.IsEnabled = false;
        var now = DateTimeOffset.UtcNow;
        using var controller = new PushToTalkController(Recognizer(), new VoiceCommandRouter(), new Availability(), clock: () => now);
        controller.Press(); now += TimeSpan.FromMilliseconds(200); controller.Tick();
        controller.AppendAudio(Audio(), 16000); controller.Release();
        for (int i = 0; i < 200 && controller.State == PushToTalkState.Processing; i++) await Task.Delay(50);
        Assert.Equal(PushToTalkState.Error, controller.State);
        Assert.Contains("non e abilitato", controller.ErrorMessage);
        Assert.Null(controller.ProposedCommand);
    }
}
