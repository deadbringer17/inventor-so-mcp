using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Ipt.Server;

namespace Inventor.So.Mcp.Http.Voice;

/// <summary>
/// Runs a configured local command per request: the audio is written as a WAV to a private temp file
/// (its path is the last argument, or replaces a <c>{wav}</c> token), the command prints the transcript
/// on stdout and exits 0. The temp file is overwritten and deleted in <c>finally</c>; stderr and stdout
/// are never logged. See <c>Inventor XR SO/Tools~/stt-bench/serve_once.py</c> for a reference command.
/// </summary>
public sealed class ExternalProcessSpeechEngine : ISpeechEngine
{
    public const string WavToken = "{wav}";
    public const int MaxOutputChars = 8192;

    private readonly IReadOnlyList<string> _command;
    private readonly TimeSpan _timeout;
    private readonly string _tempDirectory;

    public string Name => "external";
    public bool IsEnabled => true;

    public ExternalProcessSpeechEngine(string commandLine, TimeSpan timeout, string? tempDirectory = null)
    {
        _command = Tokenize(commandLine);
        if (_command.Count == 0) throw new ArgumentException("The voice command is empty.", nameof(commandLine));
        _timeout = timeout;
        _tempDirectory = tempDirectory ?? Path.GetTempPath();
    }

    /// <summary>The engine for the configuration: external when a command is set, otherwise disabled.</summary>
    public static ISpeechEngine FromConfig(InventorMcpConfig config) =>
        string.IsNullOrWhiteSpace(config.VoiceCommand)
            ? new DisabledSpeechEngine()
            : new ExternalProcessSpeechEngine(config.VoiceCommand!, TimeSpan.FromMilliseconds(Math.Max(1000, config.VoiceTimeoutMs)));

    /// <summary>Whitespace-separated, with "double quotes" (or single) grouping; no shell, no expansion.</summary>
    public static IReadOnlyList<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        bool has = false;
        foreach (var ch in line)
        {
            if (quote != '\0') { if (ch == quote) quote = '\0'; else current.Append(ch); }
            else if (ch is '"' or '\'') { quote = ch; has = true; }
            else if (char.IsWhiteSpace(ch)) { if (has || current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); has = false; } }
            else current.Append(ch);
        }
        if (has || current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    public async Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16, int sampleRate, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_tempDirectory, "so-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await WriteWavAsync(path, pcm16, sampleRate, cancellationToken);
            return await RunAsync(path, cancellationToken);
        }
        finally
        {
            ScrubAndDelete(path);
        }
    }

    private async Task<string> RunAsync(string wavPath, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_command[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        bool placed = false;
        foreach (var arg in _command.Skip(1))
        {
            if (arg.Contains(WavToken, StringComparison.Ordinal)) { start.ArgumentList.Add(arg.Replace(WavToken, wavPath, StringComparison.Ordinal)); placed = true; }
            else start.ArgumentList.Add(arg);
        }
        if (!placed) start.ArgumentList.Add(wavPath);

        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            throw new SpeechEngineException(SpeechEngineException.Unavailable, "The voice command could not be started.");
        }
        try { process.StandardInput.Close(); } catch (IOException) { }

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stdout = ReadBoundedAsync(process.StandardOutput, MaxOutputChars);
        var stderr = process.StandardError.ReadToEndAsync();   // drained and discarded: it may echo recognized text
        try
        {
            await process.WaitForExitAsync(linked.Token);
            var text = await stdout;
            await stderr;
            if (process.ExitCode != 0)
                throw new SpeechEngineException(process.ExitCode == 3 ? SpeechEngineException.Unavailable : SpeechEngineException.Failed,
                    "The voice command failed (exit code " + process.ExitCode + ").");
            return text.Trim();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new SpeechEngineException(SpeechEngineException.Timeout, "The voice command did not answer within " + (int)_timeout.TotalMilliseconds + " ms.");
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int max)
    {
        var sb = new StringBuilder();
        var buffer = new char[1024];
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            if (sb.Length < max) sb.Append(buffer, 0, Math.Min(n, max - sb.Length));
        return sb.ToString();
    }

    private static async Task WriteWavAsync(string path, ReadOnlyMemory<byte> pcm, int sampleRate, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(path, options);
        var header = new byte[44];
        void Put(int at, string s) => Encoding.ASCII.GetBytes(s, 0, s.Length, header, at);
        Put(0, "RIFF"); BitConverter.TryWriteBytes(header.AsSpan(4), 36 + pcm.Length);
        Put(8, "WAVE"); Put(12, "fmt "); BitConverter.TryWriteBytes(header.AsSpan(16), 16);
        BitConverter.TryWriteBytes(header.AsSpan(20), (short)1); BitConverter.TryWriteBytes(header.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(header.AsSpan(24), sampleRate); BitConverter.TryWriteBytes(header.AsSpan(28), sampleRate * 2);
        BitConverter.TryWriteBytes(header.AsSpan(32), (short)2); BitConverter.TryWriteBytes(header.AsSpan(34), (short)16);
        Put(36, "data"); BitConverter.TryWriteBytes(header.AsSpan(40), pcm.Length);
        await file.WriteAsync(header, ct);
        await file.WriteAsync(pcm, ct);
    }

    /// <summary>Best effort: zero the audio on disk, then delete. Never throws.</summary>
    private static void ScrubAndDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var length = new FileInfo(path).Length;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var zeros = new byte[8192];
                for (long done = 0; done < length; done += zeros.Length) file.Write(zeros, 0, (int)Math.Min(zeros.Length, length - done));
            }
        }
        catch (Exception) { }
        try { File.Delete(path); } catch (Exception) { }
    }
}
