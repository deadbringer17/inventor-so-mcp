using Inventor.So.Mcp.Http.Pairing;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Pairing;

public class PairingClientTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    private readonly PairingClient _client = new PairingClient(trust => new SystemHttpTransport(trust));

    public PairingClientTests(BackendFixture fixture) { _f = fixture; }

    private PairingPayload Payload(PairingWindow window, string sha = null) =>
        PairingPayload.Parse(PairingSetup.QrPayload(_f.Base.Host, _f.Base.Port, window.OneTimeToken, sha ?? _f.CertSha256));

    [Fact]
    public async Task QrPayloadPairsWithThePinnedCertificate()
    {
        var window = _f.Pairing.Open("quest-qr", TimeSpan.FromMinutes(1));
        var server = await _client.PairWithQrAsync(Payload(window), "Quest 3", CancellationToken.None);
        Assert.Equal("quest-qr", server.ClientName);
        Assert.Equal(_f.CertSha256, server.CertSha256);
        Assert.Equal("quest-qr", _f.Tokens.Authenticate(server.Token));
    }

    [Fact]
    public async Task AWrongFingerprintIsRefusedBeforeTheSecretIsSent()
    {
        var window = _f.Pairing.Open("quest-mitm", TimeSpan.FromMinutes(1));
        var rejected = await Assert.ThrowsAsync<CertificateRejectedException>(() =>
            _client.PairWithQrAsync(Payload(window, new string('0', 64)), "Quest 3", CancellationToken.None));
        Assert.Equal(_f.CertSha256, rejected.PresentedSha256);
        // The secret never reached the server, so it still pairs.
        Assert.Equal("quest-mitm", (await _client.PairWithQrAsync(Payload(window), "Quest 3", CancellationToken.None)).ClientName);
    }

    [Fact]
    public async Task ManualPathProbesTheFingerprintThenPairsPinned()
    {
        var window = _f.Pairing.Open("quest-manual", TimeSpan.FromMinutes(1));
        var sha = await _client.ProbeFingerprintAsync(_f.Base.Host, _f.Base.Port, CancellationToken.None);
        Assert.Equal(_f.CertSha256, sha);
        var server = await _client.PairAsync(_f.Base.Host, _f.Base.Port, window.Code, ServerTrust.Pinned(sha), "Quest 3", CancellationToken.None);
        Assert.Equal("quest-manual", server.ClientName);
    }

    [Fact]
    public async Task AWrongCodeSurfacesTheServerErrorCode()
    {
        _f.Pairing.Open("quest-wrong", TimeSpan.FromMinutes(1));
        var ex = await Assert.ThrowsAsync<PairingException>(() =>
            _client.PairAsync(_f.Base.Host, _f.Base.Port, "not-the-code", ServerTrust.Pinned(_f.CertSha256), "Quest 3", CancellationToken.None));
        Assert.Equal("PAIRING_INVALID", ex.Code);
    }
}
