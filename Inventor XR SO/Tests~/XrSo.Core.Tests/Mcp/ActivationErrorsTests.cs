using System;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;
using Xunit;

namespace InventorXrSo.Core.Tests.Mcp;

public class ActivationErrorsTests
{
    private static McpToolException Tool(string tool, string code, string message = "Errore non specificato")
        => new(tool, code, message, new JObject());

    [Fact]
    public void ApiErrorFromActivationGetsTheItalianExplanationAndKeepsCodeAndText()
    {
        var text = ActivationErrors.Describe(Tool(ActivationErrors.ActivateTool, "API_ERROR"));
        Assert.StartsWith("Inventor non riesce ad attivare il documento. Aprilo in una finestra in Inventor e riprova.", text);
        Assert.Contains("API_ERROR: Errore non specificato", text);
    }

    [Theory]
    [InlineData("inventor_activate_open_document_xr", "NO_DOCUMENT")]
    [InlineData("inventor_inspect_xr", "API_ERROR")]
    public void OtherToolErrorsKeepTheirOwnMessage(string tool, string code)
    {
        var error = Tool(tool, code, "originale");
        Assert.False(ActivationErrors.IsActivationApiError(error));
        Assert.Equal("originale", ActivationErrors.Describe(error));
    }

    [Fact]
    public void NonToolExceptionsKeepTheirMessage() => Assert.Equal("boom", ActivationErrors.Describe(new InvalidOperationException("boom")));
}
