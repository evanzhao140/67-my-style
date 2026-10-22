using System.Drawing;
using System.Windows.Forms;

namespace PlantPulse;

/// <summary>
/// Music sync panel: direct Windows loopback, a tempo-following beat, and a
/// continuous bed that never drops below 50% while listening.
/// </summary>
public sealed class MusicSyncForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 14, 18);
    private static readonly Color Panel = Color.FromArgb(26, 26, 34);
    private static readonly Color Border = Color.FromArgb(52, 52, 64);
    private static readonly Color Accent = Color.FromArgb(168, 120, 255);
    private static readonly Color Dim = Color.FromArgb(150, 150, 166);
    private static readonly Color Good = Color.FromArgb(120, 220, 140);
    private static readonly Color Warn = Color.FromArgb(240, 180, 90);

    private readonly HapticServer _server;
    private readonly Settings _settings;
    private readonly MusicSyncEngine _engine;
    private readonly bool _persist;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ToolTip _tips = new();

    private readonly Label _state = new();
    private readonly Label _phone = new();
    private readonly Label _device = new();
    private readonly Label _hint = new();
    private readonly Label _floorHint = new();
    private readonly Button _enableButton = new();
    private readonly Button _bridgeButton = new();
    private readonly Label _bridgeStatus = new();
    private readonly MusicMeter _meter = new();
    private readonly TrackBar _swell = new();
    private readonly TrackBar _punch = new();
    private readonly TrackBar _lead = new();
    private readonly Label _swellValue = new();
    private readonly Label _punchValue = new();
    private readonly Label _leadValue = new();

    private bool _listening;
    private bool _closing;

    public MusicSyncForm(HapticServer server, Settings settings, bool persist = true)
    {
        _server = server;
        _settings = settings;
        _persist = persist;
        _engine = new MusicSyncEngine(server);
        _engine.SetOptions(settings.MusicSwell, settings.MusicPunch, settings.MusicLead);

        Text = "Music sync — Plant Pulse";
        ClientSize = new Size(552, 596);
        BackColor = Canvas;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = SystemIcons.Application;
        DoubleBuffered = true;

        BuildHeader();
        BuildStatusRow();
        BuildEnableButton();
        BuildMeter();
        BuildSliders();
        BuildBridgeRow();
        BuildFooter();

        _timer.Interval = 180;
        _timer.Tick += (_, _) => RefreshUi();
        _timer.Start();

        Shown += (_, _) =>
        {
            CenterOverOwner();
            if (_settings.MusicEnabled) BeginInvoke(new Action(StartListening));
        };
        FormClosing += OnClosing;
        RefreshUi();
    }

    private void CenterOverOwner()
    {
        if (Owner == null || Owner.WindowState == FormWindowState.Minimized) return;

        Rectangle area = Screen.FromControl(Owner).WorkingArea;
        int x = Owner.Left + (Owner.Width - Width) / 2;
        int y = Owner.Top + (Owner.Height - Height) / 2;
        Left = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void BuildHeader()
    {
        AddLabel("Music sync", 24, 14, 420, 34, 20f, Color.White, FontStyle.Bold);
        AddLabel("system audio → phone · tempo + volume · direct loopback",
            26, 50, 500, 20, 8.8f, Dim);
    }

    private void BuildStatusRow()
    {
        _state.Left = 24;
        _state.Top = 74;
        _state.Width = 250;
        _state.Height = 20;
        _state.Font = new Font("Segoe UI", 8.5f);
        _state.ForeColor = Dim;
        Controls.Add(_state);

        _phone.Left = 276;
        _phone.Top = 74;
        _phone.Width = 252;
        _phone.Height = 20;
        _phone.TextAlign = ContentAlignment.MiddleRight;
        _phone.Font = new Font("Segoe UI", 8.5f);
        _phone.ForeColor = Dim;
        Controls.Add(_phone);
    }

    private void BuildEnableButton()
    {
        _enableButton.Left = 24;
        _enableButton.Top = 100;
        _enableButton.Width = 504;
        _enableButton.Height = 50;
        _enableButton.FlatStyle = FlatStyle.Flat;
        _enableButton.FlatAppearance.BorderSize = 1;
        _enableButton.BackColor = Panel;
        _enableButton.ForeColor = Color.White;
        _enableButton.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _enableButton.Cursor = Cursors.Hand;
        _enableButton.Click += (_, _) => ToggleListening();
        Controls.Add(_enableButton);
    }

    private void BuildMeter()
    {
        _meter.Left = 24;
        _meter.Top = 164;
        _meter.Width = 504;
        _meter.Height = 90;
        _meter.AccessibleName = "Music sync levels";
        Controls.Add(_meter);

        _device.Left = 24;
        _device.Top = 258;
        _device.Width = 504;
        _device.Height = 18;
        _device.Font = new Font("Segoe UI", 8.2f);
        _device.ForeColor = Dim;
        _device.AutoEllipsis = true;
        Controls.Add(_device);
    }

    private void BuildSliders()
    {
        AddLabel("Volume swell", 24, 282, 240, 22, 9.5f, Color.White, FontStyle.Bold);
        _swellValue.Left = 380;
        _swellValue.Top = 282;
        _swellValue.Width = 148;
        _swellValue.Height = 22;
        _swellValue.TextAlign = ContentAlignment.MiddleRight;
        _swellValue.ForeColor = Dim;
        Controls.Add(_swellValue);

        _swell.Left = 24;
        _swell.Top = 302;
        _swell.Width = 504;
        _swell.Height = 38;
        _swell.Minimum = 0;
        _swell.Maximum = 40;
        _swell.TickFrequency = 5;
        _swell.TickStyle = TickStyle.None;
        _swell.SmallChange = 1;
        _swell.LargeChange = 5;
        _swell.Value = Math.Clamp(_settings.MusicSwell, _swell.Minimum, _swell.Maximum);
        _swell.ValueChanged += (_, _) => PushOptions();
        _swell.AccessibleName = "Volume swell";
        Controls.Add(_swell);
        _swellValue.Text = _swell.Value + "%";

        AddLabel("Beat strength", 24, 344, 240, 22, 9.5f, Color.White, FontStyle.Bold);
        _punchValue.Left = 380;
        _punchValue.Top = 344;
        _punchValue.Width = 148;
        _punchValue.Height = 22;
        _punchValue.TextAlign = ContentAlignment.MiddleRight;
        _punchValue.ForeColor = Dim;
        Controls.Add(_punchValue);

        _punch.Left = 24;
        _punch.Top = 364;
        _punch.Width = 504;
        _punch.Height = 38;
        _punch.Minimum = 55;
        _punch.Maximum = 100;
        _punch.TickFrequency = 5;
        _punch.TickStyle = TickStyle.None;
        _punch.SmallChange = 1;
        _punch.LargeChange = 5;
        _punch.Value = Math.Clamp(_settings.MusicPunch, _punch.Minimum, _punch.Maximum);
        _punch.ValueChanged += (_, _) => PushOptions();
        _punch.AccessibleName = "Beat strength";
        Controls.Add(_punch);
        _punchValue.Text = _punch.Value + "%";

        AddLabel("Timing lead", 24, 406, 260, 22, 9.5f, Color.White, FontStyle.Bold);
        _leadValue.Left = 380;
        _leadValue.Top = 406;
        _leadValue.Width = 148;
        _leadValue.Height = 22;
        _leadValue.TextAlign = ContentAlignment.MiddleRight;
        _leadValue.ForeColor = Dim;
        Controls.Add(_leadValue);

        _lead.Left = 24;
        _lead.Top = 426;
        _lead.Width = 504;
        _lead.Height = 38;
        _lead.Minimum = 0;
        _lead.Maximum = 200;
        _lead.TickFrequency = 25;
        _lead.TickStyle = TickStyle.None;
        _lead.SmallChange = 5;
        _lead.LargeChange = 25;
        _lead.Value = Math.Clamp(_settings.MusicLead, _lead.Minimum, _lead.Maximum);
        _lead.ValueChanged += (_, _) => PushOptions();
        _lead.AccessibleName = "Timing lead";
        Controls.Add(_lead);
        _leadValue.Text = _lead.Value + " ms";

        _hint.Left = 24;
        _hint.Top = 466;
        _hint.Width = 504;
        _hint.Height = 20;
        _hint.Font = new Font("Segoe UI", 8.3f);
        _hint.ForeColor = Dim;
        _hint.AutoEllipsis = true;
        Controls.Add(_hint);

        _floorHint.Left = 24;
        _floorHint.Top = 486;
        _floorHint.Width = 504;
        _floorHint.Height = 20;
        _floorHint.Font = new Font("Segoe UI", 8.2f);
        _floorHint.ForeColor = Color.FromArgb(122, 122, 138);
        _floorHint.Text = "The bed never drops below 50% while listening.";
        Controls.Add(_floorHint);

        _tips.SetToolTip(_swell, "How much volume lifts the always-on bed above its 50% floor.");
        _tips.SetToolTip(_punch, "Peak strength of the tempo beats.");
        _tips.SetToolTip(_lead,
            "Sends tempo beats early to compensate for Bluetooth/phone delay. " +
            "Use 0 ms for Wi-Fi if the hits feel early.");
    }

    private void BuildBridgeRow()
    {
        _bridgeStatus.Left = 24;
        _bridgeStatus.Top = 516;
        _bridgeStatus.Width = 320;
        _bridgeStatus.Height = 24;
        _bridgeStatus.Font = new Font("Segoe UI", 8.5f);
        _bridgeStatus.ForeColor = Dim;
        Controls.Add(_bridgeStatus);

        _bridgeButton.Left = 380;
        _bridgeButton.Top = 512;
        _bridgeButton.Width = 148;
        _bridgeButton.Height = 32;
        StyleSmallButton(_bridgeButton, "Start bridge");
        _bridgeButton.Click += (_, _) => StartBridge();
        Controls.Add(_bridgeButton);
    }

    private void BuildFooter()
    {
        AddLabel("Captures Windows system audio directly — no tosu or websocket.",
            24, 552, 504, 18, 8f, Color.FromArgb(122, 122, 138));
        AddLabel("Bluetooth: Plant Bridge relays the same phone connection.",
            24, 570, 504, 18, 8f, Color.FromArgb(122, 122, 138));
    }

    private void StyleSmallButton(Button button, string text)
    {
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 36, 48);
        button.BackColor = Panel;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 8.5f);
        button.Cursor = Cursors.Hand;
    }

    private Label AddLabel(string text, int left, int top, int width, int height,
        float size, Color color, FontStyle style = FontStyle.Regular)
    {
        Label label = new()
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ForeColor = color,
            Font = new Font("Segoe UI", size, style),
        };
        Controls.Add(label);
        return label;
    }

    private void PushOptions()
    {
        _swellValue.Text = _swell.Value + "%";
        _punchValue.Text = _punch.Value + "%";
        _leadValue.Text = _lead.Value + " ms";
        _settings.MusicSwell = _swell.Value;
        _settings.MusicPunch = _punch.Value;
        _settings.MusicLead = _lead.Value;
        _engine.SetOptions(_swell.Value, _punch.Value, _lead.Value);
    }

    private void ToggleListening()
    {
        if (_listening || _engine.Running) StopListening();
        else StartListening();
    }

    private void StartListening()
    {
        _listening = false;
        if (!_engine.Start(out string error))
        {
            _settings.MusicEnabled = false;
            if (_persist) _settings.Save();
            _state.Text = "○ couldn't start";
            _state.ForeColor = Warn;
            _hint.Text = string.IsNullOrWhiteSpace(error)
                ? "Audio capture could not start."
                : error;
            _hint.ForeColor = Warn;
            RefreshUi();
            return;
        }

        _listening = true;
        _settings.MusicEnabled = true;
        if (_persist) _settings.Save();
        RefreshUi();
    }

    private void StopListening()
    {
        _engine.Stop();
        _listening = false;
        _settings.MusicEnabled = false;
        if (_persist) _settings.Save();
        RefreshUi();
    }

    private void RefreshUi()
    {
        MusicStatus status = _engine.GetStatus();

        if (!status.Running && _listening)
        {
            // The device disappeared or capture aborted. Stop the bed and let
            // the next Start retry with a clean capture object.
            _listening = false;
            _settings.MusicEnabled = false;
            if (_persist) _settings.Save();
            _engine.Stop();
            status = _engine.GetStatus();
        }

        int clients = _server.ClientCount;
        _phone.Text = clients > 0
            ? $"● phone connected ({clients})"
            : "○ no phone";
        _phone.ForeColor = clients > 0 ? Good : Dim;

        if (status.Running)
        {
            string tempo = status.TempoLocked ? status.Bpm + " BPM" : "finding tempo";
            _state.Text = "● listening · " + tempo;
            _state.ForeColor = Good;
            _device.Text = $"{status.Device} · {status.BufferMs} ms capture";
            _device.ForeColor = Dim;
            _hint.Text = status.TempoLocked
                ? "Tempo locked from the music. Louder passages lift the bed and peaks."
                : "Locking onto the beat… the bed is already on.";
            _hint.ForeColor = Dim;
            _tips.SetToolTip(_hint, "");
        }
        else
        {
            _state.Text = "○ stopped";
            _state.ForeColor = Dim;
            _device.Text = "Default Windows output";
            _device.ForeColor = Dim;
            if (!string.IsNullOrWhiteSpace(status.Error))
            {
                _hint.Text = status.Error;
                _hint.ForeColor = Warn;
                _tips.SetToolTip(_hint, status.Error);
            }
            else
            {
                _hint.Text = "Play music in any app; the bed stays at 50%+ while listening.";
                _hint.ForeColor = Dim;
                _tips.SetToolTip(_hint, "");
            }
        }

        bool live = status.Running;
        _enableButton.Text = live
            ? "■   Listening — click to stop"
            : "▶   Start music sync";
        _enableButton.BackColor = live ? Color.FromArgb(70, 44, 110) : Panel;
        _enableButton.FlatAppearance.BorderColor = live ? Accent : Border;
        _enableButton.FlatAppearance.MouseOverBackColor = live
            ? Color.FromArgb(86, 56, 132)
            : Color.FromArgb(36, 36, 48);

        _meter.SetStatus(status);

        bool bridge = BridgeLauncher.IsRunning();
        _bridgeStatus.Text = bridge ? "Bluetooth bridge running" : "Bluetooth bridge off";
        _bridgeStatus.ForeColor = bridge ? Good : Dim;
        _bridgeButton.Text = bridge ? "Bridge running" : "Start bridge";
        _bridgeButton.ForeColor = bridge ? Good : Color.White;
        _bridgeButton.FlatAppearance.BorderColor = bridge
            ? Color.FromArgb(62, 96, 72)
            : Border;
        _bridgeButton.Cursor = bridge ? Cursors.Default : Cursors.Hand;
    }

    private void StartBridge()
    {
        if (BridgeLauncher.IsRunning())
        {
            RefreshUi();
            return;
        }

        if (!BridgeLauncher.Start(out string error))
        {
            MessageBox.Show(this, error, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshUi();
    }

    private void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        _closing = true;

        _timer.Stop();
        _settings.MusicSwell = _swell.Value;
        _settings.MusicPunch = _punch.Value;
        _settings.MusicLead = _lead.Value;
        _settings.MusicEnabled = _listening || _engine.Running;
        if (_persist) _settings.Save();

        _engine.Dispose();
        _tips.Dispose();
    }
}

