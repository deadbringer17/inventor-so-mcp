using System.Drawing;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.Pairing.Control;
using Inventor.So.Pairing.Desktop;

namespace Inventor.So.Pairing.Desktop.Tests;

public sealed class PairingWindowTests
{
    [Fact]
    public async Task BackgroundPollingKeepsNetworkDropdownEnabledAndOpen()
    {
        var polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollCount = 0;
        var status = new ControlResponse { Addresses = [new("192.168.1.20", "Ethernet"), new("192.168.2.20", "Wi-Fi")], Port = 8443 };
        async Task<ControlResponse> Request(ControlRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref pollCount);
            polling.TrySetResult();
            await releasePoll.Task.WaitAsync(ct);
            // A new adapter must be deferred until the user's dropdown interaction ends.
            return status with { Addresses = [new("192.168.1.20", "Ethernet"), new("192.168.2.20", "Wi-Fi"), new("192.168.3.20", "New adapter")] };
        }
        try
        {
            await RunWindowAsync(() => new PairingForm(null, Request, _ => Task.FromResult<ControlResponse?>(status), listenForActivation: false), async form =>
            {
                var addresses = Find<ComboBox>(form, "NetworkAddress");
                await UntilAsync(() => addresses.Items.Count == 2 && addresses.Enabled);
                var disabled = 0;
                addresses.EnabledChanged += (_, _) => { if (!addresses.Enabled) disabled++; };
                await polling.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(addresses.Enabled); // Also while a slow background request is pending.
                addresses.DroppedDown = true;
                releasePoll.TrySetResult();
                await Task.Delay(2200); // Allow the pending response and two subsequent timer ticks.
                Assert.True(addresses.DroppedDown);
                Assert.Equal(2, addresses.Items.Count);
                Assert.True(pollCount >= 2); // Status and expiry keep updating while the menu is open.
                Assert.Equal(0, disabled);
                addresses.DroppedDown = false;
                addresses.SelectedIndex = 1;
                await UntilAsync(() => Find<Button>(form, "GenerateCode").Enabled);
                await UntilAsync(() => addresses.Items.Count == 3);
                Assert.Equal("192.168.2.20", ((NetworkAddress)addresses.SelectedItem!).Address);
            });
        }
        finally { releasePoll.TrySetResult(); }
    }

    [Fact]
    public async Task NetworkChoiceIsExplicitAndChangingItCancelsPreviousCode()
    {
        var root = Path.Combine(Path.GetTempPath(), "so-pair-ui-" + Guid.NewGuid().ToString("N"));
        var store = new PairingStore(() => DateTimeOffset.UtcNow);
        var config = new InventorMcpConfig { DescriptorDirectory = Path.Combine(root, "targets"), TargetId = "2027",
            HttpUrls = ["https://0.0.0.0:8443"] };
        var controller = new LocalPairingController(store, config, new PluginClient(config), new string('c', 64),
            () => [new NetworkAddress("192.168.1.20", "Ethernet"), new NetworkAddress("192.168.2.20", "Wi-Fi")]);
        var pipe = "InventorSO.UI.Test." + Guid.NewGuid().ToString("N");
        await using var server = new ControlPipeServer(pipe, controller.Handle);
        Task<ControlResponse> Request(ControlRequest request, CancellationToken ct) => ControlWire.CallAsync(pipe, request, ct);
        async Task<ControlResponse?> Probe(CancellationToken ct) => await Request(new ControlRequest(), ct);
        try
        {
            await RunWindowAsync(() => new PairingForm("inventor-2027-other", Request, Probe, root, listenForActivation: false), async form =>
            {
                var addresses = Find<ComboBox>(form, "NetworkAddress");
                var generate = Find<Button>(form, "GenerateCode");
                await UntilAsync(() => addresses.Items.Count == 2);
                Assert.Equal(-1, addresses.SelectedIndex); Assert.False(generate.Enabled);
                Assert.Contains("inventor-2027-other", Find<Label>(form, "CallerTargetHint").Text);
                Assert.Equal("2027", (await Request(new ControlRequest(), CancellationToken.None)).TargetRule);
                addresses.SelectedIndex = 0;
                await UntilAsync(() => generate.Enabled); generate.PerformClick();
                await UntilAsync(() => Find<PictureBox>(form, "PairingQr").Image != null);
                var old = store.Status().Window!;
                addresses.SelectedIndex = 1;
                await UntilAsync(() => store.Status().State == "cancelled");
                Assert.Null(Find<PictureBox>(form, "PairingQr").Image);
                Assert.False(store.Redeem(old.OneTimeToken).Ok);
                Assert.Equal("192.168.2.20:8443", Find<Label>(form, "ManualAddress").Text);
                Assert.Equal("192.168.2.20", File.ReadAllText(Path.Combine(root, "last-address.txt")));
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RealWindowGeneratesRegeneratesAndCancelsThroughOwnerPipe()
    {
        var root = Path.Combine(Path.GetTempPath(), "so-pair-ui-" + Guid.NewGuid().ToString("N"));
        var store = new PairingStore(() => DateTimeOffset.UtcNow);
        var config = new InventorMcpConfig { DescriptorDirectory = Path.Combine(root, "targets"), TargetId = "2027",
            HttpUrls = ["https://0.0.0.0:8443"], HttpSelfSignedCertificate = true };
        var controller = new LocalPairingController(store, config, new PluginClient(config), new string('a', 64),
            () => [new NetworkAddress("192.168.1.20", "Test Ethernet")]);
        var pipe = "InventorSO.UI.Test." + Guid.NewGuid().ToString("N");
        await using var server = new ControlPipeServer(pipe, controller.Handle);
        Task<ControlResponse> Request(ControlRequest request, CancellationToken ct) => ControlWire.CallAsync(pipe, request, ct);
        async Task<ControlResponse?> Probe(CancellationToken ct) => await Request(new ControlRequest(), ct);
        try
        {
            await RunWindowAsync(() => new PairingForm(null, Request, Probe, root, listenForActivation: false), async form =>
            {
                var generate = Find<Button>(form, "GenerateCode");
                var code = Find<Label>(form, "ManualCode");
                var qr = Find<PictureBox>(form, "PairingQr");
                await UntilAsync(() => generate.Enabled);
                Assert.Equal("ready", store.Status().State); // Opening the window must not open pairing.
                generate.PerformClick();
                await UntilAsync(() => qr.Image != null);
                var old = store.Status().Window!;
                Assert.Equal(old.Code.Insert(3, " "), code.Text);
                Assert.Equal("192.168.1.20:8443", Find<Label>(form, "ManualAddress").Text);
                Assert.Equal(16, Find<TextBox>(form, "CertificateFingerprint").Text.Split(' ').Length);
                generate.PerformClick();
                await UntilAsync(() => store.Status().Window!.Id != old.Id && qr.Image != null);
                Assert.False(store.Redeem(old.OneTimeToken).Ok);
                var next = store.Status().Window!;
                Assert.Equal(next.Code.Insert(3, " "), code.Text);
                // Optional visual evidence from an isolated test window; code is invalidated below.
                var evidence = Environment.GetEnvironmentVariable("INVENTOR_SO_PAIRING_TEST_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(evidence, "pairing-window.png"));
                }
                Find<Button>(form, "CancelPairing").PerformClick();
                await UntilAsync(() => store.Status().State == "cancelled");
                Assert.Null(qr.Image);
                Assert.False(store.Redeem(next.Code).Ok);
                await UntilAsync(() => generate.Enabled);
                generate.PerformClick(); await UntilAsync(() => qr.Image != null);
                form.Close(); // Awaited close handler invalidates the active window; server stays alive.
            });
            Assert.Equal("cancelled", store.Status().State);
            Assert.True((await Request(new ControlRequest(), CancellationToken.None)).Ok);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExpiryRemovesQrAndCodeWithoutAutomaticRegeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), "so-pair-ui-" + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow;
        var store = new PairingStore(() => now);
        var config = new InventorMcpConfig { DescriptorDirectory = root, HttpUrls = ["https://0.0.0.0:8443"] };
        var controller = new LocalPairingController(store, config, new PluginClient(config), new string('b', 64),
            () => [new NetworkAddress("192.168.1.20", "Test Ethernet")]);
        var pipe = "InventorSO.UI.Test." + Guid.NewGuid().ToString("N");
        await using var server = new ControlPipeServer(pipe, controller.Handle);
        Task<ControlResponse> Request(ControlRequest request, CancellationToken ct) => ControlWire.CallAsync(pipe, request, ct);
        async Task<ControlResponse?> Probe(CancellationToken ct) => await Request(new ControlRequest(), ct);
        await RunWindowAsync(() => new PairingForm(null, Request, Probe, root, listenForActivation: false), async form =>
        {
            await UntilAsync(() => Find<Button>(form, "GenerateCode").Enabled);
            Find<Button>(form, "GenerateCode").PerformClick();
            await UntilAsync(() => Find<PictureBox>(form, "PairingQr").Image != null);
            var id = store.Status().Window!.Id;
            now += TimeSpan.FromSeconds(121);
            await UntilAsync(() => Find<Label>(form, "PairingStatus").Text == "Codice scaduto");
            Assert.Null(Find<PictureBox>(form, "PairingQr").Image);
            Assert.Equal("Codice non generato", Find<Label>(form, "ManualCode").Text);
            Assert.Equal(id, store.Status().Window!.Id);
        });
    }

    private static T Find<T>(Form form, string name) where T : System.Windows.Forms.Control =>
        (T)form.Controls.Find(name, true).Single();
    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Window did not reach the expected state.");
            await Task.Delay(30);
        }
    }
    private static Task RunWindowAsync(Func<PairingForm> create, Func<PairingForm, Task> exercise)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                using var form = create();
                form.Shown += async (_, _) =>
                {
                    try { await exercise(form); }
                    catch (Exception ex) { failure = ex; }
                    finally { if (!form.IsDisposed) form.Close(); }
                };
                Application.Run(form);
                if (failure != null) result.SetException(failure); else result.SetResult();
            }
            catch (Exception ex) { result.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
