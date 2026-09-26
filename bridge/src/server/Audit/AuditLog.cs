using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Audit;

/// <summary>
/// Append-only JSON Lines audit of every call that can change CAD, view or files (plan §23): one
/// record per call, refused ones included. Identity is the authenticated caller (token name or
/// <c>stdio</c>); the client's self-declared name is kept apart as <c>declared_client</c>. Argument
/// values are never written - only their names and a SHA-256 of the whole argument object - so
/// neither tokens nor model content leak into the log.
/// </summary>
public sealed class AuditLog
{
    private readonly object _gate = new();
    private readonly string? _directory;
    private readonly Func<DateTimeOffset> _clock;

    private readonly string _stream;

    public AuditLog(InventorMcpConfig config) : this(config.AuditEnabled ? config.AuditDirectory : null, null, config.Transport) { }

    /// <param name="stream">Host kind in the file name. Each process writes its own file
    /// (audit-YYYYMMDD-&lt;stream&gt;-&lt;pid&gt;.jsonl): a stdio server and the HTTP host appending to one file
    /// would contend for it, and a log write must never be what fails a CAD operation.</param>
    public AuditLog(string? directory, Func<DateTimeOffset>? clock = null, string stream = "stdio")
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _stream = string.IsNullOrWhiteSpace(stream) ? "server" : stream;
    }

    public bool Enabled => _directory != null;

    /// <summary>Whether a tool is audited: anything that is not a pure read.</summary>
    public static bool IsAudited(string? tool)
    {
        var contract = ToolContracts.Find(tool);
        // Unknown tools (full-access legacy writes) are audited too: fail towards recording.
        return contract == null || contract.Access is not ("query" or "meta");
    }

    public string? CurrentFile => _directory == null ? null
        : Path.Combine(_directory, "audit-" + _clock().ToString("yyyyMMdd") + "-" + _stream + "-" + Environment.ProcessId + ".jsonl");

    /// <summary>Records that could not be written (disk full, permissions); reported, never silently lost.</summary>
    public long FailedWrites { get; private set; }

    public void Write(JObject record)
    {
        if (_directory == null) return;
        record["timestamp"] ??= _clock().ToString("O");
        var line = record.ToString(Formatting.None) + "\n";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(CurrentFile!, line, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The CAD operation already happened (or was refused); failing the tool call now
                // would misreport it. Keep the record on stderr, which MCP hosts collect.
                FailedWrites++;
                Console.Error.WriteLine("inventor-so-mcp: audit write failed (" + ex.Message + "): " + line.TrimEnd());
            }
        }
    }

    /// <summary>Build the record for one tool call from its request and (possibly failed) result.</summary>
    public static JObject Record(string tool, IDictionary<string, JsonElement>? arguments, string? resultText, string? exceptionCode,
        string client, string? sessionId, string? declaredClient, long durationMs)
    {
        var args = arguments == null ? new JObject() : JObject.Parse(System.Text.Json.JsonSerializer.Serialize(arguments));
        JObject? result = null;
        if (resultText != null) { try { result = JObject.Parse(resultText); } catch (Newtonsoft.Json.JsonException) { } }
        string? errorCode = exceptionCode ?? (result?["ok"]?.Type == JTokenType.Boolean && !(bool)result["ok"]! ? (string?)result["error"]?["code"] : null);
        string outcome = errorCode != null ? "error"
            : (string?)result?["status"] ?? ((string?)result?["plan_id"] != null && tool == "inventor_plan_change" ? "planned" : "ok");
        return new JObject
        {
            ["session_id"] = sessionId,
            ["client"] = client,
            ["declared_client"] = declaredClient,
            ["tool"] = tool,
            ["document_id"] = (string?)args["document_id"] ?? (string?)result?["document_id"],
            ["revision_before"] = (string?)args["expected_revision"],
            ["revision_after"] = (string?)result?["revision"],
            ["preview"] = (bool?)args["preview"] ?? (tool is "inventor_plan_change" or "inventor_sample_parameter_motion" ? true : null),
            ["validation"] = args["validate"]?.DeepClone() ?? result?["validated"]?.DeepClone(),
            ["plan_id"] = (string?)args["plan_id"] ?? (string?)result?["plan_id"],
            ["result"] = outcome,
            ["error_code"] = errorCode,
            ["duration_ms"] = durationMs,
            ["argument_names"] = new JArray(args.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)),
            ["arguments_sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(args.ToString(Formatting.None)))).ToLowerInvariant(),
        };
    }

    /// <summary>The <c>tools/call</c> filter that writes the record around the real handler.</summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter() => next => async (context, ct) =>
    {
        var tool = context.Params?.Name;
        var log = context.Services?.GetService<AuditLog>();
        if (log == null || !log.Enabled || !IsAudited(tool)) return await next(context, ct);
        var caller = context.Services?.GetService<ICallerIdentity>() ?? new StdioCallerIdentity();
        string? declared = null;
        try { declared = context.Server?.ClientInfo?.Name; } catch { /* not yet initialized */ }
        string? session = caller.SessionId;
        try { session ??= context.Server?.SessionId; } catch { }
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await next(context, ct);
            string? text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            log.Write(Record(tool ?? "", context.Params?.Arguments, text, result.IsError == true && text == null ? "TOOL_ERROR" : null,
                caller.Client, session, declared, watch.ElapsedMilliseconds));
            return result;
        }
        catch (Exception ex)
        {
            log.Write(Record(tool ?? "", context.Params?.Arguments, null, ex.GetType().Name, caller.Client, session, declared, watch.ElapsedMilliseconds));
            throw;
        }
    };
}
