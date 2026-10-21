using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PlantPulse;

/// <summary>
/// Sketch editor for a one-bar custom texture. Drag across the 16 columns to
/// draw an intensity shape, then play it as taps or as a smooth continuous bed.
/// </summary>
public sealed class SketchForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 14, 18);
    private static readonly Color Panel = Color.FromArgb(26, 26, 34);
    private static readonly Color Border = Color.FromArgb(52, 52, 64);
    private static readonly Color Dim = Color.FromArgb(150, 150, 166);

    private static readonly int[] HighEnergy =
    {
        100, 85, 90, 85, 100, 85, 95, 85,
        100, 90, 90, 85, 100, 85, 95, 90,
    };

    private readonly Settings _settings;
    private readonly SketchEngine _engine;
    private readonly int[] _steps;
    private readonly SketchPad _pad = new();
    private readonly TrackBar _bpm = new();
    private readonly Label _bpmValue = new();
    private readonly CheckBox _smooth = new();
    private readonly Button _play = new();
    private Button _clear;
    private Button _high;
    private Button _close;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ToolTip _tips = new();

    public SketchForm(HapticServer server, Settings settings)
    {
        _settings = settings;
        _engine = new SketchEngine(server);
        _steps = settings.SketchSteps is { Length: SketchEngine.Columns }
            ? (int[])settings.SketchSteps.Clone()
            : new int[SketchEngine.Columns];

        Text = "Draw custom — Plant Pulse";
        ClientSize = new Size(704, 580);
        BackColor = Canvas;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = SystemIcons.Application;
        DoubleBuffered = true;

        BuildHeader();
        BuildPad();
        BuildSpeed();
        BuildActions();
        BuildPlay();
        BuildFooter();

        _timer.Interval = 40;
        _timer.Tick += (_, _) =>
        {
            if (_engine.Running)
            {
                _pad.Playhead = _engine.Progress;
                _pad.Invalidate();
            }
            else if (_pad.Playhead >= 0)
            {
                _pad.Playhead = -1;
                _pad.Invalidate();
            }
        };
        _timer.Start();

        FormClosing += OnClosing;
    }

    private void BuildHeader()
    {
        AddLabel("Draw custom", 24, 14, 420, 34, 20f, Color.White, FontStyle.Bold);
        AddLabel("drag to sculpt a loop · height = strength", 26, 50, 520, 20, 8.8f, Dim);
    }

    private void BuildPad()
    {
        _pad.Left = 24;
        _pad.Top = 82;
        _pad.Width = 656;
        _pad.Height = 272;
        _pad.Steps = _steps;
        _pad.Smooth = _settings.SketchSmooth;
        _pad.AccessibleName = "Pattern drawing pad";
        _pad.AccessibleDescription = "Drag across the columns to draw intensity";
        _pad.Changed += (_, _) =>
        {
            if (_engine.Running) StopPlayback();
        };
        Controls.Add(_pad);
    }

    private void BuildSpeed()
    {
        AddLabel("Speed", 24, 368, 100, 22, 9.5f, Color.White, FontStyle.Bold);

        _bpmValue.Left = 380;
        _bpmValue.Top = 368;
        _bpmValue.Width = 300;
        _bpmValue.Height = 22;
        _bpmValue.TextAlign = ContentAlignment.MiddleRight;
        _bpmValue.ForeColor = Dim;
        Controls.Add(_bpmValue);

        _bpm.Left = 24;
        _bpm.Top = 388;
        _bpm.Width = 656;
        _bpm.Height = 40;
        _bpm.Minimum = 40;
        _bpm.Maximum = 180;
        _bpm.TickFrequency = 10;
        _bpm.TickStyle = TickStyle.None;
        _bpm.SmallChange = 1;
        _bpm.LargeChange = 10;
        _bpm.Value = Math.Clamp(_settings.SketchBpm, _bpm.Minimum, _bpm.Maximum);
        _bpm.ValueChanged += (_, _) =>
        {
            _bpmValue.Text = _bpm.Value + " BPM";
            _settings.SketchBpm = _bpm.Value;
            _engine.UpdateBpm(_bpm.Value);
        };
        Controls.Add(_bpm);
        _bpmValue.Text = _bpm.Value + " BPM";
    }

    private void BuildActions()
    {
        _smooth.Left = 24;
        _smooth.Top = 441;
        _smooth.Width = 180;
        _smooth.Height = 24;
        _smooth.Text = "Smooth continuous";
        _smooth.FlatStyle = FlatStyle.Flat;
        _smooth.BackColor = Canvas;
        _smooth.ForeColor = Color.FromArgb(220, 220, 230);
        _smooth.Font = new Font("Segoe UI", 9f);
        _smooth.Checked = _settings.SketchSmooth;
        _smooth.CheckedChanged += (_, _) =>
        {
            _settings.SketchSmooth = _smooth.Checked;
            _pad.Smooth = _smooth.Checked;
            _pad.Invalidate();
            if (_engine.Running) StopPlayback();
        };
        Controls.Add(_smooth);

        _clear = MakeButton("Clear", 212, 438, 76, 28);
        _clear.Click += (_, _) =>
        {
            if (_engine.Running) StopPlayback();
            Array.Clear(_steps, 0, _steps.Length);
            _pad.Invalidate();
        };
        Controls.Add(_clear);

        _high = MakeButton("High energy", 296, 438, 118, 28);
        _high.Click += (_, _) =>
        {
            if (_engine.Running) StopPlayback();
            Array.Copy(HighEnergy, _steps, _steps.Length);
            _pad.Invalidate();
        };
        Controls.Add(_high);

        Label hint = AddLabel("16 steps · 4 beats", 430, 442, 250, 20, 8f, Dim);
        hint.TextAlign = ContentAlignment.MiddleRight;

        _tips.SetToolTip(_smooth, "Continuous vibration instead of separate taps");
        _tips.SetToolTip(_high, "Fill the bar with 85–100% hits");
        _tips.SetToolTip(_clear, "Erase the drawing");
        _tips.SetToolTip(_pad, "Drag to draw; height is strength");
    }

    private void BuildPlay()
    {
        _play.Left = 24;
        _play.Top = 476;
        _play.Width = 656;
        _play.Height = 48;
        _play.FlatStyle = FlatStyle.Flat;
        _play.FlatAppearance.BorderSize = 0;
        _play.FlatAppearance.MouseOverBackColor = Color.FromArgb(86, 56, 132);
        _play.BackColor = Color.FromArgb(70, 44, 110);
        _play.ForeColor = Color.White;
        _play.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _play.Cursor = Cursors.Hand;
        _play.Text = "▶   Play drawing";
        _play.Click += (_, _) => TogglePlay();
        Controls.Add(_play);
    }

    private void BuildFooter()
    {
        AddLabel("Drag across the grid. Each column is one 16th note.",
            24, 536, 500, 20, 8f, Color.FromArgb(122, 122, 138));

        _close = MakeButton("Close", 560, 528, 120, 34);
        _close.Click += (_, _) => Close();
        Controls.Add(_close);
    }

    private Button MakeButton(string text, int left, int top, int width, int height)
    {
        Button button = new()
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 36, 48);
        return button;
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

    private void TogglePlay()
    {
        if (_engine.Running)
        {
            StopPlayback();
            return;
        }

        if (_steps.All(value => value < 8))
        {
            MessageBox.Show(this, "Draw a few bars first — drag across the grid to set the shape.",
                "Plant Pulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _settings.SketchSteps = (int[])_steps.Clone();
        _settings.SketchBpm = _bpm.Value;
        _settings.SketchSmooth = _smooth.Checked;
        _settings.Save();

        _engine.Start(_steps, _bpm.Value, _smooth.Checked);
        _play.Text = "■   Stop drawing";
        _play.BackColor = Color.FromArgb(88, 48, 76);
        _pad.Playhead = 0;
        _pad.Invalidate();
    }

    private void StopPlayback()
    {
        _engine.Stop();
        _pad.Playhead = -1;
        _pad.Invalidate();
        _play.Text = "▶   Play drawing";
        _play.BackColor = Color.FromArgb(70, 44, 110);
    }

    private void OnClosing(object sender, FormClosingEventArgs e)
    {
        _timer.Stop();
        _engine.Stop();
        _settings.SketchSteps = (int[])_steps.Clone();
        _settings.SketchBpm = _bpm.Value;
        _settings.SketchSmooth = _smooth.Checked;
        _settings.Save();
        _engine.Dispose();
    }

    /// <summary>Dark grid where the pattern is drawn; columns map to 16th notes.</summary>
    private sealed class SketchPad : Panel
    {
        private static readonly Color PadCanvas = Color.FromArgb(17, 17, 23);
        private static readonly Color Grid = Color.FromArgb(38, 38, 48);
        private static readonly Color BeatGrid = Color.FromArgb(60, 60, 76);
        private static readonly Color PadBorder = Color.FromArgb(52, 52, 64);

        private bool _painting;
        private int _lastStep = -1;
        private int _lastValue;
        private int _hover = -1;

        public int[] Steps;
        public double Playhead = -1;
        public bool Smooth;
        public event EventHandler Changed;

        public SketchPad()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint, true);
            BackColor = PadCanvas;
            Cursor = Cursors.Cross;
            TabStop = false;
        }

        private static int PlotTop => 12;
        private int PlotBottom => Math.Max(PlotTop + 1, Height - 14);

        private int YFor(int value) =>
            (int)Math.Round(PlotBottom - (PlotBottom - PlotTop)
                * Math.Clamp(value, 0, 100) / 100.0);

        private int StepAt(int x) => Math.Clamp(
            (int)(x * (double)SketchEngine.Columns / Math.Max(1, Width)),
            0, SketchEngine.Columns - 1);

        private int ValueAt(int y)
        {
            double value = (PlotBottom - y) * 100.0 / Math.Max(1, PlotBottom - PlotTop);
            return Math.Clamp((int)Math.Round(value), 0, 100);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(PadCanvas);

            double columnWidth = Width / (double)SketchEngine.Columns;

            using (Pen grid = new(Grid))
            using (Pen beat = new(BeatGrid))
            {
                for (int quarter = 25; quarter <= 100; quarter += 25)
                {
                    int y = YFor(quarter);
                    g.DrawLine(quarter == 100 ? beat : grid, 0, y, Width, y);
                }

                for (int i = 1; i < SketchEngine.Columns; i++)
                {
                    int x = (int)Math.Round(i * columnWidth);
                    g.DrawLine(i % 4 == 0 ? beat : grid, x, 0, x, Height);
                }
            }

            if (_hover >= 0)
            {
                int x = (int)(_hover * columnWidth);
                using SolidBrush hover = new(Color.FromArgb(22, 255, 255, 255));
                g.FillRectangle(hover, x + 1, 1, (int)Math.Ceiling(columnWidth) - 2, Height - 2);
            }

            for (int i = 0; i < SketchEngine.Columns; i++)
            {
                int value = Steps != null ? Steps[i] : 0;
                int barWidth = Math.Max(5, (int)(columnWidth * 0.58));
                int x = (int)Math.Round(i * columnWidth + (columnWidth - barWidth) / 2.0);
                int y = YFor(value);
                int height = PlotBottom - y;

                if (value < 1)
                {
                    using SolidBrush rest = new(Color.FromArgb(48, 48, 62));
                    g.FillRectangle(rest, x, PlotBottom - 2, barWidth, 2);
                    continue;
                }

                using (SolidBrush bar = new(BarColor(value)))
                {
                    g.FillRectangle(bar, x, y, barWidth, height);
                }
                using (Pen cap = new(CapColor(value), 2f))
                {
                    g.DrawLine(cap, x, y, x + barWidth, y);
                }
            }

            using (Pen shape = new(Color.FromArgb(Smooth ? 135 : 60, 214, 194, 255), Smooth ? 1.8f : 1.1f))
            {
                if (Smooth) shape.DashStyle = DashStyle.Dot;
                PointF[] points = new PointF[SketchEngine.Columns];
                for (int i = 0; i < points.Length; i++)
                {
                    int value = Steps != null ? Steps[i] : 0;
                    points[i] = new PointF(
                        (float)((i + 0.5) * columnWidth), YFor(value));
                }
                g.DrawLines(shape, points);

                using SolidBrush dot = new(Color.FromArgb(Smooth ? 190 : 100, 220, 200, 255));
                foreach (PointF point in points)
                {
                    g.FillEllipse(dot, point.X - 2f, point.Y - 2f, 4f, 4f);
                }
            }

            if (Playhead >= 0)
            {
                int x = (int)Math.Clamp(Playhead * Width, 0, Width - 1);
                using (Pen glow = new(Color.FromArgb(50, 120, 220, 140), 7f))
                using (Pen line = new(Color.FromArgb(225, 120, 220, 140), 2f))
                {
                    g.DrawLine(glow, x, 0, x, Height);
                    g.DrawLine(line, x, 0, x, Height);
                }
            }

            if (_hover >= 0 && Steps != null)
            {
                string text = $"step {_hover + 1}  ·  {Steps[_hover]}%";
                using Font font = new("Segoe UI", 8.5f, FontStyle.Bold);
                SizeF size = g.MeasureString(text, font);
                RectangleF textBox = new(Width - size.Width - 12, 4, size.Width, size.Height);
                using (SolidBrush back = new(Color.FromArgb(205, 14, 14, 18)))
                {
                    g.FillRectangle(back, textBox);
                }
                using (SolidBrush brush = new(Color.FromArgb(235, 235, 245)))
                using (StringFormat format = new() { Alignment = StringAlignment.Far })
                {
                    g.DrawString(text, font, brush,
                        new RectangleF(0, 4, Width - 8, 18), format);
                }
            }

            using Pen border = new(PadBorder);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        private static Color BarColor(int value)
        {
            if (value >= 80) return Color.FromArgb(196, 148, 255);
            if (value >= 50) return Color.FromArgb(146, 100, 228);
            if (value >= 20) return Color.FromArgb(96, 72, 166);
            return Color.FromArgb(68, 58, 106);
        }

        private static Color CapColor(int value)
        {
            if (value >= 80) return Color.FromArgb(238, 220, 255);
            if (value >= 50) return Color.FromArgb(190, 164, 245);
            return Color.FromArgb(120, 100, 180);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _painting = true;
            Capture = true;
            Apply(e.X, e.Y, true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int step = StepAt(e.X);
            if (step != _hover)
            {
                _hover = step;
                Invalidate();
            }
            if (_painting) Apply(e.X, e.Y, false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            _painting = false;
            Capture = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_painting) return;
            _hover = -1;
            Invalidate();
        }

        private void Apply(int x, int y, bool first)
        {
            int step = StepAt(x);
            int value = ValueAt(y);

            if (first || _lastStep < 0 || step == _lastStep)
            {
                Steps[step] = value;
            }
            else
            {
                int distance = Math.Abs(step - _lastStep);
                int direction = Math.Sign(step - _lastStep);
                for (int i = 1; i <= distance; i++)
                {
                    int target = _lastStep + direction * i;
                    double t = i / (double)distance;
                    Steps[target] = (int)Math.Round(_lastValue + (value - _lastValue) * t);
                }
            }

            _lastStep = step;
            _lastValue = value;
            Changed?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }
}
