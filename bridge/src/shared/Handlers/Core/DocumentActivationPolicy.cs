using System;
using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// Pure decision logic for <c>activate_open_document_xr</c>. A document that is loaded in Inventor
/// but has no window (typical for the referenced definitions of an assembly) makes
/// <c>Document.Activate()</c> fail with E_FAIL. The handler then shows the window of the already
/// loaded document (<c>Documents.Open(fullFileName, true)</c>) and activates again. No Inventor
/// reference here: the unit tests compile this file directly.
/// </summary>
public static class DocumentActivationPolicy
{
    /// <summary>HRESULT E_FAIL, "Errore non specificato".</summary>
    public const int EFail = unchecked((int)0x80004005);

    /// <summary>True when the exception is the E_FAIL COMException Inventor raises for a window-less document.</summary>
    public static bool IsNoWindowFailure(Exception? error)
        => error is COMException com && com.ErrorCode == EFail;

    /// <summary>
    /// Whether to retry by showing the document window: Activate threw E_FAIL, or it returned but the
    /// active document is still not the requested one. Never retried when the document is already the
    /// active one, and a missing active id (null) counts as a mismatch.
    /// </summary>
    public static bool ShouldShowWindowAndRetry(Exception? activateError, string requestedId, string? activeIdAfterActivate)
    {
        if (activateError != null) return IsNoWindowFailure(activateError);
        return !string.Equals(requestedId, activeIdAfterActivate, StringComparison.Ordinal);
    }

    /// <summary>A usable file name is required to show the window of a loaded document.</summary>
    public static bool HasFileName(string? fullFileName) => !string.IsNullOrWhiteSpace(fullFileName);

    public const string NoWindowNoFileMessage = "The document has no window and no file name, so it cannot be shown for activation.";
}
