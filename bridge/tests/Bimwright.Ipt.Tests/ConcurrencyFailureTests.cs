using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// The three refusals every safe write shares. Each one used to travel as the MESSAGE of a generic
/// INVALID_ARGUMENT, so these assert the split that replaced it: the identity in the code, the
/// specifics in the details, and nothing a caller has to parse out of the sentence.
/// </summary>
public sealed class ConcurrencyFailureTests
{
    [Fact]
    public void DocumentChangedCarriesBothDocumentIds()
    {
        var failure = ConcurrencyFailure.DocumentChanged("doc_a", "doc_b");

        Assert.Equal(InventorErrorCodes.DOCUMENT_CHANGED, failure.Code);
        Assert.Equal("doc_a", (string?)failure.Details?["expected_document_id"]);
        Assert.Equal("doc_b", (string?)failure.Details?["active_document_id"]);
        Assert.Null(failure.Details?["phase"]);
        Assert.DoesNotContain(failure.Code, failure.Message);
    }

    [Fact]
    public void DocumentChangedNamesThePhaseWhenNoticedMidOperation()
    {
        var failure = ConcurrencyFailure.DocumentChanged("doc_a", null, "the diff");

        Assert.Equal(InventorErrorCodes.DOCUMENT_CHANGED, failure.Code);
        Assert.Equal("the diff", (string?)failure.Details?["phase"]);
        Assert.Null(failure.Details?["active_document_id"]);
    }

    [Fact]
    public void DependencyChangedNamesTheDocumentThatMoved()
    {
        var failure = ConcurrencyFailure.DependencyChanged("doc_ref", "the plan");

        Assert.Equal(InventorErrorCodes.DOCUMENT_CHANGED, failure.Code);
        Assert.Equal("doc_ref", (string?)failure.Details?["changed_document_id"]);
    }

    [Fact]
    public void StaleRevisionCarriesBothRevisions()
    {
        var failure = ConcurrencyFailure.StaleRevision("r1", "r2");

        Assert.Equal(InventorErrorCodes.STALE_REVISION, failure.Code);
        Assert.Equal("r1", (string?)failure.Details?["expected_revision"]);
        Assert.Equal("r2", (string?)failure.Details?["actual_revision"]);
        Assert.DoesNotContain(failure.Code, failure.Message);
    }

    [Fact]
    public void TransactionBusyIsItsOwnCode()
    {
        var failure = ConcurrencyFailure.TransactionBusy();

        Assert.Equal(InventorErrorCodes.TRANSACTION_BUSY, failure.Code);
        Assert.Null(failure.Details);
        Assert.DoesNotContain(failure.Code, failure.Message);
    }

    [Theory]
    [InlineData("CLEARANCE_FAILED: 3.2", "CLEARANCE_FAILED")]
    [InlineData("INTERFERENCE", null)]                        // no colon, nothing to lift
    [InlineData("Assembly rebuild failed.", null)]
    [InlineData(@"failed at C:\models\a.iam", null)]          // a drive letter is not a code
    public void LiftCodeReadsTheProseTagThrowSitesStillUse(string message, string? expected)
        => Assert.Equal(expected, CodedFailureException.LiftCode(new InvalidOperationException(message)));

    [Fact]
    public void LiftCodePrefersTheStructuralCode()
    {
        var coded = new CodedFailureException(InventorErrorCodes.ROLLED_BACK, "Nothing to parse here");

        Assert.Equal(InventorErrorCodes.ROLLED_BACK, CodedFailureException.LiftCode(coded));
        Assert.Equal(new JObject { ["reason"] = InventorErrorCodes.ROLLED_BACK }.ToString(),
            CodedFailureException.ReasonOf(coded)!.ToString());
    }

    [Fact]
    public void ReasonOfIsNullWhenThereIsNoCodeToLift()
        => Assert.Null(CodedFailureException.ReasonOf(new InvalidOperationException("Assembly rebuild failed.")));
}
