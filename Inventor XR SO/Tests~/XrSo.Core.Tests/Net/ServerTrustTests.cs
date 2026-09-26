using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Net;

public class ServerTrustTests
{
    private static readonly string Pinned = new string('a', 64);
    private static readonly byte[] MatchingDer = { 1, 2, 3 };
    private static readonly byte[] WrongDer = { 4, 5, 6 };

    [Fact]
    public void ValidateWithAMatchingCertificateDoesNotIncrementRejectionCount()
    {
        var trust = ServerTrust.Pinned(CertificatePin.Sha256Hex(MatchingDer));
        Assert.True(trust.Validate(MatchingDer));
        Assert.Equal(0, trust.RejectionCount);
    }

    [Fact]
    public void ValidateWithAWrongCertificateIncrementsRejectionCount()
    {
        var trust = ServerTrust.Pinned(CertificatePin.Sha256Hex(MatchingDer));
        Assert.False(trust.Validate(WrongDer));
        Assert.Equal(1, trust.RejectionCount);
        Assert.Equal(CertificatePin.Sha256Hex(WrongDer), trust.LastPresentedSha256);

        // A second rejection counts again: each request's own rejection is independently observable.
        Assert.False(trust.Validate(WrongDer));
        Assert.Equal(2, trust.RejectionCount);
    }

    [Fact]
    public void FirstUseNeverRejectsANonNullCertificate()
    {
        var trust = ServerTrust.FirstUse();
        Assert.True(trust.Validate(MatchingDer));
        Assert.True(trust.Validate(WrongDer));
        Assert.Equal(0, trust.RejectionCount);
    }

    [Fact]
    public void FirstUseStillRejectsANullCertificate()
    {
        var trust = ServerTrust.FirstUse();
        Assert.False(trust.Validate(null));
        Assert.Equal(1, trust.RejectionCount);
        Assert.Null(trust.LastPresentedSha256);
    }

    [Fact]
    public void APinnedTrustRejectsANullCertificate()
    {
        var trust = ServerTrust.Pinned(Pinned);
        Assert.False(trust.Validate(null));
        Assert.Equal(1, trust.RejectionCount);
    }
}
