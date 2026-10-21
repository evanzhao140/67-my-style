using System.Drawing;
using System.Windows.Forms;

namespace PlantPulse;

/// <summary>
/// Compact dark control panel: pick a looping texture, tune tempo and
/// intensity, and watch the phone/bridge connection state.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 14, 18);
    private static readonly Color Panel = Color.FromArgb(26, 26, 34);
    private static readonly Color Border = Color.FromArgb(52, 52, 64);
    private static readonly Color Accent = Color.FromArgb(168, 120, 255);
    private static readonly Color Dim = Color.FromArgb(150, 150, 166);
    private static readonly Color Good = Color.FromArgb(120, 220, 140);
    private static readonly Color Warn = Color.FromArgb(240, 180, 90);

    private readonly Settings _settings;
    private readonly HapticServer _server;
    private readonly PatternEngine _engine;
    private readonly List<Button> _patternButtons = new();

    private readonly Label _phoneStatus = new();
    private readonly Label _bridgeStatus = new();
    private readonly Label _bpmValue = new();
    private readonly Label _intensityValue = new();
    private readonly TrackBar _bpm = new();
    private readonly TrackBar _intensity = new();
    private readonly Button _playButton = new();
    private readonly Button _bridgeButton = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    private int _selected;
    private OsuSyncForm _osuForm;

    public MainForm()
    {
        _settings = Settings.Load();
        _selected = Math.Clamp(_settings.Pattern, 0, PatternEngine.Patterns.Length - 1);

        Text = "Plant Pulse";
        ClientSize = new Size(552, 630);
        BackColor = Canvas;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        DoubleBuffered = true;

        BuildHeader();
        BuildStatusRow();
        BuildPatternGrid();
        BuildSliders();
        BuildPlayButton();
        BuildBridgeRow();
        BuildFooter();

        _server = new HapticServer();
        _server.Start();
        _engine = new PatternEngine(_server);

        _timer.Interval = 500;
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        StylePatternButtons();
        RefreshStatus();
        FormClosing += OnClosing;
    }

    private void BuildHeader()
    {
        AddLabel("Plant Pulse", 24, 14, 360, 34, 20f, Color.White, FontStyle.Bold);
        AddLabel("haptic pattern transmitter", 26, 50, 400, 20, 8.8f, Dim);

        Button osuButton = new()
        {
            Text = "osu! sync",
            Left = 408,
            Top = 16,
            Width = 120,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
        };
        osuButton.FlatAppearance.BorderColor = Border;
        osuButton.FlatAppearance.BorderSize = 1;
        osuButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 36, 48);
        osuButton.Click += (_, _) => OpenOsuSync();
        Controls.Add(osuButton);
    }

    private void BuildStatusRow()
    {
        _phoneStatus.Left = 24;
        _phoneStatus.Top = 78;
        _phoneStatus.Width = 300;
        _phoneStatus.Height = 20;
        _phoneStatus.Font = new Font("Segoe UI", 8.5f);
        _phoneStatus.ForeColor = Dim;
        Controls.Add(_phoneStatus);

        _bridgeStatus.Left = 276;
        _bridgeStatus.Top = 78;
        _bridgeStatus.Width = 252;
        _bridgeStatus.Height = 20;
        _bridgeStatus.TextAlign = ContentAlignment.MiddleRight;
        _bridgeStatus.Font = new Font("Segoe UI", 8.5f);
        _bridgeStatus.ForeColor = Dim;
        Controls.Add(_bridgeStatus);
    }

    private void BuildPatternGrid()
    {
        for (int i = 0; i < PatternEngine.Patterns.Length; i++)
        {
            int index = i;
            (string name, string hint) = PatternEngine.Patterns[i];

            Button button = new()
            {
                Text = name + "\n" + hint,
                Left = 24 + (i % 2) * 258,
                Top = 112 + (i / 2) * 72,
                Width = 246,
                Height = 62,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Panel,
                ForeColor = Color.FromArgb(220, 220, 230),
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand,
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 36, 48);
            button.Click += (_, _) => SelectPattern(index);

            Controls.Add(button);
            _patternButtons.Add(button);
        }
    }

    private void BuildSliders()
    {
        AddLabel("Tempo", 24, 334, 140, 22, 9.5f, Color.White, FontStyle.Bold);
        _bpmValue.Left = 380;
        _bpmValue.Top = 334;
        _bpmValue.Width = 148;
        _bpmValue.Height = 22;
        _bpmValue.TextAlign = ContentAlignment.MiddleRight;
        _bpmValue.ForeColor = Dim;
        Controls.Add(_bpmValue);

        _bpm.Left = 24;
        _bpm.Top = 354;
        _bpm.Width = 504;
        _bpm.Height = 42;
        _bpm.Minimum = 40;
        _bpm.Maximum = 180;
        _bpm.TickFrequency = 10;
        _bpm.TickStyle = TickStyle.None;
        _bpm.SmallChange = 1;
        _bpm.LargeChange = 10;
        _bpm.Value = Math.Clamp(_settings.Bpm, _bpm.Minimum, _bpm.Maximum);
        _bpm.ValueChanged += (_, _) =>
        {
            _bpmValue.Text = _bpm.Value + " BPM";
            if (_engine != null) _engine.Update(_bpm.Value, _intensity.Value);
        };
        Controls.Add(_bpm);
        _bpmValue.Text = _bpm.Value + " BPM";

        AddLabel("Intensity", 24, 400, 140, 22, 9.5f, Color.White, FontStyle.Bold);
        _intensityValue.Left = 380;
        _intensityValue.Top = 400;
        _intensityValue.Width = 148;
        _intensityValue.Height = 22;
        _intensityValue.TextAlign = ContentAlignment.MiddleRight;
        _intensityValue.ForeColor = Dim;
        Controls.Add(_intensityValue);

        _intensity.Left = 24;
        _intensity.Top = 420;
        _intensity.Width = 504;
        _intensity.Height = 42;
        _intensity.Minimum = 10;
        _intensity.Maximum = 100;
        _intensity.TickFrequency = 10;
        _intensity.TickStyle = TickStyle.None;
        _intensity.SmallChange = 1;
        _intensity.LargeChange = 10;
        _intensity.Value = Math.Clamp(_settings.Intensity, _intensity.Minimum, _intensity.Maximum);
        _intensity.ValueChanged += (_, _) =>
        {
            _intensityValue.Text = _intensity.Value + "%";
            if (_engine != null) _engine.Update(_bpm.Value, _intensity.Value);
        };
        Controls.Add(_intensity);
        _intensityValue.Text = _intensity.Value + "%";
    }

    private void BuildPlayButton()
    {
        _playButton.Left = 24;
        _playButton.Top = 472;
        _playButton.Width = 504;
        _playButton.Height = 46;
        _playButton.FlatStyle = FlatStyle.Flat;
        _playButton.FlatAppearance.BorderSize = 0;
        _playButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(86, 56, 132);
        _playButton.BackColor = Color.FromArgb(70, 44, 110);
        _playButton.ForeColor = Color.White;
        _playButton.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _playButton.Cursor = Cursors.Hand;
        _playButton.Click += (_, _) =>
        {
            if (_engine.Running) StopPlayback();
            else StartSelected();
        };
        Controls.Add(_playButton);
    }

    private void BuildBridgeRow()
    {
        AddLabel("Bluetooth: start the bridge, then connect in the app.",
            24, 532, 320, 34, 8.4f, Dim);

        _bridgeButton.Left = 348;
        _bridgeButton.Top = 530;
        _bridgeButton.Width = 180;
        _bridgeButton.Height = 34;
        _bridgeButton.FlatStyle = FlatStyle.Flat;
        _bridgeButton.FlatAppearance.BorderColor = Border;
        _bridgeButton.FlatAppearance.BorderSize = 1;
        _bridgeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 36, 48);
        _bridgeButton.BackColor = Panel;
        _bridgeButton.ForeColor = Color.White;
        _bridgeButton.Cursor = Cursors.Hand;
        _bridgeButton.Click += (_, _) => StartBridge();
        Controls.Add(_bridgeButton);
    }

    private void BuildFooter()
    {
        AddLabel("Wi-Fi: the phone finds “Plant Pulse” on the same network.",
            24, 576, 504, 18, 8f, Color.FromArgb(122, 122, 138));
        AddLabel("Bluetooth: Plant Bridge relays port 25666 over BLE.",
            24, 594, 504, 18, 8f, Color.FromArgb(122, 122, 138));
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

    private void SelectPattern(int index)
    {
        if (_engine.Running && _selected == index)
        {
            StopPlayback();
            return;
        }
        _selected = index;
        _settings.Pattern = index;
        _settings.Save();
        StartSelected();
    }

    private void StartSelected()
    {
        _engine.Start(_selected, _bpm.Value, _intensity.Value);
        StylePatternButtons();
        RefreshStatus();
    }

    private void StopPlayback()
    {
        _engine.Stop();
        _server.Clear();
        StylePatternButtons();
        RefreshStatus();
    }

    private void StylePatternButtons()
    {
        for (int i = 0; i < _patternButtons.Count; i++)
        {
            bool active = i == _selected && _engine.Running;
            Button button = _patternButtons[i];
            button.BackColor = active ? Color.FromArgb(48, 32, 72) : Panel;
            button.FlatAppearance.BorderColor = active ? Accent : Border;
            button.FlatAppearance.BorderSize = active ? 2 : 1;
            button.ForeColor = active ? Color.White : Color.FromArgb(220, 220, 230);
        }
    }

    private void RefreshStatus()
    {
        int clients = _server.ClientCount;
        if (_server.TcpReady)
        {
            _phoneStatus.Text = clients > 0
                ? $"● Phone connected ({clients})"
                : "○ No phone connected";
            _phoneStatus.ForeColor = clients > 0 ? Good : Dim;
        }
        else
        {
            _phoneStatus.Text = "Waiting for port 25666 (is a game using it?)";
            _phoneStatus.ForeColor = Warn;
        }

        bool bridge = BridgeLauncher.IsRunning();
        _bridgeStatus.Text = bridge ? "Bluetooth bridge running" : "Bluetooth bridge off";
        _bridgeStatus.ForeColor = bridge ? Good : Dim;
        _bridgeButton.Text = bridge ? "Bridge running" : "Start Bluetooth bridge";
        _bridgeButton.Enabled = !bridge;

        (string name, _) = PatternEngine.Patterns[
            _engine.Running ? _engine.CurrentPattern : _selected];
        _playButton.Text = _engine.Running ? $"■   Stop   {name}" : $"▶   Play   {name}";
    }

    private void StartBridge()
    {
        if (!BridgeLauncher.Start(out string error))
        {
            MessageBox.Show(this, error, "Plant Pulse",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshStatus();
    }

    private void OpenOsuSync()
    {
        if (_osuForm != null && !_osuForm.IsDisposed)
        {
            _osuForm.Activate();
            return;
        }

        if (_engine.Running) StopPlayback();

        _osuForm = new OsuSyncForm(_server, _settings);
        _osuForm.FormClosed += (_, _) => _osuForm = null;
        _osuForm.Show(this);
    }

    private void OnClosing(object sender, FormClosingEventArgs e)
    {
        _timer.Stop();

        if (_osuForm != null && !_osuForm.IsDisposed) _osuForm.Close();

        _engine.Stop();
        _server.Clear();
        _server.Dispose();

        _settings.Pattern = _selected;
        _settings.Bpm = _bpm.Value;
        _settings.Intensity = _intensity.Value;
        _settings.Save();
    }
}
