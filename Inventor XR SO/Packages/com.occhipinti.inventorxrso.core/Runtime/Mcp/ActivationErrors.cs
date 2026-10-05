using System;

namespace InventorXrSo.Core.Mcp
{
    /// <summary>
    /// Italian user message for a failed document activation. Inventor answers API_ERROR when the
    /// document is loaded but has no window and cannot be shown; the raw code and text stay available
    /// in the detail line instead of replacing the explanation.
    /// </summary>
    public static class ActivationErrors
    {
        public const string ActivateTool = "inventor_activate_open_document_xr";
        public const string NoWindowMessage = "Inventor non riesce ad attivare il documento. Aprilo in una finestra in Inventor e riprova.";

        public static bool IsActivationApiError(Exception error)
            => error is McpToolException tool && tool.Tool == ActivateTool && tool.Code == "API_ERROR";

        /// <summary>Message for the user: the Italian explanation, a newline and "CODE: original text"; any other error keeps its own message.</summary>
        public static string Describe(Exception error)
        {
            if (error == null) return "";
            if (!IsActivationApiError(error)) return error.Message;
            var tool = (McpToolException)error;
            string detail = tool.Code + ": " + tool.Message;
            if (detail.Length > 400) detail = detail.Substring(0, 400) + "…";
            return NoWindowMessage + "\n" + detail;
        }
    }
}
