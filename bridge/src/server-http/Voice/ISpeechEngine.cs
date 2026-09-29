namespace Inventor.So.Mcp.Http.Voice;

/// <summary>
/// Local speech recognition behind a replaceable seam. Implementations run on this PC only: no engine
/// in this repository talks to a network service, and none may log or keep audio or transcripts.
/// </summary>
public interface ISpeechEngine
{
    /// <summary>Short label reported to the client (e.g. "external"); never contains paths or secrets.</summary>
    string Name { get; }

    /// <summary>True when the engine can be asked; false = the endpoint answers VOICE_DISABLED.</summary>
    bool IsEnabled { get; }

    /// <summary>Transcribe raw little-endian PCM16 mono. The buffer is owned by the caller, who clears it afterwards.</summary>
    Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16, int sampleRate, CancellationToken cancellationToken);
}

/// <summary>A coded engine failure; the message is safe to show (it never carries recognized text).</summary>
public sealed class SpeechEngineException : Exception
{
    public const string Unavailable = "VOICE_ENGINE_UNAVAILABLE";
    public const string Timeout = "VOICE_ENGINE_TIMEOUT";
    public const string Failed = "VOICE_ENGINE_FAILED";

    public string Code { get; }
    public SpeechEngineException(string code, string message) : base(message) => Code = code;
}

/// <summary>The default: voice is off until a local command is configured.</summary>
public sealed class DisabledSpeechEngine : ISpeechEngine
{
    public string Name => "disabled";
    public bool IsEnabled => false;
    public Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16, int sampleRate, CancellationToken cancellationToken) =>
        throw new SpeechEngineException(SpeechEngineException.Unavailable, "Voice is disabled.");
}
