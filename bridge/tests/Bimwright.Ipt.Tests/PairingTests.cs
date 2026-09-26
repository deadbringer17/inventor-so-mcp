using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Xunit;
using System.Security.Cryptography;

namespace Bimwright.Ipt.Tests;

public sealed class PairingStoreTests
{
    private DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private PairingStore Store() => new(() => _now);

    [Fact]
    public void OneTimeTokenPairsExactlyOnce()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        var first = store.Redeem(window.OneTimeToken);
        Assert.True(first.Ok);
        Assert.Equal("quest3", first.ClientName);
        Assert.Equal(PairingStore.Used, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void SixDigitCodePairsToo()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        Assert.Matches("^[0-9]{6}$", window.Code);
        Assert.True(store.Redeem(window.Code).Ok);
    }

    [Fact]
    public void ExpiredWindowIsRefused()
    {
        var store = Store();
        var window = store.Open("quest3", TimeSpan.FromMinutes(2));
        _now += TimeSpan.FromMinutes(2);
        Assert.Equal(PairingStore.Expired, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void FiveWrongSecretsCloseTheWindow()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        for (int i = 0; i < PairingStore.MaxFailures - 1; i++)
            Assert.Equal(PairingStore.Invalid, store.Redeem("not-a-code").ErrorCode);
        Assert.Equal(PairingStore.Expired, store.Redeem("not-a-code").ErrorCode);
        Assert.Equal(PairingStore.Expired, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void NoWindowMeansExpired() => Assert.Equal(PairingStore.Expired, Store().Redeem("anything").ErrorCode);

    [Fact]
    public void BadClientNameIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Store().Open("bad name", PairingStore.DefaultTtl));

    [Fact]
    public void ConcurrentRedeemsPairExactlyOnce()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        var outcomes = Enumerable.Range(0, 32).AsParallel().Select(_ => store.Redeem(window.OneTimeToken)).ToList();
        Assert.Single(outcomes, o => o.Ok);
    }

    [Fact]
    public void ReopeningInvalidatesThePreviousSecrets()
    {
        var store = Store();
        var old = store.Open("quest3", PairingStore.DefaultTtl);
        store.Open("quest3", PairingStore.DefaultTtl);
        Assert.Equal(PairingStore.Invalid, store.Redeem(old.OneTimeToken).ErrorCode);
    }
}

public sealed class SelfSignedCertificateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-cert-" + Guid.NewGuid().ToString("N"));
    private string Pfx => Path.Combine(_dir, "server.pfx");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void CreatesAServerCertificateWithKeyAndSubjectAltNames()
    {
        using var cert = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost", "127.0.0.1", "192.168.1.20" });
        Assert.True(cert.HasPrivateKey);
        Assert.True(File.Exists(Pfx));
        var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains(IPAddress.Parse("192.168.1.20"), san.EnumerateIPAddresses());
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Matches("^[0-9a-f]{64}$", SelfSignedCertificate.Sha256Hex(cert));
    }

    [Fact]
    public void ReloadKeepsTheFingerprintEvenIfTheHostsChange()
    {
        string first, second;
        using (var a = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" })) first = SelfSignedCertificate.Sha256Hex(a);
        using (var b = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost", "10.0.0.9" })) second = SelfSignedCertificate.Sha256Hex(b);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AnExpiringCertificateIsReplaced()
    {
        string first;
        var longAgo = DateTimeOffset.UtcNow - SelfSignedCertificate.Validity + TimeSpan.FromDays(10);
        using (var old = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" }, longAgo)) first = SelfSignedCertificate.Sha256Hex(old);
        using var renewed = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" });
        Assert.NotEqual(first, SelfSignedCertificate.Sha256Hex(renewed));
    }

    [Fact]
    public void DisplayGroupsByFourUppercase() => Assert.Equal("ABCD 0123", SelfSignedCertificate.Display("abcd0123"));
}

public sealed class TokenFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-tokens-" + Guid.NewGuid().ToString("N"));
    private string File1 => Path.Combine(_dir, "sub", "tokens.txt");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AppendCreatesTheFileAndLoadReadsIt()
    {
        var token = TokenRegistry.Generate();
        TokenFile.Append(File1, "quest3", token);
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal("quest3", registry.Authenticate(token));
    }

    [Fact]
    public void AppendAfterALineWithoutNewlineStartsANewLine()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        var first = TokenRegistry.Generate();
        System.IO.File.WriteAllText(File1, "laptop:" + first);
        var second = TokenRegistry.Generate();
        TokenFile.Append(File1, "quest3", second);
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal(new[] { "laptop", "quest3" }, registry.Names);
    }

    [Fact]
    public void AddWhileAuthenticatingIsSafe()
    {
        var registry = new TokenRegistry();
        var known = TokenRegistry.Generate();
        registry.Add("known", known);
        Parallel.For(0, 200, i =>
        {
            if (i % 2 == 0) registry.Add("c" + i, TokenRegistry.Generate());
            else Assert.Equal("known", registry.Authenticate(known));
        });
        Assert.Equal(101, registry.Count);
    }
}
