using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public interface ICadBatchBackend
{
    string DocumentId { get; }
    string Revision { get; }
    void Begin(string name);
    JObject Execute(string command, JObject arguments);
    void Validate();
    void Commit();
    void Rollback();
    /// <summary>
    /// Put the document's revision token back to what it was before a preview, after the preview's
    /// own rollback has restored the model. A preview is documented as leaving nothing behind, so it
    /// must not invalidate the revision the caller planned against.
    /// </summary>
    void RestoreRevision(string revision);
}

/// <summary>
/// A batch failure the caller can act on programmatically: the message keeps its human form, and
/// <see cref="Code"/>, <see cref="StepIndex"/> and <see cref="Command"/> say what failed and where,
/// without parsing prose.
/// </summary>
public sealed class CadBatchException : Exception
{
    public CadBatchException(string code, string message, int? stepIndex = null, string? command = null,
        string? stepCode = null, Exception? inner = null) : base(message, inner)
    {
        Code = code;
        StepIndex = stepIndex;
        Command = command;
        StepCode = stepCode;
    }

    /// <summary>Batch-level code: INVALID_ARGUMENT, DOCUMENT_CHANGED, STALE_REVISION, TIMEOUT, ROLLED_BACK, ROLLBACK_FAILED.</summary>
    public string Code { get; }
    /// <summary>0-based index of the operation that failed, when the failure belongs to one.</summary>
    public int? StepIndex { get; }
    /// <summary>Wire command of the failing operation, when the failure belongs to one.</summary>
    public string? Command { get; }
    /// <summary>Error code the failing handler itself returned, when it returned one.</summary>
    public string? StepCode { get; }

    public JObject Details()
    {
        var details = new JObject { ["code"] = Code };
        if (StepIndex != null) details["step_index"] = StepIndex;
        if (Command != null) details["command"] = Command;
        if (StepCode != null) details["step_code"] = StepCode;
        return details;
    }
}

/// <summary>A bounded single-request transaction, never held open across client calls.</summary>
public static class AtomicCadBatch
{
    /// <summary>
    /// The executable vocabulary, taken from the published catalogue so the discoverable list and the
    /// enforced list can never drift apart.
    /// </summary>
    private static readonly HashSet<string> Allowed =
        new(CadBatchCommandCatalog.Names, StringComparer.Ordinal);

    /// <summary>Allowed command names, for callers that want to check a plan before sending it.</summary>
    public static IReadOnlyList<string> AllowedCommands => CadBatchCommandCatalog.Names;

    public static JObject Run(ICadBatchBackend backend, string documentId, string expectedRevision,
        JArray operations, bool preview, Func<bool>? expired = null)
    {
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(expectedRevision))
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "document_id and expected_revision are required.");
        if (operations.Count < 1 || operations.Count > 32)
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "Use 1 to 32 operations per transaction.");
        // Validate the entire command surface before beginning or executing any operation.
        for (int i = 0; i < operations.Count; i++)
            Reject(operations[i], i);
        if (backend.DocumentId != documentId)
            throw new CadBatchException(InventorErrorCodes.DOCUMENT_CHANGED, "DOCUMENT_CHANGED: active document differs from the plan.");
        if (backend.Revision != expectedRevision)
            throw new CadBatchException(InventorErrorCodes.STALE_REVISION, "STALE_REVISION: read the document again and replan.");
        if (expired?.Invoke() == true)
            throw new CadBatchException(InventorErrorCodes.TIMEOUT, "Batch expired before execution.");

        backend.Begin("Inventor SO atomic batch");
        var results = new JArray();
        int index = 0;
        string? command = null;
        try
        {
            foreach (JObject step in operations)
            {
                command = (string)step["command"]!;
                if (expired?.Invoke() == true)
                    throw new CadBatchException(InventorErrorCodes.TIMEOUT, "Batch deadline exceeded.", index, command);
                if (backend.DocumentId != documentId)
                    throw new CadBatchException(InventorErrorCodes.DOCUMENT_CHANGED, "DOCUMENT_CHANGED during batch.", index, command);
                results.Add(backend.Execute(command, (JObject)step["arguments"]!));
                index++;
            }
            command = null;
            backend.Validate();
            if (expired?.Invoke() == true)
                throw new CadBatchException(InventorErrorCodes.TIMEOUT, "Batch deadline exceeded before commit.", index);
            if (backend.DocumentId != documentId)
                throw new CadBatchException(InventorErrorCodes.DOCUMENT_CHANGED, "DOCUMENT_CHANGED before commit.", index);
            if (preview)
            {
                backend.Rollback();
                // A preview is defined as leaving nothing behind, so the revision the caller planned
                // against stays valid and several batches can be prepared from one read.
                backend.RestoreRevision(expectedRevision);
            }
            else backend.Commit();
            return new JObject { ["status"] = preview ? "preview_rolled_back" : "committed", ["steps"] = results,
                ["document_id"] = documentId, ["revision"] = backend.Revision };
        }
        catch (Exception error)
        {
            string? stepCode = StepCodeOf(error);
            try { backend.Rollback(); }
            catch (Exception rollbackError)
            {
                throw new CadBatchException(InventorErrorCodes.ROLLBACK_FAILED,
                    "ROLLBACK_FAILED: inspect CAD before continuing. Step " + index + ": " + error.Message +
                    "; rollback: " + rollbackError.Message, index, command, stepCode, error);
            }
            throw new CadBatchException(InventorErrorCodes.ROLLED_BACK,
                "ROLLED_BACK: step " + index + ": " + error.Message, index, command, stepCode, error);
        }
    }

    /// <summary>
    /// Refuse a malformed or unlisted operation, naming the step and the command. A caller that only
    /// learns "something in the plan is wrong" has to bisect a 32-step batch by hand.
    /// </summary>
    private static void Reject(JToken token, int index)
    {
        string at = "operation " + index + ": ";
        if (token is not JObject step)
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT,
                at + "each operation must be an object with command and arguments.", index);
        if (step["command"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)step["command"]))
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT,
                at + "command must be a non-empty wire command name. " + Vocabulary(), index);
        string command = (string)step["command"]!;
        if (!Allowed.Contains(command))
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT,
                at + "'" + command + "' is not a batch command. Scripting, file IO, document lifecycle and " +
                "nested batches are excluded. " + Vocabulary(), index, command);
        if (step["arguments"] is not JObject)
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT,
                at + "'" + command + "' needs an arguments object" +
                (step["arguments"] == null ? " (none was sent)." : ", not " + step["arguments"]!.Type + ".") +
                " " + Expected(command), index, command);
    }

    private static string Expected(string command)
    {
        var entry = CadBatchCommandCatalog.Find(command);
        if (entry == null) return "";
        return "Required: " + (entry.Required.Count == 0 ? "(none)" : string.Join(", ", entry.Required)) + ".";
    }

    private static string Vocabulary()
        => "Allowed commands: " + string.Join(", ", CadBatchCommandCatalog.Names) +
           " (full argument lists: the inventor://batch-commands resource).";

    /// <summary>
    /// Handlers report failure as "CODE: message"; lift that code out so the caller does not have to
    /// read the sentence to find it.
    /// </summary>
    private static string? StepCodeOf(Exception error) => CodedFailureException.LiftCode(error);
}
