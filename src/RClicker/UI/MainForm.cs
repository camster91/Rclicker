using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RClicker.Networking;
using RClicker.Presentation;
using RClicker.Qr;
using RClicker.Server;
using RClicker.Sessions;

namespace RClicker.UI;

/// <summary>
/// The one small window: QR code, URL, status, adapter picker, "New session" and "Quit".
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color OkColor = Color.FromArgb(0, 110, 40);
    private static readonly Color WarnColor = Color.FromArgb(150, 85, 0);
    private static readonly Color BadColor = Color.FromArgb(180, 20, 20);

    private readonly ILogger _logger;
    private readonly SessionManager _sessions;
    private readonly ControllerHub _hub;
    private readonly KeyboardPresentationController _controller;
    private readonly RemoteServer _server;

    private readonly Label _statusLabel = new();
    private readonly Label _hintLabel = new();
    private readonly QrCodeView _qrView = new();
    private readonly Panel _qrHiddenPanel = new();
    private readonly TextBox _urlBox = new();
    private readonly ComboBox _adapterBox = new();
    private readonly CheckBox _powerPointOnlyBox = new();
    private readonly Button _copyButton = new();
    private readonly Button _regenerateButton = new();
    private readonly System.Windows.Forms.Timer _networkDebounce = new() { Interval = 1500 };
    private readonly System.Windows.Forms.Timer _expiryTimer = new() { Interval = 30_000 };

    private IReadOnlyList<LanAddressCandidate> _candidates = [];
    private string? _serverError;
    private bool _userRevealedQr;
    private bool _closeRequested;
    private bool _readyToClose;

    public MainForm(CommandLineOptions options, ILoggerFactory loggerFactory, Action<ILoggingBuilder> configureServerLogging)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<MainForm>();
        _sessions = new SessionManager();
        _hub = new ControllerHub(_sessions, logger: loggerFactory.CreateLogger<ControllerHub>());
        _controller = new KeyboardPresentationController(
            new Win32KeySender(),
            new Win32ForegroundWindowProvider(),
            loggerFactory.CreateLogger<KeyboardPresentationController>());
        var router = new CommandRouter(_controller, new CommandRateLimiter(), loggerFactory.CreateLogger<CommandRouter>());
        _server = new RemoteServer(
            new RemoteServerOptions
            {
                Port = options.Port ?? RemoteServerOptions.DefaultPort,
                PortFallbackAttempts = options.Port is null ? 10 : 0,
                ConfigureLogging = configureServerLogging,
            },
            _sessions,
            _hub,
            router);

        BuildLayout();

        _sessions.SessionChanged += (_, _) => RunOnUi(RefreshUrlAndQr);
        _hub.StatusChanged += (_, _) => RunOnUi(RefreshStatus);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _networkDebounce.Tick += (_, _) =>
        {
            _networkDebounce.Stop();
            RefreshAddresses();
        };
        _expiryTimer.Tick += (_, _) => _sessions.RegenerateIfExpired();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshAddresses();
        RefreshStatus();

        try
        {
            await _server.StartAsync();
            _serverError = null;
        }
        catch (ServerStartException ex)
        {
            _logger.LogError(ex, "Server failed to start");
            _serverError = ex.Message;
        }
#pragma warning disable CA1031 // Show any startup failure to the user instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Server failed to start");
            _serverError = "Could not start the server: " + ex.Message;
        }

        _expiryTimer.Start();
        RefreshUrlAndQr();
        RefreshStatus();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_readyToClose || e.CloseReason == CloseReason.WindowsShutDown)
        {
            base.OnFormClosing(e);
            return;
        }

        // Stop the server (which tells the phone the session is over) before the window goes.
        e.Cancel = true;
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        Hide();
        try
        {
            await _server.StopAsync();
        }
