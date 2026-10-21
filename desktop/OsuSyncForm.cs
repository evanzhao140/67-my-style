using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PlantPulse;

/// <summary>
/// osu!lazer sync panel. Everything streams through the shared haptic server,
/// so the phone connection is the same one the pattern screen uses.
/// </summary>
public sealed class OsuSyncForm : Form
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
    private readonly OsuSyncEngine _engine;
    private readonly System.Windows.Forms.Timer _timer = new();

    private readonly Label _tosuStatus = new();
    private readonly Label _phoneStatus = new();
    private readonly Label _stateLabel = new();
    private readonly Label _beatmapLabel = new();
    private readonly Label _statsLabel = new();
    private readonly Button _enableButton = new();
    private readonly Button _bridgeButton = new();
    private readonly Label _bridgeStatus = new();
    private readonly Label _helpLabel = new();
    private readonly Button _startTosuButton = new();
    private readonly Button _getTosuButton = new();
    private readonly TrackBar _intensity = new();
    private readonly Label _intensityValue = new();
    private readonly ComboBox _accent = new();
    private readonly CheckBox _missBuzz = new();
    private readonly CheckBox _startPing = new();
    private bool _closing;

    public OsuSyncForm(HapticServer server, Settings settings)
    {
        _server = server;
        _settings = settings;

        Text = "osu! sync — Plant Pulse";
        ClientSize = new Size(552, 524);
        BackColor = Canvas;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = SystemIcons.Application;
        DoubleBuffered = true;

        BuildHeader();
        BuildStatusRow();
        BuildEnableButton();
        BuildInfo();
        BuildOptions();
        BuildTosuHelp();
        BuildBridgeRow();
        BuildFooter();

        _engine = new OsuSyncEngine(server);
        _engine.SetOptions(
            _settings.OsuIntensity,
            _settings.OsuAccentEvery,
            _settings.OsuMissBuzz,
            _settings.OsuStartPing);
        _engine.Enabled = _settings.OsuSyncEnabled;

        _timer.Interval = 300;
        _timer.Tick += (_, _) => RefreshUi();
        _timer.Start();

        RefreshUi();
        FormClosing += OnClosing;
    }

    private void BuildHeader()
    {
        AddLabel("osu! sync", 24, 14, 400, 30, 18f, Color.White, FontStyle.Bold);
        AddLabel("osu!lazer → phone · live hits through tosu", 26, 46, 420, 20, 8.8f, Dim);
    }

    private void BuildStatusRow()
    {
        _tosuStatus.Left = 24;
        _tosuStatus.Top = 74;
        _tosuStatus.Width = 250;
        _tosuStatus.Height = 20;
        _tosuStatus.Font = new Font("Segoe UI", 8.5f);
        _tosuStatus.ForeColor = Dim;
        Controls.Add(_tosuStatus);

        _phoneStatus.Left = 276;
        _phoneStatus.Top = 74;
        _phoneStatus.Width = 252;
        _phoneStatus.Height = 20;
        _phoneStatus.TextAlign = ContentAlignment.MiddleRight;
        _phoneStatus.Font = new Font("Segoe UI", 8.5f);
        _phoneStatus.ForeColor = Dim;
        Controls.Add(_phoneStatus);
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
        _enableButton.Click += (_, _) => ToggleSync();
        Controls.Add(_enableButton);
    }

    private void BuildInfo()
    {
        _stateLabel.Left = 24;
        _stateLabel.Top = 164;
        _stateLabel.Width = 504;
        _stateLabel.Height = 22;
        _stateLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _stateLabel.ForeColor = Dim;
        Controls.Add(_stateLabel);

        _beatmapLabel.Left = 24;
        _beatmapLabel.Top = 186;
        _beatmapLabel.Width = 504;
        _beatmapLabel.Height = 20;
        _beatmapLabel.Font = new Font("Segoe UI", 8.8f);
        _beatmapLabel.ForeColor = Color.FromArgb(220, 220, 230);
        _beatmapLabel.AutoEllipsis = true;
        Controls.Add(_beatmapLabel);

        _statsLabel.Left = 24;
        _statsLabel.Top = 208;
        _statsLabel.Width = 504;
        _statsLabel.Height = 20;
        _statsLabel.Font = new Font("Segoe UI", 8.8f);
        _statsLabel.ForeColor = Dim;
        Controls.Add(_statsLabel);
    }

    private void BuildOptions()
    {
        AddLabel("Hit intensity", 24, 246, 160, 22, 9.5f, Color.White, FontStyle.Bold);
        _intensityValue.Left = 380;
        _intensityValue.Top = 246;
        _intensityValue.Width = 148;
        _intensityValue.Height = 22;
        _intensityValue.TextAlign = ContentAlignment.MiddleRight;
        _intensityValue.ForeColor = Dim;
        Controls.Add(_intensityValue);

        _intensity.Left = 24;
        _intensity.Top = 266;
        _intensity.Width = 504;
        _intensity.Height = 40;
        _intensity.Minimum = 10;
        _intensity.Maximum = 100;
        _intensity.TickFrequency = 10;
        _intensity.TickStyle = TickStyle.None;
        _intensity.SmallChange = 1;
        _intensity.LargeChange = 10;
        _intensity.Value = Math.Clamp(_settings.OsuIntensity, _intensity.Minimum, _intensity.Maximum);
        _intensity.ValueChanged += (_, _) => PushOptions();
        Controls.Add(_intensity);
        _intensityValue.Text = _intensity.Value + "%";

        AddLabel("Accent", 24, 314, 160, 24, 9.5f, Color.White, FontStyle.Bold);

        _accent.Left = 380;
        _accent.Top = 310;
        _accent.Width = 148;
        _accent.Height = 28;
        _accent.DropDownStyle = ComboBoxStyle.DropDownList;
        _accent.FlatStyle = FlatStyle.Flat;
        _accent.BackColor = Panel;
        _accent.ForeColor = Color.White;
        _accent.Items.AddRange(new object[] { "every 4 hits", "every 8 hits", "every 16 hits" });
        _accent.SelectedIndex = _settings.OsuAccentEvery <= 4 ? 0 : _settings.OsuAccentEvery <= 8 ? 1 : 2;
        _accent.SelectedIndexChanged += (_, _) => PushOptions();
        Controls.Add(_accent);

        _missBuzz.Left = 24;
        _missBuzz.Top = 348;
        _missBuzz.Width = 150;
        _missBuzz.Height = 24;
        _missBuzz.Text = "Miss buzz";
        _missBuzz.ForeColor = Color.White;
        _missBuzz.BackColor = Canvas;
        _missBuzz.FlatStyle = FlatStyle.Flat;
        _missBuzz.Checked = _settings.OsuMissBuzz;
        _missBuzz.CheckedChanged += (_, _) => PushOptions();
        Controls.Add(_missBuzz);

        _startPing.Left = 190;
        _startPing.Top = 348;
        _startPing.Width = 150;
        _startPing.Height = 24;
        _startPing.Text = "Start ping";
        _startPing.ForeColor = Color.White;
        _startPing.BackColor = Canvas;
        _startPing.FlatStyle = FlatStyle.Flat;
        _startPing.Checked = _settings.OsuStartPing;
        _startPing.CheckedChanged += (_, _) => PushOptions();
        Controls.Add(_startPing);
    }

    private void BuildTosuHelp()
    {
        _helpLabel.Left = 24;
        _helpLabel.Top = 384;
        _helpLabel.Width = 300;
        _helpLabel.Height = 42;
        _helpLabel.Font = new Font("Segoe UI", 8.4f);
        _helpLabel.ForeColor = Dim;
        Controls.Add(_helpLabel);

        _startTosuButton.Left = 336;
        _startTosuButton.Top = 382;
        _startTosuButton.Width = 90;
        _startTosuButton.Height = 32;
        StyleSmallButton(_startTosuButton, "Start tosu");
        _startTosuButton.Click += (_, _) => StartTosu();
        Controls.Add(_startTosuButton);

        _getTosuButton.Left = 434;
        _getTosuButton.Top = 382;
        _getTosuButton.Width = 94;
        _getTosuButton.Height = 32;
        StyleSmallButton(_getTosuButton, "Get tosu");
        _getTosuButton.Click += (_, _) => OpenTosuSite();
        Controls.Add(_getTosuButton);
    }

    private void BuildBridgeRow()
    {
        _bridgeStatus.Left = 24;
        _bridgeStatus.Top = 428;
        _bridgeStatus.Width = 320;
        _bridgeStatus.Height = 24;
        _bridgeStatus.Font = new Font("Segoe UI", 8.5f);
        _bridgeStatus.ForeColor = Dim;
        Controls.Add(_bridgeStatus);

        _bridgeButton.Left = 380;
        _bridgeButton.Top = 424;
        _bridgeButton.Width = 148;
        _bridgeButton.Height = 32;
        StyleSmallButton(_bridgeButton, "Start bridge");
        _bridgeButton.Click += (_, _) => StartBridge();
        Controls.Add(_bridgeButton);
    }

    private void BuildFooter()
    {
        AddLabel("Wi-Fi: the phone discovers Plant Pulse automatically.",
            24, 470, 504, 18, 8f, Color.FromArgb(122, 122, 138));
        AddLabel("Bluetooth: Plant Bridge relays port 25666 over BLE.",
            24, 488, 504, 18, 8f, Color.FromArgb(122, 122, 138));
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

    private int AccentEvery()
    {
        return _accent.SelectedIndex switch
        {
            0 => 4,
            2 => 16,
            _ => 8,
        };
    }

    private void PushOptions()
    {
        _intensityValue.Text = _intensity.Value + "%";
        _engine?.SetOptions(
            _intensity.Value,
            AccentEvery(),
            _missBuzz.Checked,
            _startPing.Checked);
    }

    private void ToggleSync()
    {
        _engine.Enabled = !_engine.Enabled;
        _settings.OsuSyncEnabled = _engine.Enabled;
        _settings.Save();
        RefreshUi();
    }

    private void RefreshUi()
    {
        bool tosu = _engine.Connected;
        bool precise = _engine.PreciseConnected;

        _tosuStatus.Text = tosu
            ? (precise ? "● tosu · live" : "● tosu · slow feed")
            : "○ tosu not detected";
        _tosuStatus.ForeColor = tosu ? Good : Warn;

        int clients = _server.ClientCount;
        _phoneStatus.Text = clients > 0
            ? $"● phone connected ({clients})"
            : "○ no phone";
        _phoneStatus.ForeColor = clients > 0 ? Good : Dim;

        _enableButton.Text = _engine.Enabled
            ? "■   Sync enabled — click to pause"
            : "▶   Enable osu! sync";
        _enableButton.BackColor = _engine.Enabled
            ? Color.FromArgb(70, 44, 110)
            : Panel;
        _enableButton.FlatAppearance.BorderColor = _engine.Enabled ? Accent : Border;
        _enableButton.FlatAppearance.MouseOverBackColor = _engine.Enabled
            ? Color.FromArgb(86, 56, 132)
            : Color.FromArgb(36, 36, 48);

        bool playing = _engine.Playing;
        _stateLabel.Text = DisplayState(_engine.StateName, playing);
        _stateLabel.ForeColor = playing ? Good : Dim;
        _beatmapLabel.Text = _engine.BeatmapLabel;
        _statsLabel.Text = $"{_engine.Combo}x combo  ·  {_engine.TotalHits} hits  ·  {_engine.Misses} misses";

        _helpLabel.Text = tosu
            ? "tosu is feeding live hits. Keep it running while you play."
            : "tosu is not running. Click Start tosu, then play — it reads the live hits.";
        _helpLabel.ForeColor = tosu ? Good : Warn;
        _startTosuButton.Visible = !tosu;
        _getTosuButton.Visible = !tosu;

        bool bridge = BridgeLauncher.IsRunning();
        _bridgeStatus.Text = bridge ? "Bluetooth bridge running" : "Bluetooth bridge off";
        _bridgeStatus.ForeColor = bridge ? Good : Dim;
        _bridgeButton.Text = bridge ? "Bridge running" : "Start bridge";
        _bridgeButton.Enabled = !bridge;
    }

    private static string DisplayState(string state, bool playing)
    {
        if (playing) return "PLAYING";
        return state switch
        {
            "" => "—",
            "menu" => "menu",
            "selectPlay" => "song select",
            "selectEdit" => "editor select",
            "selectDrawings" => "editor",
            "edit" => "editor",
            "resultScreen" => "results",
            "selectMulti" => "multiplayer",
            "matchSetup" => "multiplayer",
            "rankingVs" => "results",
            "rankingTagCoop" => "results",
            "rankingTeam" => "results",
            "lobby" => "lobby",
            "tourney" => "tournament",
            _ => state,
        };
    }

    private void StartTosu()
    {
        string path = FindTosu();
        if (path == null)
        {
            MessageBox.Show(this,
                "tosu wasn't found on this PC.\n\n" +
                "Click \"Get tosu\" to download it once, then start it before playing.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true,
            });
            Log.Line("osu: started tosu " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not start tosu:\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenTosuSite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://tosu.app",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the tosu site:\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartBridge()
    {
        if (!BridgeLauncher.Start(out string error))
        {
            MessageBox.Show(this, error, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshUi();
    }

    private static string FindTosu()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Portable copy bundled next to the app (kept out of git by desktop/publish/).
        DirectoryInfo dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            string bundled = Path.Combine(dir.FullName, "publish", "tosu", "tosu.exe");
            if (File.Exists(bundled)) return bundled;

            bundled = Path.Combine(dir.FullName, "tosu", "tosu.exe");
            if (File.Exists(bundled)) return bundled;

            dir = dir.Parent;
        }

        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "tosu.exe"),
            Path.Combine(local, "Microsoft", "WinGet", "Links", "tosu.exe"),
            Path.Combine(local, "Programs", "tosu", "tosu.exe"),
            Path.Combine(home, "tosu", "tosu.exe"),
            Path.Combine(home, "Downloads", "tosu", "tosu.exe"),
            Path.Combine(home, "Downloads", "tosu.exe"),
            @"C:\tosu\tosu.exe",
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string folder in pathVariable.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim(), "tosu.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        _closing = true;

        _timer.Stop();
        _settings.OsuSyncEnabled = _engine.Enabled;
        _settings.OsuIntensity = _intensity.Value;
        _settings.OsuAccentEvery = AccentEvery();
        _settings.OsuMissBuzz = _missBuzz.Checked;
        _settings.OsuStartPing = _startPing.Checked;
        _settings.Save();
        _engine.Dispose();
    }
}
