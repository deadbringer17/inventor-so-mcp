using InventorXrSo.Core.Pairing;

namespace InventorXrSo.Core.Tests.Pairing;

public class PairingPayloadTests
{
    private static readonly string Sha = new string('a', 64);

    [Fact]
    public void ParsesTheServerQrPayload()
    {
        var p = PairingPayload.Parse("{\"v\":1,\"host\":\"192.168.1.20\",\"port\":8443,\"ott\":\"secret\",\"cert_sha256\":\"" + Sha + "\"}");
        Assert.Equal("192.168.1.20", p.Host);
        Assert.Equal(8443, p.Port);
        Assert.Equal("secret", p.OneTimeToken);
        Assert.Equal(Sha, p.CertSha256);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"v\":2,\"host\":\"h\",\"port\":1,\"ott\":\"s\",\"cert_sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}")]
    [InlineData("{\"v\":1,\"port\":1,\"ott\":\"s\",\"cert_sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}")]
    [InlineData("{\"v\":1,\"host\":\"h\",\"port\":70000,\"ott\":\"s\",\"cert_sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}")]
    [InlineData("{\"v\":1,\"host\":\"h\",\"port\":1,\"ott\":\"s\",\"cert_sha256\":\"abc\"}")]
    public void RejectsAnythingElse(string text) => Assert.Throws<FormatException>(() => PairingPayload.Parse(text));

    [Fact]
    public void PairedServerRoundTripsAndBracketsIpv6()
    {
        var server = new PairedServer("fe80::1", 8443, Sha, "quest3", "token-value-token-value-token-value");
        Assert.Equal("https://[fe80::1]:8443", server.BaseUrl);
        var back = PairedServer.FromJson(server.ToJson());
        Assert.Equal(server.Host, back.Host);
        Assert.Equal(server.Port, back.Port);
        Assert.Equal(server.CertSha256, back.CertSha256);
        Assert.Equal(server.ClientName, back.ClientName);
        Assert.Equal(server.Token, back.Token);
        Assert.Throws<FormatException>(() => PairedServer.FromJson("{}"));
    }
}
