using System.Diagnostics;

namespace PlantPulse;

/// <summary>
/// Plays a one-bar pattern drawn in the sketch editor. Taps mode fires one
/// burst per column; smooth mode streams the interpolated shape as a constant,
/// so drawn swells become a continuous vibration.
/// </summary>
public sealed class SketchEngine : IDisposable
{
    public const int Columns = 16;

    private readonly HapticServer _server;
    private readonly object _lifecycle = new();
    private Thread _thread;
    private int[] _pattern = new int[Columns];
    private volatile bool _running;
    private volatile bool _smooth;
    private volatile int _bpm = 120;
    private long _progressBits;

    public SketchEngine(HapticServer server) => _server = server;

    public bool Running => _running;
    public double Progress => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _progressBits));

    public void Start(int[] steps, int bpm, bool smooth)
    {
        Stop();

        int[] copy = new int[Columns];
        if (steps != null)
        {
            for (int i = 0; i < copy.Length && i < steps.Length; i++)
            {
                copy[i] = Math.Clamp(steps[i], 0, 100);
            }
        }

        lock (_lifecycle)
        {
            _pattern = copy;
            _bpm = Math.Clamp(bpm, 40, 180);
            _smooth = smooth;
            _running = true;
            SetProgress(0);
            _thread = new Thread(Run) { IsBackground = true, Name = "pulse-sketch" };
            _thread.Start();
        }
    }

    public void UpdateBpm(int bpm) => _bpm = Math.Clamp(bpm, 40, 180);

    public void Stop()
    {
        Thread thread;
        lock (_lifecycle)
        {
            _running = false;
            thread = _thread;
        }

        _server.ConstantEnd("sketch_flow");

        if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
        {
            thread.Join(2000);
        }
        lock (_lifecycle)
        {
            if (ReferenceEquals(_thread, thread)) _thread = null;
        }
        SetProgress(0);
    }

    public void Dispose() => Stop();

    private void Run()
    {
        try
        {
            if (_smooth) Smooth();
            else Taps();
        }
        catch (Exception ex)
        {
            Log.Line("sketch: " + ex.Message);
        }
        finally
        {
            _server.ConstantEnd("sketch_flow");
            SetProgress(0);
        }
    }

    private void Taps()
    {
        while (_running)
        {
            for (int i = 0; i < Columns && _running; i++)
            {
                SetProgress(i / (double)Columns);
                int value = _pattern[i];
                if (value >= 8)
                {
                    if (value >= 80) _server.Burst("sketch_accent", value, 140, 2);
                    else _server.Burst("sketch_tap", value, 90, 1);
                }
                if (!WaitSeconds(StepSeconds())) return;
            }
        }
    }

    private void Smooth()
    {
        int last = -1;
        bool active = false;
        while (_running)
        {
            double loop = StepSeconds() * Columns;
            long start = Stopwatch.GetTimestamp();
            while (_running)
            {
                double elapsed = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
                if (elapsed >= loop) break;

                SetProgress(elapsed / loop);
                double position = elapsed / loop * Columns;
                int index = Math.Min(Columns - 1, (int)position);
                int next = (index + 1) % Columns;
                double fraction = position - index;
                int value = (int)Math.Round(
                    _pattern[index] * (1 - fraction) + _pattern[next] * fraction);

                if (value < 4)
                {
                    if (active)
                    {
                        _server.ConstantEnd("sketch_flow");
                        active = false;
                        last = -1;
                    }
                }
                else if (!active || Math.Abs(value - last) >= 3)
                {
                    _server.Constant("sketch_flow", value);
                    active = true;
                    last = value;
                }

                Thread.Sleep(90);
            }
        }
    }

    private double StepSeconds() => Math.Max(0.06, 60.0 / _bpm / 4.0);

    private void SetProgress(double value) =>
        Interlocked.Exchange(ref _progressBits, BitConverter.DoubleToInt64Bits(value));

    /// <summary>Sleeps until the deadline while staying responsive to Stop().</summary>
    private bool WaitSeconds(double seconds)
    {
        if (seconds <= 0) return _running;
        long target = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        while (_running)
        {
            long remaining = target - Stopwatch.GetTimestamp();
            if (remaining <= 0) return true;
            double ms = remaining * 1000.0 / Stopwatch.Frequency;
            if (ms > 4) Thread.Sleep((int)(ms - 2));
            else Thread.SpinWait(80);
        }
        return false;
    }
}
