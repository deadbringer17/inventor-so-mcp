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
