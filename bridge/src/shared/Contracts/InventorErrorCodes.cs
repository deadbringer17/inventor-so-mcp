namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Frozen integration contract: canonical error codes returned in
/// <see cref="InventorError.Code"/> across the server and add-in.
/// </summary>
public static class InventorErrorCodes
{
    public const string NO_TARGET = "NO_TARGET";
    public const string TARGET_UNAVAILABLE = "TARGET_UNAVAILABLE";
    public const string NO_DOCUMENT = "NO_DOCUMENT";
    public const string WRONG_DOCUMENT_TYPE = "WRONG_DOCUMENT_TYPE";
    public const string INVALID_ARGUMENT = "INVALID_ARGUMENT";
    public const string UNSUPPORTED_HOST = "UNSUPPORTED_HOST";
    public const string API_ERROR = "API_ERROR";
    public const string TIMEOUT = "TIMEOUT";
    public const string RESPONSE_TOO_LARGE = "RESPONSE_TOO_LARGE";
    public const string READ_ONLY = "READ_ONLY";
    public const string ATOMIC_REQUIRED = "ATOMIC_REQUIRED";
    public const string SEND_CODE_DISABLED = "SEND_CODE_DISABLED";
    public const string UNAUTHORIZED = "UNAUTHORIZED";

    // Atomic-batch outcomes. Previously these travelled only as a prefix inside an API_ERROR
    // message, so a caller had to parse prose to tell a stale plan from a failed step.
    public const string STALE_REVISION = "STALE_REVISION";
    public const string DOCUMENT_CHANGED = "DOCUMENT_CHANGED";
    public const string ROLLED_BACK = "ROLLED_BACK";
    public const string ROLLBACK_FAILED = "ROLLBACK_FAILED";

    // Drawing layout outcomes (create_drawing_safe). Previously these also travelled only as a
    // prefix inside an API_ERROR message from a thrown exception; a caller could not tell a
    // layout failure from a genuine Inventor API error without parsing prose.
    public const string NO_FITTING_SCALE = "NO_FITTING_SCALE";
    public const string VIEW_OUTSIDE_LAYOUT = "VIEW_OUTSIDE_LAYOUT";
    public const string VIEW_OVERLAP = "VIEW_OVERLAP";
    public const string PROJECTION_UNAVAILABLE = "PROJECTION_UNAVAILABLE";

    // Company drawing template outcomes (create_drawing_safe with `template`). Each one is a
    // different thing for the caller to fix - install the file, write the sidecar, correct the
    // sidecar, pair it with the right template - so they are distinct codes rather than one
    // TEMPLATE_ERROR whose message would have to be parsed to tell them apart.
    public const string TEMPLATE_NOT_FOUND = "TEMPLATE_NOT_FOUND";
    public const string TEMPLATE_MANIFEST_MISSING = "TEMPLATE_MANIFEST_MISSING";
    public const string TEMPLATE_MANIFEST_INVALID = "TEMPLATE_MANIFEST_INVALID";
    public const string TEMPLATE_SHEET_MISMATCH = "TEMPLATE_SHEET_MISMATCH";
    public const string TEMPLATE_UNUSABLE = "TEMPLATE_UNUSABLE";
    public const string TITLE_BLOCK_FIELD_REJECTED = "TITLE_BLOCK_FIELD_REJECTED";

    // A document already owned by another transaction. Every safe write probes for this before it
    // starts one of its own, and used to report it the same way: the identifier as the MESSAGE of a
    // generic INVALID_ARGUMENT, which a caller cannot branch on. It is a distinct, retryable
    // outcome - nothing was changed - so it is a code of its own.
    public const string TRANSACTION_BUSY = "TRANSACTION_BUSY";
}
