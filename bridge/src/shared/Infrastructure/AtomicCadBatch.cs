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
/// Optional capability of a backend: run the caller's <c>validate</c> checks before commit. A backend
/// that does not implement it can still run batches whose checks are only the document defaults.
/// </summary>
public interface ICadBatchValidatingBackend
{
    /// <summary>Throw <see cref="CodedFailureException"/> with <c>VALIDATION_FAILED</c> when a check fails.</summary>
    void Validate(ValidationSpec spec);
}

/// <summary>Capture the tentative part while the owned transaction is still open.</summary>
public interface ICadBatchPreviewBackend
{
    JObject CapturePreview();
}

/// <summary>What the caller of a batch is allowed to run, and against which kind of document.</summary>
public sealed class CadBatchOptions
{
    /// <summary>The active document's kind (<see cref="CadDocumentKinds"/>); null skips the kind check (legacy callers).</summary>
    public string? DocumentKind { get; set; }
    /// <summary>Admit experimental catalogue entries. Host-owned: never taken from the request.</summary>
    public bool AllowExperimental { get; set; }
    /// <summary>
    /// Whether the executing add-in has a handler for a command. An experimental command can be
    /// catalogued yet absent from a default build; refusing it here keeps the whole plan untouched.
    /// </summary>
    public Func<string, bool>? IsRegistered { get; set; }
    /// <summary>Whether this add-in build can run a validation check; unsupported checks are refused before the transaction.</summary>
    public Func<string, bool>? SupportsCheck { get; set; }
    /// <summary>The raw <c>validate</c> argument, parsed against <see cref="DocumentKind"/>.</summary>
    public JToken? Validate { get; set; }
    public bool IncludePreviewMesh { get; set; }
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
        // A failing step or validation check may carry its own specifics (the interfering pair, the
        // measured clearance): pass them through instead of flattening them into the message.
        if (InnerException is CodedFailureException { Details: { } inner }) details["step_details"] = inner.DeepClone();
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
        new(CadBatchCommandCatalog.AllNames, StringComparer.Ordinal);

    /// <summary>Stable (production) command names, for callers that want to check a plan before sending it.</summary>
    public static IReadOnlyList<string> AllowedCommands => CadBatchCommandCatalog.Names;

    public static JObject Run(ICadBatchBackend backend, string documentId, string expectedRevision,
        JArray operations, bool preview, Func<bool>? expired = null)
        => Run(backend, documentId, expectedRevision, operations, preview, expired, null);

