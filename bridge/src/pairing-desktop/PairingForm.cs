using Inventor.So.Pairing.Control;
using QRCoder;

namespace Inventor.So.Pairing.Desktop;

internal sealed class PairingForm : Form
{
    private readonly Func<ControlRequest, CancellationToken, Task<ControlResponse>> _request;
    private readonly Func<CancellationToken, Task<ControlResponse?>> _probe;
    private readonly string _profileDirectory;
    private readonly bool _listenForActivation;
    private readonly Label _status = new() { AutoSize = true, Text = "Avvio server…", Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly Label _inventor = new() { AutoSize = true };
    private readonly Label _caller = new() { AutoSize = true, ForeColor = Color.DarkOrange };
    private readonly ComboBox _targets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _addresses = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _endpoint = new() { AutoSize = true, Text = "Seleziona un indirizzo LAN." };
    private readonly PictureBox _qr = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, AccessibleName = "QR per associare il visore" };
    private readonly Label _code = new() { AutoSize = true, Text = "Codice non generato", Font = new Font("Segoe UI", 24, FontStyle.Bold) };
    private readonly Label _ttl = new() { AutoSize = true };
    private readonly TextBox _fingerprint = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, Height = 52, ScrollBars = ScrollBars.Vertical, AccessibleName = "Impronta SHA-256 del certificato" };
    private readonly Button _start = new() { AutoSize = true, Text = "Avvia / Riprova" };
    private readonly Button _disconnect = new() { AutoSize = true, Text = "Scollega altri server", Name = "DisconnectServers" };
    private readonly Button _generate = new() { AutoSize = true, Text = "Genera codice", Enabled = false };
    private readonly Button _cancel = new() { AutoSize = true, Text = "Annulla pairing", Enabled = false };
    private readonly Button _copy = new() { AutoSize = true, Text = "Copia indirizzo" };
    private readonly Button _enlarge = new() { AutoSize = true, Text = "Ingrandisci QR", Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private ControlPipeServer? _activation;
    private ControlResponse? _current;
    private string? _windowId;
    private string? _callerTarget;
    private string? _savedHost;
    private string? _displayedHost;
    private bool _applying;
    private bool _closing;
    private bool _allowClose;
    private Form? _zoom;

    public PairingForm(string? callerTarget,
        Func<ControlRequest, CancellationToken, Task<ControlResponse>>? request = null,
        Func<CancellationToken, Task<ControlResponse?>>? probe = null, string? profileDirectory = null, bool listenForActivation = true)
    {
        _request = request ?? ((r, ct) => ControlWire.CallAsync(ControlNames.HostPipe, r, ct));
        _probe = probe ?? HostLauncher.TryStatusAsync;
        _profileDirectory = profileDirectory ?? HostLauncher.ProfileDirectory;
        _listenForActivation = listenForActivation;
        _callerTarget = callerTarget;
        _status.Name = "PairingStatus"; _code.Name = "ManualCode"; _qr.Name = "PairingQr";
        _caller.Name = "CallerTargetHint";
        _generate.Name = "GenerateCode"; _cancel.Name = "CancelPairing"; _addresses.Name = "NetworkAddress";
        _ttl.Name = "PairingHint"; _fingerprint.Name = "CertificateFingerprint";
        _endpoint.Name = "ManualAddress";
        Text = "Inventor SO — Connessione visore";
        Name = "InventorSoPairing";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(720, 830);
        MinimumSize = new Size(620, 750);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 12 };
        for (var i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(i == 5 ? SizeType.Percent : SizeType.AutoSize, i == 5 ? 100 : 0));
        layout.Controls.Add(new Label { Text = "PC: " + Environment.MachineName, AutoSize = true });
        layout.Controls.Add(_status); layout.Controls.Add(_inventor); layout.Controls.Add(_caller);
        var choices = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        choices.Controls.Add(new Label { Text = "Inventor:", AutoSize = true }, 0, 0); choices.Controls.Add(_targets, 1, 0);
        choices.Controls.Add(new Label { Text = "Indirizzo LAN:", AutoSize = true }, 0, 1); choices.Controls.Add(_addresses, 1, 1);
        choices.Controls.Add(new Label { Text = "Sul visore:", AutoSize = true }, 0, 2); choices.Controls.Add(_endpoint, 1, 2);
        layout.Controls.Add(choices); layout.Controls.Add(_qr); layout.Controls.Add(_code); layout.Controls.Add(_ttl);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(640, 0), Text = "Sul visore scegli ‘Associa PC’. Per il codice manuale inserisci indirizzo e porta, confronta l'impronta del certificato e digita le sei cifre. PC e visore devono essere sulla stessa rete." });
        layout.Controls.Add(new Label { AutoSize = true, Text = "Impronta certificato (SHA-256):" });
        layout.Controls.Add(_fingerprint);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([_start, _generate, _cancel, _copy, _enlarge, _disconnect]); layout.Controls.Add(buttons);
        Controls.Add(layout);
        void ResizeText()
        {
            foreach (var label in new[] { _ttl, _caller, _inventor, _endpoint }) label.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 60), 0);
        }
        Resize += (_, _) => ResizeText(); ResizeText();
        _start.Click += async (_, _) => await RunAsync(StartAsync);
        _disconnect.Click += async (_, _) => await RunAsync(DisconnectServersAsync);
        _generate.Click += async (_, _) => await RunAsync(GenerateAsync);
        _cancel.Click += async (_, _) => await RunAsync(CancelAsync);
        _copy.Click += (_, _) =>
        {
            if (_addresses.SelectedItem is NetworkAddress address && _current != null)
                try { Clipboard.SetText(address.Address + ":" + _current.Port); }
                catch (System.Runtime.InteropServices.ExternalException) { _ttl.Text = "Appunti occupati. Riprova."; }
        };
        _enlarge.Click += (_, _) => EnlargeQr();
        _addresses.SelectedIndexChanged += async (_, _) =>
        {
            if (_applying) return;
            await RunAsync(async () =>
            {
                await CancelAsync();
                if (_addresses.SelectedItem is NetworkAddress address)
                {
                    _endpoint.Text = address.Address + ":" + _current?.Port;
                    _savedHost = address.Address;
                    OwnerStorage.ProtectDirectory(_profileDirectory);
                    File.WriteAllText(Path.Combine(_profileDirectory, "last-address.txt"), _savedHost);
                }
            });
        };
        _timer.Tick += async (_, _) => await RunAsync(PollAsync, skipBusy: true);
        Shown += async (_, _) =>
        {
            if (_listenForActivation) _activation = new ControlPipeServer(ControlNames.DesktopPipe, request =>
            {
                if (request.Operation != "activate") return ControlResponse.Error("UNKNOWN_OPERATION", "Operazione non supportata.");
                if (_closing) return ControlResponse.Error("WINDOW_CLOSING", "La finestra si sta chiudendo. Riprova.");
                BeginInvoke(() =>
                {
                    if (_closing) return;
                    _callerTarget = request.TargetId;
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    Show(); Activate(); UpdateCaller();
                });
                return new ControlResponse();
            });
            await RunAsync(StartAsync); _timer.Start();
        };
        FormClosing += OnClosing;
    }

    private async Task RunAsync(Func<Task> work, bool skipBusy = false)
    {
        if (_closing) return;
        if (skipBusy && !await _operations.WaitAsync(0)) return;
        if (!skipBusy) await _operations.WaitAsync();
        try
        {
            if (_closing) return;
            if (!skipBusy)
                _disconnect.Enabled = _start.Enabled = _generate.Enabled = _cancel.Enabled = _addresses.Enabled = false;
            await work();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            ClearSecrets();
            _current = null;
            _status.Text = "Errore";
            _ttl.Text = ex is OperationCanceledException ? "Controllo locale non disponibile. Il codice precedente resta valido al massimo fino alla scadenza." : ex.Message;
        }
        finally
        {
            _operations.Release();
            if (!_closing)
            {
                _disconnect.Enabled = _start.Enabled = true; _addresses.Enabled = _current != null;
                _generate.Enabled = _current != null && _addresses.SelectedItem is NetworkAddress;
                _cancel.Enabled = _current?.State == "waiting";
                _enlarge.Enabled = _qr.Image != null;
            }
        }
    }

    private async Task DisconnectServersAsync()
    {
        var servers = await Task.Run(ServerProcesses.Discover, _stop.Token);
        if (servers.Length == 0)
        {
            MessageBox.Show(this, "Nessun backend HTTP Inventor SO del tuo utente da scollegare. Se la porta è ancora occupata, appartiene a un'altra applicazione.", "Server locali");
            return;
        }
        using var dialog = new Form { Text = "Scollega server Inventor SO", ClientSize = new Size(800, 360), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        list.Items.AddRange(servers);
        var info = new Label { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12), Text = "Seleziona i backend da fermare. I collegamenti dei visori a questi server saranno interrotti.\nInventor e i documenti restano aperti. Verrà poi avviato il server di pairing." };
        var confirm = new Button { Text = "Scollega selezionati", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        actions.Controls.AddRange([confirm, cancel]);
        list.ItemCheck += (_, e) => confirm.Enabled = list.CheckedItems.Count + (e.NewValue == CheckState.Checked ? 1 : -1) > 0;
        dialog.Controls.Add(list); dialog.Controls.Add(info); dialog.Controls.Add(actions); dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var server in list.CheckedItems.Cast<ServerProcess>())
            await ServerProcesses.DisconnectAsync(server, _stop.Token);
        ClearSecrets(); _windowId = null; _current = null;
        await StartAsync();
    }

    private async Task StartAsync()
    {
        _status.Text = "Avvio server…";
        var existing = await _probe(_stop.Token);
        if (existing != null) { Apply(existing); return; }
        _applying = true;
        try
        {
            var previous = (_targets.SelectedItem as InventorTarget)?.Id;
            var targets = HostLauncher.DiscoverTargets();
            _targets.Items.Clear(); _targets.Items.AddRange(targets);
            _targets.Enabled = true;
            var chosen = targets.FirstOrDefault(t => t.Id == (_callerTarget ?? previous));
            _targets.SelectedItem = chosen;
            if (_callerTarget == null && targets.Length > 1 && chosen == null)
            {
                _status.Text = "Scegli l'istanza Inventor";
                _ttl.Text = "Sono aperte più istanze. Seleziona quella da collegare e premi Avvia / Riprova.";
                return;
            }
            var targetRule = _callerTarget != null
                ? (targets.Length == 1 && targets[0].Id == _callerTarget ? "2027" : _callerTarget)
                : (targets.Length > 1 ? chosen!.Id : "2027");
            Apply(await HostLauncher.StartAsync(targetRule, _stop.Token));
        }
        finally { _applying = false; }
    }

    private async Task GenerateAsync()
    {
        if (_addresses.SelectedItem is not NetworkAddress address) return;
        ClearSecrets();
        var response = await _request(new ControlRequest(Operation: "open", Host: address.Address), _stop.Token);
        Apply(response);
        if (response.Code == null || response.QrPayload == null || response.WindowId == null) throw new IOException("Il server non ha restituito un codice valido.");
        _windowId = response.WindowId; _displayedHost = address.Address;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(response.QrPayload, QRCodeGenerator.ECCLevel.M);
        using var stream = new MemoryStream(new PngByteQRCode(data).GetGraphic(10));
        using var image = Image.FromStream(stream);
        _qr.Image = new Bitmap(image);
        _code.Text = response.Code.Insert(3, " ");
        _generate.Text = "Genera nuovo codice";
    }

    private async Task CancelAsync()
    {
        var id = _windowId ?? (_current?.State == "waiting" ? _current.WindowId : null);
        ClearSecrets();
        if (id == null) return;
        var response = await _request(new ControlRequest(Operation: "cancel", WindowId: id), _stop.Token);
        if (!response.Ok && response.ErrorCode == "WINDOW_STALE") { await PollAsync(); return; }
        Apply(response);
    }

    private async Task PollAsync()
    {
        if (_current == null) return;
        Apply(await _request(new ControlRequest(), _stop.Token));
        if (_current?.State == "waiting" && _displayedHost != null &&
            (_addresses.SelectedItem as NetworkAddress)?.Address != _displayedHost)
            await CancelAsync();
    }

    private void Apply(ControlResponse response)
    {
        if (!response.Ok) throw new InvalidOperationException(response.Message ?? response.ErrorCode);
        _current = response;
        if (response.State != "waiting" || (_windowId != null && response.WindowId != _windowId)) ClearSecrets();
        _status.Text = response.State switch
        {
            "waiting" => "In attesa del visore", "expired" => "Codice scaduto", "locked" => "Pairing chiuso: cinque tentativi errati",
            "cancelled" => "Pairing annullato", "paired" => "Visore associato", "failed" => "Errore: credenziale non salvata", _ => "Server pronto"
        };
        _ttl.Text = response.State == "waiting" ? "Valido ancora: " + TimeSpan.FromSeconds(Math.Ceiling(response.RemainingSeconds)).ToString(@"mm\:ss")
            : "Stato connessione visore non disponibile. L'associazione sarà riutilizzata dal Quest.";
        _inventor.Text = response.TargetId == null ? (response.Targets.Length > 1 ? "Inventor: target ambiguo o non disponibile" : "Apri Inventor per visualizzare un modello")
            : "Inventor: " + response.TargetId + " — " + (response.Document ?? "Apri un documento");
        var fingerprint = response.CertSha256 == null ? "" : string.Join(" ", Enumerable.Range(0, response.CertSha256.Length / 4)
            .Select(i => response.CertSha256.Substring(i * 4, 4).ToUpperInvariant()));
        if (_fingerprint.Text != fingerprint) { _fingerprint.Text = fingerprint; _fingerprint.SelectionLength = 0; }
        var wasApplying = _applying; _applying = true;
        try
        {
            _targets.Enabled = false;
            if (_targets.Items.Count == 0 && response.Targets.Length > 0) _targets.Items.AddRange(response.Targets);
            _targets.SelectedItem = _targets.Items.OfType<InventorTarget>().FirstOrDefault(t => t.Id == response.TargetId);
            if (!_addresses.DroppedDown && !_addresses.Items.OfType<NetworkAddress>().SequenceEqual(response.Addresses))
            {
                var selected = (_addresses.SelectedItem as NetworkAddress)?.Address;
                _addresses.Items.Clear(); _addresses.Items.AddRange(response.Addresses);
                if (_savedHost == null)
                {
                    var file = Path.Combine(_profileDirectory, "last-address.txt");
                    if (File.Exists(file)) _savedHost = File.ReadAllText(file).Trim();
                }
                _addresses.SelectedItem = response.Addresses.FirstOrDefault(a => a.Address == (selected ?? _savedHost));
                if (_addresses.SelectedIndex < 0 && response.Addresses.Length == 1) _addresses.SelectedIndex = 0;
            }
            if (response.Addresses.Length == 0) _ttl.Text = "Nessun indirizzo LAN disponibile. Collega il PC alla rete del visore.";
            else if (_addresses.SelectedIndex < 0) _ttl.Text = "Seleziona l'indirizzo della rete condivisa con il visore.";
            _endpoint.Text = _addresses.SelectedItem is NetworkAddress selectedAddress
                ? selectedAddress.Address + ":" + response.Port : "Seleziona un indirizzo LAN.";
        }
        finally { _applying = wasApplying; }
        UpdateCaller();
    }

    private void UpdateCaller() => _caller.Text = _callerTarget != null && _current != null && _callerTarget != _current.TargetId
        ? "Il pulsante proviene da " + _callerTarget + ". Il server mantiene il target " + (_current.TargetId ?? _current.TargetRule) + "." : "";

    private void ClearSecrets()
    {
        _zoom?.Close(); _zoom = null;
        var old = _qr.Image; _qr.Image = null; old?.Dispose();
        _code.Text = "Codice non generato"; _displayedHost = null; _enlarge.Enabled = false;
    }

    private void EnlargeQr()
    {
        if (_qr.Image == null) return;
        _zoom?.Close();
        _zoom = new Form { Text = "Scansiona questo QR dal visore", ClientSize = new Size(800, 800), StartPosition = FormStartPosition.CenterParent };
        var image = new Bitmap(_qr.Image);
        _zoom.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Image = image });
        _zoom.FormClosed += (_, _) => { image.Dispose(); _zoom = null; };
        _zoom.Show(this);
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _timer.Stop(); Enabled = false;
        await _operations.WaitAsync();
        try
        {
            await CancelAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
        {
            MessageBox.Show("Annullamento non confermato dal server. Il codice resta valido al massimo fino alla sua scadenza di due minuti.", "Inventor SO");
        }
        finally
        {
            _stop.Cancel();
            if (_activation != null) await _activation.DisposeAsync();
            ClearSecrets(); _operations.Release(); _timer.Dispose(); _stop.Dispose();
            _allowClose = true; Close();
        }
    }
}
