using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace EjecutableNFC;

public class MainForm : Form
{
    private readonly NfcService    _nfcService    = new();
    private readonly SerialService _serialService = new();

    // ── Security gate ─────────────────────────────────────────────────────
    // Only set when the Arduino physically detects and sends an NFC UID.
    // The Register button is ONLY enabled when this has a value.
    private string? _pendingUid = null;

    // ── Controls ──────────────────────────────────────────────────────────
    private ComboBox              _cmbPort           = null!;
    private ComboBox              _cmbBaud           = null!;
    private Button                _btnRefreshPorts   = null!;
    private Button                _btnConnect        = null!;
    private Label                 _lblStatusDot      = null!;
    private Label                 _lblStatusText     = null!;
    private TextBox               _txtBackendUrl     = null!;
    private Button                _btnTestBackend    = null!;
    private RichTextBox           _rtbLogs           = null!;
    private Button                _btnClearLogs      = null!;
    private TextBox               _txtLastUid        = null!;
    private Button                _btnRegister       = null!;
    private Label                 _lblRegisterHint   = null!;
    private ToolStripStatusLabel  _tslConnStatus     = null!;
    private ToolStripStatusLabel  _tslTime           = null!;
    private System.Windows.Forms.Timer _clockTimer   = null!;

    // ─────────────────────────────────────────────────────────────────────

    public MainForm()
    {
        _serialService.UidReceived    += OnUidReceived;
        _serialService.LogMessage     += OnArduinoLog;
        _serialService.ConnectionLost += OnConnectionLost;

        BuildUI();
        RefreshPorts();
        AppendLog("Aplicación iniciada. Selecciona un puerto COM y pulsa Conectar.", LogLevel.Info);
        AppendLog("URL del backend cargada desde configuración.", LogLevel.Info);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _serialService.Disconnect();
        _clockTimer.Stop();
        base.OnFormClosing(e);
    }

    // ═════════════════════════════════════════════════════════════════════
    // UI CONSTRUCTION
    // ═════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        SuspendLayout();

