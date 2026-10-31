using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RClicker.Native;
using RClicker.Presentation;
using RClicker.Qr;
using RClicker.Relay;
using RClicker.Sessions;

namespace RClicker.UI;

/// <summary>
/// The one small window: QR code, link, status, "New session" and "Quit".
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color OkColor = Color.FromArgb(0, 110, 40);
    private static readonly Color WarnColor = Color.FromArgb(150, 85, 0);
    private static readonly Color BadColor = Color.FromArgb(180, 20, 20);

    private readonly ILogger _logger;
    private readonly SessionManager _sessions;
    private readonly KeyboardPresentationController _controller;
    private readonly RelayHostClient? _relay;
    private readonly string? _configError;

    private readonly Label _statusLabel = new();
    private readonly Label _hintLabel = new();
    private readonly QrCodeView _qrView = new();
    private readonly Panel _qrHiddenPanel = new();
    private readonly TextBox _urlBox = new();
    private readonly CheckBox _powerPointOnlyBox = new();
    private readonly ComboBox _targetBox = new();
    private readonly Label _targetHint = new();
    private readonly Button _copyButton = new();
    private readonly System.Windows.Forms.Timer _expiryTimer = new() { Interval = 30_000 };

    private bool _userRevealedQr;
    private bool _keepingAwake;
    private bool _closeRequested;
    private bool _readyToClose;

    public MainForm(Uri? relay, string? configError, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<MainForm>();
        _sessions = new SessionManager();
        _controller = new KeyboardPresentationController(
            new Win32KeySender(),
            new Win32ForegroundWindowProvider(),
            loggerFactory.CreateLogger<KeyboardPresentationController>());
        var router = new CommandRouter(_controller, new CommandRateLimiter(), loggerFactory.CreateLogger<CommandRouter>());

        if (relay is not null)
        {
            _relay = new RelayHostClient(new RelayClientOptions(relay), _sessions, router, loggerFactory.CreateLogger<RelayHostClient>());
            _relay.StatusChanged += (_, _) => RunOnUi(RefreshStatus);
        }
        else
        {
            _configError = configError ?? "No relay address is configured.";
        }

        BuildLayout();

        _sessions.SessionChanged += (_, _) => RunOnUi(RefreshUrlAndQr);
        _expiryTimer.Tick += (_, _) => _sessions.RegenerateIfExpired();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_relay is not null)
        {
            _logger.LogInformation("Using relay {Relay}", _relay.Relay);
            _relay.Start();
        }

        _expiryTimer.Start();
        RefreshUrlAndQr();
        RefreshStatus();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_readyToClose || e.CloseReason == CloseReason.WindowsShutDown || _relay is null)
        {
            base.OnFormClosing(e);
            return;
        }

        // Tell the relay we're closing (the phone shows "rclicker closed") before the window goes.
        e.Cancel = true;
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        Hide();
        try
        {
            await _relay.DisposeAsync();
        }
