using System.Diagnostics;
using System.Security.Cryptography;
using Bimwright.Ipt.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Inventor.So.Mcp.Http.Voice;

/// <summary>
/// POST /voice/transcribe: raw PCM16 mono 16 kHz in, <c>{transcript, engine, elapsed_ms}</c> out. Same
/// bearer auth and rate limit as /mcp. It only transcribes: the text is never interpreted here (command
/// routing is client-side). Audio and transcripts are never logged; with diagnostics on, only the client
/// name, byte count, duration and outcome are.
/// </summary>
public static class VoiceEndpoint
{
    public const string Route = "/voice/transcribe";
    public const int SampleRate = 16000;
    public const int MaxSeconds = 10;
    public const int MaxBytes = SampleRate * 2 * MaxSeconds;
    public const string SampleRateHeader = "X-Audio-Sample-Rate";
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public sealed record Settings(bool Diagnostics);

    public static void Map(WebApplication app)
    {
        app.MapPost(Route, HandleAsync).RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { ok = false, code, error = new { code, message } }, statusCode: status);

    private static async Task<IResult> HandleAsync(HttpContext http, ISpeechEngine engine, Settings settings, ICallerIdentity caller, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("Inventor.So.Mcp.Http.Voice");
        if (!engine.IsEnabled)
            return Error(StatusCodes.Status503ServiceUnavailable, "VOICE_DISABLED", "Voice transcription is not enabled on this host.");

        var request = http.Request;
        if (!AcceptsFormat(request, out var formatProblem))
            return Error(StatusCodes.Status415UnsupportedMediaType, "VOICE_UNSUPPORTED_FORMAT", formatProblem);

        // Refuse oversized bodies before reading a byte, and cap what a chunked body can deliver.
        if (request.ContentLength is { } declared)
        {
            if (declared == 0) return Error(StatusCodes.Status400BadRequest, "VOICE_EMPTY", "No audio in the request body.");
            if (declared > MaxBytes) return TooLarge();
        }
        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = MaxBytes;

        if (!await OneAtATime.WaitAsync(0))
            return Error(StatusCodes.Status429TooManyRequests, "VOICE_BUSY", "A transcription is already running; retry shortly.");
        var buffer = new byte[MaxBytes + 1];
        int length = 0;
        var watch = Stopwatch.StartNew();
        string outcome = "ok";
        try
        {
            try
            {
                int n;
                while (length <= MaxBytes && (n = await request.Body.ReadAsync(buffer.AsMemory(length, buffer.Length - length), http.RequestAborted)) > 0)
                    length += n;
            }
            catch (BadHttpRequestException) { length = MaxBytes + 1; }
            if (length > MaxBytes) { outcome = "too_large"; return TooLarge(); }
            if (length == 0) { outcome = "empty"; return Error(StatusCodes.Status400BadRequest, "VOICE_EMPTY", "No audio in the request body."); }
            if (length % 2 != 0) { outcome = "bad_length"; return Error(StatusCodes.Status400BadRequest, "VOICE_BAD_AUDIO", "PCM16 needs an even number of bytes."); }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
                var transcript = await engine.TranscribeAsync(buffer.AsMemory(0, length), SampleRate, timeout.Token);
                return Results.Json(new { transcript, engine = engine.Name, elapsed_ms = watch.ElapsedMilliseconds });
            }
            catch (SpeechEngineException ex)
            {
                outcome = ex.Code;
                var status = ex.Code == SpeechEngineException.Timeout ? StatusCodes.Status504GatewayTimeout
                    : ex.Code == SpeechEngineException.Unavailable ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway;
                return Error(status, ex.Code, ex.Message);
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                outcome = "aborted";
                return Results.StatusCode(499);
            }
            catch (Exception)
            {
                outcome = SpeechEngineException.Failed;
                return Error(StatusCodes.Status502BadGateway, SpeechEngineException.Failed, "The speech engine failed.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);   // the audio leaves memory with the request
            OneAtATime.Release();
            if (settings.Diagnostics)
                log.LogInformation("voice client={Client} bytes={Bytes} duration_ms={Duration} outcome={Outcome}",
                    caller.Client, length, watch.ElapsedMilliseconds, outcome);
        }
    }

    private static IResult TooLarge() =>
        Error(StatusCodes.Status413PayloadTooLarge, "VOICE_TOO_LARGE", "Audio exceeds " + MaxSeconds + " s (" + MaxBytes + " bytes) of 16 kHz PCM16 mono.");

    /// <summary><c>audio/L16;rate=16000</c>, or <c>application/octet-stream</c> with X-Audio-Sample-Rate: 16000 (mono implied).</summary>
    internal static bool AcceptsFormat(HttpRequest request, out string problem)
    {
        problem = "";
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type))
        {
            problem = "Content-Type must be audio/L16;rate=16000 or application/octet-stream with " + SampleRateHeader + ".";
            return false;
        }
        string? rate;
        if (type.MediaType.Equals("audio/L16", StringComparison.OrdinalIgnoreCase))
        {
            rate = type.Parameters.FirstOrDefault(p => p.Name.Equals("rate", StringComparison.OrdinalIgnoreCase))?.Value.Value;
            var channels = type.Parameters.FirstOrDefault(p => p.Name.Equals("channels", StringComparison.OrdinalIgnoreCase))?.Value.Value;
            if (channels != null && channels != "1") { problem = "Only mono audio is accepted."; return false; }
        }
        else if (type.MediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            rate = request.Headers[SampleRateHeader].FirstOrDefault();
        else
        {
            problem = "Content-Type must be audio/L16;rate=16000 or application/octet-stream with " + SampleRateHeader + ".";
            return false;
        }
        if (rate != "16000") { problem = "Only 16000 Hz sample rate is accepted."; return false; }
        return true;
    }
}