        Text            = "NFC Access Manager";
        Size            = new Size(760, 680);
        MinimumSize     = new Size(660, 580);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);
        AutoScaleMode   = AutoScaleMode.Dpi;
        BackColor       = Color.FromArgb(245, 246, 250);

        // Status strip — must be added before the fill panel
        var strip = new StatusStrip { SizingGrip = false, BackColor = Color.FromArgb(230, 232, 236) };
        _tslConnStatus = new ToolStripStatusLabel("● Desconectado") { ForeColor = Color.Red };
        _tslTime       = new ToolStripStatusLabel { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.FromArgb(80, 80, 80) };
        strip.Items.Add(_tslConnStatus);
        strip.Items.Add(new ToolStripStatusLabel { Spring = true });
        strip.Items.Add(_tslTime);
        Controls.Add(strip);

        // Root layout
        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            Padding     = new Padding(10, 8, 10, 6),
            ColumnCount = 1,
            RowCount    = 4,
            BackColor   = Color.Transparent
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));   // Arduino
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));   // Backend
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // Logs
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));   // Register
        Controls.Add(root);

        root.Controls.Add(BuildArduinoGroup(),  0, 0);
        root.Controls.Add(BuildBackendGroup(),  0, 1);
        root.Controls.Add(BuildLogsGroup(),     0, 2);
        root.Controls.Add(BuildRegisterGroup(), 0, 3);

        // Clock
        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => _tslTime.Text = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();
        _tslTime.Text = DateTime.Now.ToString("HH:mm:ss");

        ResumeLayout(true);
    }

    // ── Group: Arduino connection ─────────────────────────────────────────

    private GroupBox BuildArduinoGroup()
    {
        var grp = MakeGroupBox("Conexión Arduino");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 6),
            ColumnCount = 8,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grp.Controls.Add(layout);

        var lblPort = MakeFlowLabel("Puerto:");
        layout.Controls.Add(lblPort, 0, 0);

        _cmbPort = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 180,
            IntegralHeight = false
        };
        _cmbPort.DropDown += (_, _) => RefreshPorts();
        layout.Controls.Add(_cmbPort, 1, 0);

        var lblBaud = MakeFlowLabel("Baudios:");
        layout.Controls.Add(lblBaud, 2, 0);

        _cmbBaud = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
        _cmbBaud.SelectedIndex = 0;
        layout.Controls.Add(_cmbBaud, 3, 0);

        _btnRefreshPorts = new Button
        {
            Text      = "↻",
            Dock      = DockStyle.Fill,
            Margin    = new Padding(6, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 11f)
        };
        _btnRefreshPorts.Click += (_, _) => RefreshPorts();
        layout.Controls.Add(_btnRefreshPorts, 4, 0);

        _btnConnect = MakeButton("Conectar", Point.Empty, new Size(108, 26),
                                 Color.FromArgb(0, 120, 215));
        _btnConnect.Dock = DockStyle.Fill;
        _btnConnect.Click += OnConnectClicked;
        layout.Controls.Add(_btnConnect, 5, 0);

        _lblStatusDot = new Label
        {
            Text     = "●",
            Dock     = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true,
            ForeColor = Color.Red,
            Font     = new Font("Segoe UI", 14f)
        };
        layout.Controls.Add(_lblStatusDot, 6, 0);

        _lblStatusText = new Label
        {
            Text     = "Desconectado",
            Dock     = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true
        };
        layout.Controls.Add(_lblStatusText, 7, 0);

        return grp;
    }

    // ── Group: Backend URL ────────────────────────────────────────────────

    private GroupBox BuildBackendGroup()
    {
        var grp = MakeGroupBox("Configuración Backend");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 6),
            ColumnCount = 3,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
        grp.Controls.Add(layout);

        layout.Controls.Add(MakeFlowLabel("URL:"), 0, 0);

        _txtBackendUrl = new TextBox
        {
            Text     = "https://backend-nfc-lo1t.onrender.com",
            Dock     = DockStyle.Fill,
            Margin   = new Padding(6, 0, 8, 0)
        };
        layout.Controls.Add(_txtBackendUrl, 1, 0);

        _btnTestBackend = MakeButton("Probar", Point.Empty, new Size(70, 26),
                                     Color.FromArgb(39, 160, 100));
        _btnTestBackend.Dock = DockStyle.Fill;
        _btnTestBackend.Click += OnTestBackendClicked;
        layout.Controls.Add(_btnTestBackend, 2, 0);

        return grp;
    }

    // ── Group: Logs ───────────────────────────────────────────────────────

    private GroupBox BuildLogsGroup()
    {
        var grp = MakeGroupBox("Actividad en Tiempo Real");
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 2, 4, 4) };
        grp.Controls.Add(outer);

        // Top bar
        var topBar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
        _btnClearLogs = new Button
        {
            Text      = "Limpiar",
            Dock      = DockStyle.Right,
            Width     = 72,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 8.5f)
        };
        _btnClearLogs.Click += (_, _) => _rtbLogs.Clear();
        topBar.Controls.Add(_btnClearLogs);

        _rtbLogs = new RichTextBox
        {
            ReadOnly    = true,
            BackColor   = Color.FromArgb(22, 27, 34),
            ForeColor   = Color.FromArgb(201, 209, 217),
            Font        = new Font("Consolas", 9f),
            Dock        = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars  = RichTextBoxScrollBars.Vertical
        };
        outer.Controls.Add(_rtbLogs);
        outer.Controls.Add(topBar);

        return grp;
    }

    // ── Group: Register ───────────────────────────────────────────────────

    private GroupBox BuildRegisterGroup()
    {
        var grp = MakeGroupBox("Registro de Nueva Tarjeta NFC");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 6),
            ColumnCount = 3,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        grp.Controls.Add(layout);

        layout.Controls.Add(MakeFlowLabel("Última UID leída:"), 0, 0);

        _txtLastUid = new TextBox
        {
            Dock      = DockStyle.Fill,
            ReadOnly  = true,
            Text      = "(ninguna)",
            BackColor = Color.FromArgb(236, 240, 241),
            Font      = new Font("Consolas", 9.5f),
            Margin    = new Padding(6, 0, 8, 0)
        };
        layout.Controls.Add(_txtLastUid, 1, 0);

        _btnRegister = MakeButton("Registrar Tarjeta", Point.Empty,
                                  new Size(150, 28), Color.FromArgb(39, 174, 96));
        _btnRegister.Dock = DockStyle.Fill;
        _btnRegister.Enabled = false;
        _btnRegister.Click  += OnRegisterClicked;
        layout.Controls.Add(_btnRegister, 2, 0);

        _lblRegisterHint = new Label
        {
            Text      = "⚠  Acerca una tarjeta NFC al lector Arduino para habilitar el registro.",
            AutoSize  = true,
            ForeColor = Color.FromArgb(127, 140, 141),
            Font      = new Font("Segoe UI", 8.5f, FontStyle.Italic)
        };
        layout.SetColumnSpan(_lblRegisterHint, 3);
        layout.Controls.Add(_lblRegisterHint, 0, 1);

        return grp;
    }

    // ═════════════════════════════════════════════════════════════════════
    // EVENT HANDLERS
    // ═════════════════════════════════════════════════════════════════════

    private void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_serialService.IsConnected)
        {
            _serialService.Disconnect();
            SetConnectedState(false);
            SetPendingUid(null);
            AppendLog("Desconectado del Arduino.", LogLevel.Warning);
            return;
        }

        if (_cmbPort.SelectedItem is not string port)
        {
            MessageBox.Show("Selecciona un puerto COM.", "Sin puerto",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(_cmbBaud.SelectedItem as string, out int baud)) baud = 9600;

        bool ok = _serialService.Connect(port, baud);
        SetConnectedState(ok);

        if (ok)
            AppendLog($"Conectado a {port} @ {baud} bps. Esperando tarjetas NFC...", LogLevel.Success);
        else
            AppendLog($"No se pudo abrir {port}. Verifica que el Arduino está conectado.", LogLevel.Error);
    }

    // Called from serial thread — marshalled to UI thread
    private void OnUidReceived(string uid)
    {
        if (InvokeRequired) { BeginInvoke(() => OnUidReceived(uid)); return; }

        AppendLog($"NFC detectado — UID: {uid}", LogLevel.Nfc);
        SetPendingUid(uid);

        // Auto-scan asynchronously — fire and forget on UI SynchronizationContext
        _ = ScanUidAsync(uid);
    }

    private async Task ScanUidAsync(string uid)
    {
        string backendUrl = _txtBackendUrl.Text.Trim();
        AppendLog("Consultando al backend...", LogLevel.Info);

        var result = await _nfcService.ScanAsync(backendUrl, uid);
        AppendLog(result.Message, result.Success ? LogLevel.Success : LogLevel.Error);
    }

    private void OnArduinoLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => OnArduinoLog(message)); return; }
        AppendLog(message, LogLevel.Info);
    }

    private void OnConnectionLost()
    {
        if (InvokeRequired) { BeginInvoke(OnConnectionLost); return; }
        SetConnectedState(false);
        SetPendingUid(null);
        AppendLog("⚠ Conexión con Arduino perdida inesperadamente.", LogLevel.Warning);
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        if (_pendingUid is null) return;

        string uid = _pendingUid;
        string backendUrl = _txtBackendUrl.Text.Trim();

        // Immediately gate: disable + clear pending before the async call
        // This ensures the user must re-scan physically to register again
        SetPendingUid(null);
        _btnRegister.Text    = "Registrando...";
        _btnRegister.Enabled = false;

        AppendLog($"Registrando UID: {uid}...", LogLevel.Info);
        var result = await _nfcService.RegisterAsync(backendUrl, uid);
        AppendLog(result.Message, result.Success ? LogLevel.Success : LogLevel.Warning);

        _btnRegister.Text = "Registrar Tarjeta";
        // Button stays disabled — user must physically scan again (security gate)
    }

    private async void OnTestBackendClicked(object? sender, EventArgs e)
    {
        _btnTestBackend.Enabled = false;
        _btnTestBackend.Text    = "...";

        string backendUrl = _txtBackendUrl.Text.Trim();
        AppendLog($"Probando conexión a: {backendUrl}", LogLevel.Info);

        var result = await _nfcService.TestConnectionAsync(backendUrl);
        AppendLog(result.Message, result.Success ? LogLevel.Success : LogLevel.Error);

        _btnTestBackend.Enabled = true;
        _btnTestBackend.Text    = "Probar";
    }

    // ═════════════════════════════════════════════════════════════════════
    // HELPERS — UI STATE
    // ═════════════════════════════════════════════════════════════════════

    private void RefreshPorts()
    {
        string? selected = _cmbPort.SelectedItem as string;
        _cmbPort.Items.Clear();
        var ports = SerialService.GetAvailablePorts()
            .OrderBy(GetPortSortKey)
            .ToArray();
        _cmbPort.Items.AddRange(ports);

        if (selected != null && _cmbPort.Items.Contains(selected))
            _cmbPort.SelectedItem = selected;
        else if (ports.Length > 0)
            _cmbPort.SelectedItem = ports.FirstOrDefault(p => string.Equals(p, "COM10", StringComparison.OrdinalIgnoreCase)) ?? ports[0];

        AppendLog($"Puertos COM disponibles: {(ports.Length > 0 ? string.Join(", ", ports) : "ninguno detectado")}", LogLevel.Info);
    }

    private static int GetPortSortKey(string portName)
    {
        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(portName[3..], out int portNumber))
        {
            return portNumber;
        }

        return int.MaxValue;
    }

    private void SetConnectedState(bool connected)
    {
        _btnConnect.Text     = connected ? "Desconectar" : "Conectar";
        _btnConnect.BackColor = connected ? Color.FromArgb(196, 43, 28) : Color.FromArgb(0, 120, 215);
        _lblStatusDot.ForeColor = connected ? Color.LimeGreen : Color.Red;
        _lblStatusText.Text  = connected ? "Conectado" : "Desconectado";
        _tslConnStatus.Text  = connected ? "● Conectado" : "● Desconectado";
        _tslConnStatus.ForeColor = connected ? Color.Green : Color.Red;
    }

    /// <summary>
    /// Sets the pending UID (from a real physical NFC read).
    /// The Register button is ONLY enabled when this is non-null.
    /// </summary>
    private void SetPendingUid(string? uid)
    {
        _pendingUid          = uid;
        _txtLastUid.Text     = uid ?? "(ninguna)";
        _btnRegister.Enabled = uid != null;

        if (uid != null)
        {
            _lblRegisterHint.Text      = $"✅ Tarjeta lista. Pulsa \"Registrar Tarjeta\" para guardarla en el sistema.";
            _lblRegisterHint.ForeColor = Color.FromArgb(39, 174, 96);
        }
        else
        {
            _lblRegisterHint.Text      = "⚠  Acerca una tarjeta NFC al lector Arduino para habilitar el registro.";
            _lblRegisterHint.ForeColor = Color.FromArgb(127, 140, 141);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // HELPERS — LOGGING
    // ═════════════════════════════════════════════════════════════════════

    private enum LogLevel { Info, Success, Error, Warning, Nfc }

    private void AppendLog(string message, LogLevel level)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message, level)); return; }

        string ts     = DateTime.Now.ToString("HH:mm:ss");
        string prefix = level switch
        {
            LogLevel.Success => "[OK]  ",
            LogLevel.Error   => "[ERR] ",
            LogLevel.Warning => "[WARN]",
            LogLevel.Nfc     => "[NFC] ",
            _                => "[INFO]"
        };
        Color color = level switch
        {
            LogLevel.Success => Color.FromArgb(87, 242, 135),
            LogLevel.Error   => Color.FromArgb(255, 100, 100),
            LogLevel.Warning => Color.FromArgb(255, 200, 80),
            LogLevel.Nfc     => Color.FromArgb(80, 200, 255),
            _                => Color.FromArgb(180, 190, 200)
        };

        _rtbLogs.SelectionStart  = _rtbLogs.TextLength;
        _rtbLogs.SelectionLength = 0;

        _rtbLogs.SelectionColor = Color.FromArgb(90, 110, 130);
        _rtbLogs.AppendText($"{ts} ");

        _rtbLogs.SelectionColor = color;
        _rtbLogs.AppendText($"{prefix}  {message}\n");

        _rtbLogs.ScrollToCaret();
    }

    // ═════════════════════════════════════════════════════════════════════
    // HELPERS — FACTORY
    // ═════════════════════════════════════════════════════════════════════

    private static GroupBox MakeGroupBox(string title) => new()
    {
        Text      = title,
        Dock      = DockStyle.Fill,
        ForeColor = Color.FromArgb(44, 62, 80),
        Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text     = text,
        Location = new Point(x, y),
        AutoSize = true,
        Font     = new Font("Segoe UI", 9f)
    };

    private static Label MakeFlowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 4, 6, 0),
        Font = new Font("Segoe UI", 9f)
    };

    private static Button MakeButton(string text, Point location, Size size, Color backColor)
    {
        var btn = new Button
        {
            Text      = text,
            Location  = location,
            Size      = size,
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor    = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}