#pragma warning disable CA1031 // Closing must always succeed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Error while disconnecting from the relay");
        }

        _readyToClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
        ClientSize = new Size(460, 700);
        MinimumSize = new Size(400, 600);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = Color.White,
            AutoScroll = true,
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
        _urlBox.AccessibleName = "Phone link";
        _copyButton.Text = "Copy";
        _copyButton.AutoSize = true;
        _copyButton.Click += (_, _) => CopyUrl();
        var urlRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 2),
        };
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        urlRow.Controls.Add(_urlBox, 0, 0);
        urlRow.Controls.Add(_copyButton, 1, 0);

        _targetBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _targetBox.Items.AddRange(["PowerPoint", "PDF — Adobe Acrobat/Reader or SumatraPDF"]);
        _targetBox.Dock = DockStyle.Fill;
        _targetBox.AccessibleName = "Presentation app";
        _targetHint.AutoSize = true;
        _targetHint.MaximumSize = new Size(420, 0);
        _targetHint.ForeColor = Color.FromArgb(60, 60, 60);
        _targetBox.SelectedIndexChanged += (_, _) =>
        {
            bool pdf = _targetBox.SelectedIndex == 1;
            _controller.Target = pdf ? PresentationTarget.Pdf : PresentationTarget.PowerPoint;
            if (pdf) _powerPointOnlyBox.Checked = true;
            _powerPointOnlyBox.Enabled = !pdf;
            _targetHint.Text = pdf
                ? "Click the PDF document first. Next/Previous turn pages; Start toggles fullscreen; End exits. Black is unavailable."
                : "Click on PowerPoint, then use the phone controls.";
        };
        _targetBox.SelectedIndex = 0;
        var targetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3,
        };
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetPanel.Controls.Add(new Label { Text = "Presentation app", AutoSize = true });
        targetPanel.Controls.Add(_targetBox);
        targetPanel.Controls.Add(_targetHint);
        targetPanel.SizeChanged += (_, _) =>
            _targetHint.MaximumSize = new Size(Math.Max(1, targetPanel.ClientSize.Width - 6), 0);

        _powerPointOnlyBox.Text = "Only send keys to the selected app (recommended)";
        _powerPointOnlyBox.Checked = true;
        _powerPointOnlyBox.AutoSize = true;
        _powerPointOnlyBox.MaximumSize = new Size(420, 0);
        _powerPointOnlyBox.Margin = new Padding(0, 6, 0, 6);
        _powerPointOnlyBox.CheckedChanged += (_, _) =>
        {
            _controller.RestrictToPowerPoint = _powerPointOnlyBox.Checked;
            _logger.LogInformation("Restrict to PowerPoint: {Value}", _powerPointOnlyBox.Checked);
        };

        var regenerateButton = new Button { Text = "New session", AutoSize = true, Enabled = _relay is not null };
        regenerateButton.Click += (_, _) => RegenerateSession();
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
        buttons.Controls.AddRange([regenerateButton, helpButton, quitButton]);

        var version = new Label
        {
            Text = _relay is null
                ? $"Version {AppInfo.Version}"
                : $"Version {AppInfo.Version} · relay {_relay.Relay.Host}",
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
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_statusLabel, 0, 1);
        layout.Controls.Add(_hintLabel, 0, 2);
        layout.Controls.Add(qrHost, 0, 3);
        layout.Controls.Add(urlRow, 0, 4);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(targetPanel, 0, 5);
        layout.Controls.Add(_powerPointOnlyBox, 0, 6);
        layout.Controls.Add(buttons, 0, 7);
        layout.Controls.Add(version, 0, 8);

        Controls.Add(layout);
    }

    private void RefreshUrlAndQr()
    {
        if (_relay is null)
        {
            _urlBox.Text = string.Empty;
            _qrView.Modules = null;
            _qrView.Placeholder = "No relay configured.";
            _copyButton.Enabled = false;
            return;
        }

        var url = _relay.PhoneUrl.ToString();
        _urlBox.Text = url;
        _qrView.Modules = QrCodeMatrix.Create(url);
        _copyButton.Enabled = true;
        _logger.LogInformation("Showing QR for {Relay} ({Session})", _relay.Relay, _sessions.Current);
    }

    private void RefreshStatus()
    {
        string text;
        string hint;
        Color color;
        var status = _relay?.Status;

        if (_relay is null || status is null)
        {
            (text, color) = ("Not configured", BadColor);
            hint = _configError + " Start rclicker with --relay https://<your relay address>.";
        }
        else
        {
            switch (status.State)
            {
                case RelayState.PhoneConnected:
                    (text, color) = ($"Connected — {status.DeviceLabel}", OkColor);
                    hint = Form.ActiveForm == this
                        ? "Now click on PowerPoint: key presses go to the active window."
                        : "Keep PowerPoint as the active window while presenting.";
                    break;
                case RelayState.PhoneReconnecting:
                    (text, color) = ($"Reconnecting… waiting for {status.DeviceLabel}", WarnColor);
                    hint = "The phone dropped off (screen lock or signal). It will reconnect when it wakes up.";
                    break;
                case RelayState.Ready:
                    (text, color) = ("Ready — scan the QR code", OkColor);
                    hint = "Open your phone's camera and point it at the code. The phone can use mobile data or any Wi-Fi.";
                    break;
                case RelayState.Offline:
                    (text, color) = ("Offline", BadColor);
                    hint = status.Error ?? "Can't reach the relay. Retrying…";
                    break;
                case RelayState.Stopped:
                    (text, color) = ("Stopped", BadColor);
                    hint = string.Empty;
                    break;
                default:
                    (text, color) = ("Connecting…", WarnColor);
                    hint = "Connecting to the rclicker relay over the internet.";
                    break;
            }
        }

        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
        _hintLabel.Text = hint;

        bool phonePresent = status?.State is RelayState.PhoneConnected or RelayState.PhoneReconnecting;
        if (!phonePresent)
        {
            _userRevealedQr = false;
        }

        KeepComputerAwake(phonePresent);

        bool hideQr = phonePresent && !_userRevealedQr;
        _qrHiddenPanel.Visible = hideQr;
        _qrView.Visible = !hideQr;
    }

    /// <summary>
    /// While a phone is connected, stop Windows from dimming the screen or sleeping
    /// (the presenter may not touch the PC for a long time). Runs on the UI thread,
    /// which owns the setting for as long as the app is open.
    /// </summary>
    private void KeepComputerAwake(bool awake)
    {
        if (awake == _keepingAwake)
        {
            return;
        }

        _keepingAwake = awake;
        var flags = awake
            ? NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED | NativeMethods.ES_DISPLAY_REQUIRED
            : NativeMethods.ES_CONTINUOUS;
        if (NativeMethods.SetThreadExecutionState(flags) == 0)
        {
            _logger.LogWarning("Windows did not accept the keep-awake request");
        }
        else
        {
            _logger.LogInformation("Keep screen on: {Awake}", awake);
        }
    }

    private void RegenerateSession()
    {
        var phonePresent = _relay?.Status.State is RelayState.PhoneConnected or RelayState.PhoneReconnecting;
        var answer = !phonePresent
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
            "1. Choose PowerPoint or PDF in Presentation app. Open the document.\n" +
            "2. Scan the QR code with your phone camera (mobile data is fine).\n" +
            "3. Click the presentation document so it is active, then use the phone.\n\n" +
            "Stuck on 'Offline' or 'Connecting'?\n" +
            "• This computer needs internet access. Corporate proxies are used automatically.\n" +
            "• Your network may block the relay address. Ask IT to allow it (HTTPS and WebSockets on port 443).\n\n" +
            "PDF mode\n" +
            "• Supports Adobe Acrobat/Reader and SumatraPDF. Click the PDF document first.\n" +
            "• Start toggles fullscreen, Next/Previous turn pages, End exits. Black is unavailable.\n" +
            "• PDFs in Edge/Chrome use the separate browser presenter.\n\n" +
            "PowerPoint doesn't react?\n" +
            "• PowerPoint must be the active window. Click on it once.\n" +
            "• If PowerPoint runs as administrator, run rclicker as administrator too.";
        MessageBox.Show(this, help, $"{AppInfo.Name} — Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
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