#pragma warning disable CA1031 // Closing must always succeed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Error while stopping the server");
        }

        _readyToClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            _networkDebounce.Dispose();
            _expiryTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshStatus();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        RefreshStatus();
    }

    private void BuildLayout()
    {
        Text = AppInfo.Name;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 720);
        MinimumSize = new Size(400, 620);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = Color.White,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = AppInfo.Name,
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };

        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _statusLabel.Margin = new Padding(0, 0, 0, 4);
        _statusLabel.AccessibleName = "Connection status";

        _hintLabel.AutoSize = true;
        _hintLabel.MaximumSize = new Size(420, 0);
        _hintLabel.ForeColor = Color.FromArgb(60, 60, 60);
        _hintLabel.Margin = new Padding(0, 0, 0, 8);

        _qrView.Dock = DockStyle.Fill;
        _qrView.MinimumSize = new Size(200, 200);
        _qrView.Font = new Font("Segoe UI", 11F);

        var qrHiddenLabel = new Label
        {
            Text = "QR code hidden while a phone is connected,\nso it is not shown on the projector.",
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        };
        var showQrButton = new Button { Text = "Show QR code", AutoSize = true, Dock = DockStyle.Bottom };
        showQrButton.Click += (_, _) =>
        {
            _userRevealedQr = true;
            RefreshStatus();
        };
        _qrHiddenPanel.Dock = DockStyle.Fill;
        _qrHiddenPanel.Controls.Add(qrHiddenLabel);
        _qrHiddenPanel.Controls.Add(showQrButton);
        _qrHiddenPanel.Visible = false;

        var qrHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
        qrHost.Controls.Add(_qrView);
        qrHost.Controls.Add(_qrHiddenPanel);

        _urlBox.ReadOnly = true;
        _urlBox.Dock = DockStyle.Fill;
        _urlBox.BackColor = Color.White;
        _urlBox.AccessibleName = "Controller address";
        _copyButton.Text = "Copy";
        _copyButton.AutoSize = true;
        _copyButton.Click += (_, _) => CopyUrl();
        var urlRow = Row(_urlBox, _copyButton);

        _adapterBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _adapterBox.Dock = DockStyle.Fill;
        _adapterBox.AccessibleName = "Network adapter";
        _adapterBox.SelectedIndexChanged += (_, _) => RefreshUrlAndQr();
        _adapterBox.DropDown += (_, _) => RefreshAddresses();
        var adapterLabel = new Label { Text = "Network:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };
        var adapterRow = Row(adapterLabel, _adapterBox, firstColumnAutoSize: true);

        _powerPointOnlyBox.Text = "Only send keys when PowerPoint is the active window (recommended)";
        _powerPointOnlyBox.Checked = true;
        _powerPointOnlyBox.AutoSize = true;
        _powerPointOnlyBox.MaximumSize = new Size(420, 0);
        _powerPointOnlyBox.Margin = new Padding(0, 6, 0, 6);
        _powerPointOnlyBox.CheckedChanged += (_, _) =>
        {
            _controller.RestrictToPowerPoint = _powerPointOnlyBox.Checked;
            _logger.LogInformation("Restrict to PowerPoint: {Value}", _powerPointOnlyBox.Checked);
        };

        _regenerateButton.Text = "New session";
        _regenerateButton.AutoSize = true;
        _regenerateButton.Click += (_, _) => RegenerateSession();
        var helpButton = new Button { Text = "Help", AutoSize = true };
        helpButton.Click += (_, _) => ShowHelp();
        var quitButton = new Button { Text = "Quit", AutoSize = true };
        quitButton.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.AddRange([_regenerateButton, helpButton, quitButton]);

        var version = new Label
        {
            Text = $"Version {AppInfo.Version} · local network only",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 90, 90),
            Font = new Font("Segoe UI", 8.5F),
            Margin = new Padding(0, 6, 0, 0),
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_statusLabel, 0, 1);
        layout.Controls.Add(_hintLabel, 0, 2);
        layout.Controls.Add(qrHost, 0, 3);
        layout.Controls.Add(urlRow, 0, 4);
        layout.Controls.Add(adapterRow, 0, 5);
        layout.Controls.Add(_powerPointOnlyBox, 0, 6);
        layout.Controls.Add(buttons, 0, 7);
        layout.Controls.Add(version, 0, 8);

        Controls.Add(layout);
        AcceptButton = null;
        CancelButton = null;
    }

    private static TableLayoutPanel Row(Control first, Control second, bool firstColumnAutoSize = false)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 2),
        };
        if (firstColumnAutoSize)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }
        else
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        row.Controls.Add(first, 0, 0);
        row.Controls.Add(second, 1, 0);
        return row;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => RunOnUi(() =>
    {
        // Adapters flap during Wi-Fi changes; wait for things to settle.
        _networkDebounce.Stop();
        _networkDebounce.Start();
    });

    private void RefreshAddresses()
    {
        var previous = (_adapterBox.SelectedItem as LanAddressCandidate)?.Address;
        _candidates = SystemNetworkAdapters.GetCandidates();
        _logger.LogInformation("Network candidates: {Candidates}", string.Join(", ", _candidates.Select(c => $"{c.Address} ({c.AdapterName}, score {c.Score})")));

        _adapterBox.BeginUpdate();
        _adapterBox.Items.Clear();
        foreach (var candidate in _candidates)
        {
            _adapterBox.Items.Add(candidate);
        }

        int index = previous is null ? -1 : _candidates.ToList().FindIndex(c => c.Address.Equals(previous));
        _adapterBox.SelectedIndex = index >= 0 ? index : (_candidates.Count > 0 ? 0 : -1);
        _adapterBox.EndUpdate();
        _adapterBox.Enabled = _candidates.Count > 1;

        RefreshUrlAndQr();
        RefreshStatus();
    }

    private void RefreshUrlAndQr()
    {
        var candidate = _adapterBox.SelectedItem as LanAddressCandidate;
        if (!_server.IsRunning || candidate is null)
        {
            _urlBox.Text = _server.IsRunning ? "No network address found" : string.Empty;
            _qrView.Modules = null;
            _qrView.Placeholder = _serverError ?? (_server.IsRunning
                ? "No network connection.\nConnect this computer to Wi-Fi or Ethernet."
                : "Starting…");
            _copyButton.Enabled = false;
            return;
        }

        var url = ControllerUrl.Build(candidate.Address, _server.Port, _sessions.Current.Token).ToString();
        _urlBox.Text = url;
        _qrView.Modules = QrCodeMatrix.Create(url);
        _copyButton.Enabled = true;
        _logger.LogInformation("Showing QR for http://{Address}:{Port}/ ({Session})", candidate.Address, _server.Port, _sessions.Current);
    }

    private void RefreshStatus()
    {
        var status = _hub.Status;
        string text;
        string hint;
        Color color;

        if (_serverError is not null)
        {
            (text, color) = ("Not running", BadColor);
            hint = _serverError;
        }
        else if (!_server.IsRunning)
        {
            (text, color) = ("Starting…", WarnColor);
            hint = string.Empty;
        }
        else if (_candidates.Count == 0)
        {
            (text, color) = ("No network", BadColor);
            hint = "Connect this computer to the same Wi-Fi (or network) as your phone.";
        }
        else
        {
            switch (status.State)
            {
                case ControllerState.Connected:
                    (text, color) = ($"Connected — {status.DeviceLabel}", OkColor);
                    hint = Form.ActiveForm == this
                        ? "Now click on PowerPoint: key presses go to the active window."
                        : "Keep PowerPoint as the active window while presenting.";
                    break;
                case ControllerState.Reconnecting:
                    (text, color) = ($"Reconnecting… waiting for {status.DeviceLabel}", WarnColor);
                    hint = "The phone dropped off (screen lock or Wi-Fi). It will reconnect when it wakes up.";
                    break;
                default:
                    (text, color) = ("Ready — scan the QR code", OkColor);
                    hint = "Open your phone's camera and point it at the code. Phone and computer must be on the same network.";
                    break;
            }
        }

        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
        _hintLabel.Text = hint;

        bool phonePresent = status.State != ControllerState.Waiting && _serverError is null;
        if (!phonePresent)
        {
            _userRevealedQr = false;
        }

        bool hideQr = phonePresent && !_userRevealedQr;
        _qrHiddenPanel.Visible = hideQr;
        _qrView.Visible = !hideQr;
    }

    private void RegenerateSession()
    {
        var answer = _hub.Status.State == ControllerState.Waiting
            ? DialogResult.Yes
            : MessageBox.Show(
                this,
                "This disconnects the phone that is connected now. You will need to scan the new QR code.\n\nContinue?",
                AppInfo.Name,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            _sessions.Regenerate();
        }
    }

    private void CopyUrl()
    {
        try
        {
            Clipboard.SetText(_urlBox.Text);
        }
        catch (ExternalException)
        {
            MessageBox.Show(this, "The clipboard is busy. Try again.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ShowHelp()
    {
        const string help =
            "Using it\n" +
            "1. Open your presentation in PowerPoint.\n" +
            "2. Scan the QR code with your phone camera.\n" +
            "3. Click on the PowerPoint window so it is active, then use the phone.\n\n" +
            "Phone can't open the page?\n" +
            "• Phone and computer must be on the same Wi-Fi. Guest, school and office networks often block this.\n" +
            "• Windows Firewall: allow rclicker on Private networks (Windows asks the first time). " +
            "If your Wi-Fi is set to 'Public', change it to 'Private' or allow the app for Public networks.\n" +
            "• Turn off VPNs, or pick another address in the Network list.\n\n" +
            "PowerPoint doesn't react?\n" +
            "• PowerPoint must be the active window. Click on it once.\n" +
            "• If PowerPoint runs as administrator, run rclicker as administrator too.";
        MessageBox.Show(this, help, $"{AppInfo.Name} — Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Window is closing.
            }
        }
        else
        {
            action();
        }
    }
}