    public static JObject Run(ICadBatchBackend backend, string documentId, string expectedRevision,
        JArray operations, bool preview, Func<bool>? expired, CadBatchOptions? options)
    {
        options ??= new CadBatchOptions();
        if (options.IncludePreviewMesh && (!preview || backend is not ICadBatchPreviewBackend
            || (options.DocumentKind != CadDocumentKinds.Part && options.DocumentKind != CadDocumentKinds.Assembly) || !options.AllowExperimental))
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT,
                "Preview meshes require an experimental part or assembly preview backend.");
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(expectedRevision))
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "document_id and expected_revision are required.");
        if (operations.Count < 1 || operations.Count > 32)
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "Use 1 to 32 operations per transaction.");
        // Validate the entire command surface before beginning or executing any operation.
        for (int i = 0; i < operations.Count; i++)
            Reject(operations[i], i, options);
        ValidationSpec? checks = null;
        if (options.DocumentKind != null)
        {
            try { checks = ValidationSpec.Parse(options.Validate, options.DocumentKind); }
            catch (ArgumentException ex) { throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "validate: " + ex.Message); }
        }
        else if (options.Validate != null && options.Validate.Type != JTokenType.Null)
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "validate needs a backend that reports its document kind.");
        if (checks != null && options.SupportsCheck != null)
            foreach (var rule in checks.Rules)
                if (!options.SupportsCheck(rule.Name))
                    throw new CadBatchException(InventorErrorCodes.EXPERIMENTAL_DISABLED,
                        "validate: '" + rule.Name + "' needs the experimental add-in build.");
        var validating = backend as ICadBatchValidatingBackend;
        if (checks != null && validating == null && options.Validate != null && options.Validate.Type != JTokenType.Null)
            throw new CadBatchException(InventorErrorCodes.INVALID_ARGUMENT, "This add-in cannot run requested validation checks.");
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
            if (checks != null && validating != null) validating.Validate(checks);
            if (expired?.Invoke() == true)
                throw new CadBatchException(InventorErrorCodes.TIMEOUT, "Batch deadline exceeded before commit.", index);
            if (backend.DocumentId != documentId)
                throw new CadBatchException(InventorErrorCodes.DOCUMENT_CHANGED, "DOCUMENT_CHANGED before commit.", index);
            JObject? previewMesh = null;
            if (options.IncludePreviewMesh)
            {
                previewMesh = ((ICadBatchPreviewBackend)backend).CapturePreview();
                if (expired?.Invoke() == true)
                    throw new CadBatchException(InventorErrorCodes.TIMEOUT, "Batch expired while capturing preview.");
                if (backend.DocumentId != documentId)
                    throw new CadBatchException(InventorErrorCodes.DOCUMENT_CHANGED, "Document changed while capturing preview.");
            }
            if (preview)
            {
                backend.Rollback();
                // A preview is defined as leaving nothing behind, so the revision the caller planned
                // against stays valid and several batches can be prepared from one read.
                backend.RestoreRevision(expectedRevision);
            }
            else backend.Commit();
            var outcome = new JObject { ["status"] = preview ? "preview_rolled_back" : "committed", ["steps"] = results,
                ["document_id"] = documentId, ["revision"] = backend.Revision };
            if (checks != null) outcome["validated"] = checks.ToJson();
            if (previewMesh != null) outcome["preview_mesh"] = previewMesh;
            return outcome;
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
            // The rollback left the model as it was read, so the caller's revision stays valid, as
            // after a preview: fixing one argument must not force a re-read. Verified live on 2027:
            // the aborted transaction still raises change events that advanced the revision.
            try { backend.RestoreRevision(expectedRevision); } catch { /* the refusal already stands */ }
            throw new CadBatchException(InventorErrorCodes.ROLLED_BACK,
                "ROLLED_BACK: step " + index + ": " + error.Message, index, command, stepCode, error);
        }
    }

    /// <summary>
    /// Refuse a malformed or unlisted operation, naming the step and the command. A caller that only
    /// learns "something in the plan is wrong" has to bisect a 32-step batch by hand.
    /// </summary>
    private static void Reject(JToken token, int index, CadBatchOptions options)
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
        var entry = CadBatchCommandCatalog.Find(command)!;
        if (entry.Experimental && !options.AllowExperimental)
            throw new CadBatchException(InventorErrorCodes.EXPERIMENTAL_DISABLED,
                at + "'" + command + "' is an experimental batch command (implemented, not yet live-verified). " +
                "Start the add-in with INVENTOR_SO_EXPERIMENTAL=1 from an experimental build to use it.", index, command);
        if (options.IsRegistered != null && !options.IsRegistered(command))
            throw new CadBatchException(entry.Experimental ? InventorErrorCodes.EXPERIMENTAL_DISABLED : InventorErrorCodes.INVALID_ARGUMENT,
                at + "'" + command + "' is catalogued but this add-in build has no handler for it" +
                (entry.Experimental ? " (build with -p:SoExperimental=true)." : "."), index, command);
        if (options.DocumentKind != null && !entry.AppliesTo(options.DocumentKind))
            throw new CadBatchException(InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                at + "'" + command + "' runs in " + string.Join("/", entry.Documents) + " documents, not in a " +
                options.DocumentKind + ".", index, command);
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