/// <summary>Three compact live bars: volume, the 50%-floored bed, and peaks.</summary>
internal sealed class MusicMeter : Control
{
    private static readonly Color Track = Color.FromArgb(42, 42, 54);
    private static readonly Color Purple = Color.FromArgb(168, 120, 255);
    private static readonly Color Green = Color.FromArgb(120, 220, 140);
    private static readonly Color Orange = Color.FromArgb(240, 180, 90);
    private static readonly Color Muted = Color.FromArgb(150, 150, 166);

    private readonly Font _labelFont = new("Segoe UI", 7.8f);
    private readonly Font _valueFont = new("Segoe UI", 8f, FontStyle.Bold);
    private readonly Font _bpmFont = new("Segoe UI", 9.5f, FontStyle.Bold);

    private MusicStatus _status;

    public MusicMeter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(26, 26, 34);
        AccessibleRole = AccessibleRole.Indicator;
    }

    public void SetStatus(MusicStatus status)
    {
        _status = status;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        using (Pen border = new(Color.FromArgb(52, 52, 64)))
        {
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        bool live = _status.Running;
        Color liveColor = live ? Green : Muted;
        TextRenderer.DrawText(g, live ? "● LIVE" : "○ IDLE", _labelFont,
            new Point(10, 7), liveColor);
        TextRenderer.DrawText(g, _status.TempoLocked ? _status.Bpm + " BPM" : "finding tempo",
            _bpmFont, new Rectangle(Width - 150, 5, 138, 18),
            _status.TempoLocked ? Color.White : Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        DrawBar(g, "VOLUME", _status.Volume, 29, Purple, false);
        DrawBar(g, "BED", _status.Bed, 49, Green, true);
        DrawBar(g, "PEAK", _status.Peak, 69, Orange, false);
    }

    private void DrawBar(Graphics g, string label, int value, int top, Color color, bool floor)
    {
        value = Math.Clamp(value, 0, 100);
        int trackLeft = 78;
        int trackWidth = 330;
        int trackHeight = 10;

        TextRenderer.DrawText(g, label, _labelFont, new Point(10, top - 2), Muted);
        using (SolidBrush background = new(Track))
        {
            g.FillRectangle(background, trackLeft, top, trackWidth, trackHeight);
        }

        int fill = (int)Math.Round(trackWidth * value / 100.0);
        if (fill > 0)
        {
            using SolidBrush brush = new(color);
            g.FillRectangle(brush, trackLeft, top, fill, trackHeight);
        }

        if (floor)
        {
            int half = trackLeft + trackWidth / 2;
            using Pen marker = new(Color.FromArgb(120, 255, 255, 255), 1);
            g.DrawLine(marker, half, top - 1, half, top + trackHeight);
        }

        TextRenderer.DrawText(g, value + "%", _valueFont,
            new Rectangle(418, top - 4, 74, 18),
            Color.FromArgb(220, 220, 230),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont.Dispose();
            _valueFont.Dispose();
            _bpmFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
