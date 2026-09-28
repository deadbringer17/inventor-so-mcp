using InventorXrSo.Core.Pairing;

namespace InventorXrSo.Core.Tests.Pairing;

public class CredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xrso-cred-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "pairing.dat");
    private static readonly PairedServer Server = new PairedServer("192.168.1.20", 8443, new string('a', 64), "quest3", "secret-token-secret-token-secret-token");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Reversing : IProtector
    {
        public string Protect(string plain) => new string(plain.Reverse().ToArray());
        public string Unprotect(string text) => new string(text.Reverse().ToArray());
    }

    [Fact]
    public void SavesProtectedAndLoadsBack()
    {
        var store = new FileCredentialStore(FilePath, new Reversing());
        store.Save(Server);
        Assert.DoesNotContain("secret-token", File.ReadAllText(FilePath));
        var loaded = new FileCredentialStore(FilePath, new Reversing()).Load();
        Assert.Equal(Server.Token, loaded.Token);
        Assert.Equal(Server.CertSha256, loaded.CertSha256);
    }

    [Fact]
    public void NothingSavedLoadsNull() => Assert.Null(new FileCredentialStore(FilePath, new PlainProtector()).Load());

    [Fact]
    public void SavingAgainReplacesTheProtectedFile()
    {
        var store = new FileCredentialStore(FilePath, new Reversing());
        store.Save(Server);
        var replacement = new PairedServer("pc.local", 9443, new string('b', 64), "quest3", "replacement-token");
        store.Save(replacement);
        Assert.Equal("replacement-token", store.Load().Token);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void AnUnreadableFileIsForgotten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "garbage");
        Assert.Null(new FileCredentialStore(FilePath, new PlainProtector()).Load());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ClearRemovesThePairing()
    {
        var store = new FileCredentialStore(FilePath, new PlainProtector());
        store.Save(Server);
        store.Clear();
        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", 8443)]
    [InlineData("192.168.1.20:9443", "192.168.1.20", 9443)]
    [InlineData(" pc.local:8443 ", "pc.local", 8443)]
    [InlineData("[fe80::1]:8443", "fe80::1", 8443)]
    public void AddressesParse(string text, string host, int port)
    {
        Assert.True(PairingAddress.TryParse(text, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData(":8443")]
    [InlineData("host:0")]
    [InlineData("host:99999")]
    [InlineData("host:abc")]
    [InlineData("https://host")]
    [InlineData("host/path")]
    [InlineData("user@host")]
    [InlineData("host?query")]
    [InlineData("a b")]
    [InlineData("[host]:8443")]
    [InlineData("::1")]
    [InlineData("host:+8443")]
    public void BadAddressesDoNot(string text) => Assert.False(PairingAddress.TryParse(text, out _, out _));
}
