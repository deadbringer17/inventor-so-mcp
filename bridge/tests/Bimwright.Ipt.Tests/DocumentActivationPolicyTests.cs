using System;
using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Handlers.Core;

namespace Bimwright.Ipt.Tests;

public class DocumentActivationPolicyTests
{
    private static readonly COMException EFail = new("Errore non specificato", DocumentActivationPolicy.EFail);

    [Fact]
    public void EFailComExceptionIsTheNoWindowFailure() => Assert.True(DocumentActivationPolicy.IsNoWindowFailure(EFail));

    [Fact]
    public void OtherErrorsAreNotTheNoWindowFailure()
    {
        Assert.False(DocumentActivationPolicy.IsNoWindowFailure(new COMException("x", unchecked((int)0x80004002))));
        Assert.False(DocumentActivationPolicy.IsNoWindowFailure(new InvalidOperationException("x")));
        Assert.False(DocumentActivationPolicy.IsNoWindowFailure(null));
    }

    [Fact]
    public void RetriesAfterEFail() => Assert.True(DocumentActivationPolicy.ShouldShowWindowAndRetry(EFail, "doc_a", null));

    [Fact]
    public void DoesNotRetryOtherErrors()
        => Assert.False(DocumentActivationPolicy.ShouldShowWindowAndRetry(new InvalidOperationException("x"), "doc_a", null));

    [Fact]
    public void NoRetryWhenTheRequestedDocumentIsActive()
        => Assert.False(DocumentActivationPolicy.ShouldShowWindowAndRetry(null, "doc_a", "doc_a"));

    [Theory]
    [InlineData("doc_b")]
    [InlineData(null)]
    [InlineData("DOC_A")]
    public void RetriesWhenActiveDocumentIsStillAnotherOne(string? active)
        => Assert.True(DocumentActivationPolicy.ShouldShowWindowAndRetry(null, "doc_a", active));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(@"C:\Work\APE-A-0112.iam", true)]
    public void NeedsAFileNameToShowTheWindow(string? name, bool expected)
        => Assert.Equal(expected, DocumentActivationPolicy.HasFileName(name));

    [Fact]
    public void HandlerShowsTheWindowOfALoadedDocumentWithoutLoadingOrSaving()
    {
        string? root = null;
        for (var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "src", "shared"))) { root = d.FullName; break; }
        Assert.NotNull(root);
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(root!, "src", "shared", "Handlers", "Experimental", "InspectXrHandlers.cs"));
        var start = text.IndexOf("class ActivateOpenDocumentXrHandler", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = text.Substring(start);
        Assert.Contains("DocumentActivationPolicy.ShouldShowWindowAndRetry", body);
        Assert.Contains("app.Documents.Open(fileName, true)", body);   // OpenVisible = true on the loaded document
        Assert.DoesNotContain(".Save(", body);
        Assert.DoesNotContain(".Close(", body);
        Assert.Contains("ACTIVATE_NOT_CONFIRMED", body);
        Assert.Contains("TransactionBusy", body);
    }
}
