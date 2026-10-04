using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.Pairing.Control;

namespace Bimwright.Ipt.Tests;

public sealed class PairingControlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-pair-control-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly PairingStore _store;
    private NetworkAddress[] _addresses = [new("192.168.1.20", "Ethernet")];
    private readonly LocalPairingController _controller;
    public PairingControlTests()
    {
        _store = new PairingStore(() => _now);
        var config = new InventorMcpConfig { DescriptorDirectory = _root, TargetId = "2027",
            HttpUrls = ["https://0.0.0.0:8443"], HttpSelfSignedCertificate = true };
        _controller = new LocalPairingController(_store, config, new PluginClient(config), new string('a', 64), () => _addresses);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void StatusNeverCarriesSecretsAndOpenKeepsQrV1()
    {
        var open = _controller.Handle(new ControlRequest(Operation: "open", Host: "192.168.1.20"));
        Assert.True(open.Ok); Assert.Matches("^\\d{6}$", open.Code!);
        using var payload = JsonDocument.Parse(open.QrPayload!);
        Assert.Equal(1, payload.RootElement.GetProperty("v").GetInt32());
        Assert.Equal(8443, payload.RootElement.GetProperty("port").GetInt32());
        Assert.Equal("192.168.1.20", payload.RootElement.GetProperty("host").GetString());
        var status = _controller.Handle(new ControlRequest());
        Assert.Equal(open.WindowId, status.WindowId);
        Assert.Null(status.Code); Assert.Null(status.QrPayload);
        Assert.DoesNotContain(open.Code!, JsonSerializer.Serialize(status));
        Assert.Equal(120, status.RemainingSeconds);
        _now += TimeSpan.FromSeconds(120);
        Assert.Equal("expired", _controller.Handle(new ControlRequest()).State);
    }

    [Fact]
    public void StaleCancelCannotInvalidateNewWindow()
    {
        var old = _controller.Handle(new ControlRequest(Operation: "open", Host: "192.168.1.20"));
        var replacement = _controller.Handle(new ControlRequest(Operation: "open", Host: "192.168.1.20"));
        Assert.Equal("WINDOW_STALE", _controller.Handle(new ControlRequest(Operation: "cancel", WindowId: old.WindowId)).ErrorCode);
        Assert.Equal("waiting", _store.Status().State);
        Assert.True(_store.Redeem(replacement.Code).Ok);
        Assert.Equal("paired", _controller.Handle(new ControlRequest()).State);
    }

    [Fact]
    public void CancellationAndNetworkLossInvalidateBothSecrets()
    {
        var open = _controller.Handle(new ControlRequest(Operation: "open", Host: "192.168.1.20"));
        Assert.Equal("cancelled", _controller.Handle(new ControlRequest(Operation: "cancel", WindowId: open.WindowId)).State);
        Assert.False(_store.Redeem(open.Code).Ok);
        open = _controller.Handle(new ControlRequest(Operation: "open", Host: "192.168.1.20"));
        _addresses = [];
        Assert.Equal("cancelled", _controller.Handle(new ControlRequest()).State);
        using var payload = JsonDocument.Parse(open.QrPayload!);
        Assert.False(_store.Redeem(payload.RootElement.GetProperty("ott").GetString()).Ok);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.99")]
    public void CannotPublishUnassignedAddress(string host) =>
        Assert.Equal("NETWORK_UNAVAILABLE", _controller.Handle(new ControlRequest(Operation: "open", Host: host)).ErrorCode);

    [Fact]
    public void PersistenceFailureHasNoActiveTokenAndRequiresNewWindow()
    {
        var registry = new TokenRegistry();
        var token = TokenRegistry.Generate();
        var window = _store.Open("quest", PairingStore.DefaultTtl);
        var result = _store.Redeem(window.Code, name => registry.AddPersisted(name, token, () => throw new IOException()));
        Assert.Equal("PAIRING_PERSIST_FAILED", result.ErrorCode);
        Assert.Equal(0, registry.Count); Assert.Null(registry.Authenticate(token));
        Assert.Equal("failed", _store.Status().State);
        Assert.False(_store.Redeem(window.Code).Ok);
        Assert.True(_store.Redeem(_store.Open("next", PairingStore.DefaultTtl).Code).Ok);
    }

    [Fact]
    public async Task RegenerationWaitsForDurableRedemptionAndCannotBeConsumedByOldCompletion()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var old = _store.Open("old", PairingStore.DefaultTtl);
        var redeem = Task.Run(() => _store.Redeem(old.Code, _ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var open = Task.Run(() => _store.Open("next", PairingStore.DefaultTtl));
        try { Assert.False(open.IsCompleted); }
        finally { release.Set(); }
        Assert.True((await redeem).Ok);
        var replacement = await open;
        Assert.Equal("waiting", _store.Status().State);
        Assert.Equal(replacement.Id, _store.Status().Window!.Id);
        Assert.True(_store.Redeem(replacement.Code).Ok);
    }

    [Fact]
    public async Task OwnerPipeServesMultipleRequestsAndRejectsUnknownProtocol()
    {
        var name = "InventorSO.Test." + Guid.NewGuid().ToString("N");
        await using var server = new ControlPipeServer(name, _controller.Handle);
        for (var i = 0; i < 5; i++)
            Assert.True((await ControlWire.CallAsync(name, new ControlRequest(), CancellationToken.None)).Ok);
        var unsupported = await ControlWire.CallAsync(name, new ControlRequest(Version: 2), CancellationToken.None);
        Assert.Equal("PROTOCOL_VERSION", unsupported.ErrorCode);
        Assert.Equal("UNKNOWN_OPERATION", (await ControlWire.CallAsync(name, new ControlRequest(Operation: "remove"), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task OversizedFrameIsDiscardedAndNextClientStillWorks()
    {
        var name = "InventorSO.Test." + Guid.NewGuid().ToString("N");
        await using var server = new ControlPipeServer(name, _controller.Handle);
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await client.ConnectAsync(2000);
            var bytes = Encoding.UTF8.GetBytes(new string('x', ControlWire.MaxFrameBytes + 1) + "\n");
            try { await client.WriteAsync(bytes); } catch (IOException) { }
        }
        Assert.True((await ControlWire.CallAsync(name, new ControlRequest(), CancellationToken.None)).Ok);
    }

    [Fact]
    public void ProtectedDirectoryRestrictsAccessToOwnerOnWindows()
    {
        OwnerStorage.ProtectDirectory(_root);
        if (!OperatingSystem.IsWindows()) return;
        var security = System.IO.FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(_root));
        Assert.True(security.AreAccessRulesProtected);
        var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
        var owner = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules) Assert.Equal(owner, rule.IdentityReference);
    }
}
