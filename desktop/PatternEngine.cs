using System.Diagnostics;

namespace PlantPulse;

/// <summary>
/// Generates the haptic textures and streams them through <see cref="HapticServer"/>.
/// One dedicated thread with a Stopwatch keeps the timing tight and drift-free;
/// no WinForms timers are involved, and switching patterns/stopping is instant.
/// </summary>
public sealed class PatternEngine : IDisposable
{
    public static readonly (string Name, string Hint)[] Patterns =
    {
        ("Heartbeat", "lub-dub with a soft tick"),
        ("Groove", "kick, snare and hats at your tempo"),
        ("Warp", "accelerating taps into a drop"),
        ("Rain", "scattered soft droplets"),
        ("Purr", "continuous motor wobble"),
        ("Riser", "swell, silence, then a drop"),
    };

    private readonly HapticServer _server;
    private readonly object _lifecycle = new();
    private Thread _thread;
    private volatile bool _running;
    private volatile int _pattern;
    private volatile int _bpm = 90;
    private volatile int _intensity = 75;

    public PatternEngine(HapticServer server) => _server = server;

    public bool Running => _running;
    public int CurrentPattern => _pattern;

    public void Start(int pattern, int bpm, int intensity)
    {
        lock (_lifecycle)
        {
            _pattern = Math.Clamp(pattern, 0, Patterns.Length - 1);
            _bpm = Math.Clamp(bpm, 40, 200);
            _intensity = Math.Clamp(intensity, 10, 100);
            if (_running) return;

            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "pulse-pattern" };
            _thread.Start();
        }
    }

    public void Update(int bpm, int intensity)
    {
        _bpm = Math.Clamp(bpm, 40, 200);
        _intensity = Math.Clamp(intensity, 10, 100);
    }

    public void Stop()
    {
        Thread thread;
        lock (_lifecycle)
        {
            if (!_running) return;
            _running = false;
            thread = _thread;
        }

        _server.ConstantEnd("purr");
        _server.ConstantEnd("riser");

        if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
        {
            thread.Join(2000);
        }
        lock (_lifecycle) _thread = null;
    }

    public void Dispose() => Stop();

    private void Run()
    {
        while (_running)
        {
            try
            {
                switch (_pattern)
                {
                    case 0: Heartbeat(); break;
                    case 1: Groove(); break;
                    case 2: Warp(); break;
                    case 3: Rain(); break;
                    case 4: Purr(); break;
                    case 5: Riser(); break;
                    default: return;
                }
            }
            catch (Exception ex)
            {
                Log.Line("pattern: " + ex.Message);
                Thread.Sleep(300);
            }
        }

        _server.ConstantEnd("purr");
        _server.ConstantEnd("riser");
    }

    // MARK: - Textures

    private void Heartbeat()
    {
        while (_running && _pattern == 0)
        {
            double beat = Math.Max(0.40, 60.0 / _bpm);
            int level = _intensity;
            double gap = Math.Min(0.24, beat * 0.34);

            _server.Burst("heart_lub", level, 150, 2);
            if (!WaitSeconds(gap) || _pattern != 0) return;

            _server.Burst("heart_dub", Math.Max(8, (int)(level * 0.62)), 120, 2);
            if (!WaitSeconds(Math.Max(0.1, beat * 0.55 - gap)) || _pattern != 0) return;

            _server.Burst("heart_tick", Math.Max(5, (int)(level * 0.14)), 40, 1);
            if (!WaitSeconds(Math.Max(0.08, beat * 0.45))) return;
        }
    }

    private void Groove()
    {
        while (_running && _pattern == 1)
        {
            double step = 60.0 / _bpm / 4.0;
            int level = _intensity;
            for (int i = 0; i < 16 && _running && _pattern == 1; i++)
            {
                if (i == 0 || i == 7 || i == 10)
                {
                    _server.Burst("groove_kick", level, 180, 2);
                }
                if (i == 4 || i == 12)
                {
                    _server.Burst("groove_snare", Math.Max(8, (int)(level * 0.72)), 160, 3);
                }
                if (i == 15)
                {
                    _server.Burst("groove_open", Math.Max(6, (int)(level * 0.5)), 220, 2);
                }
                if (i % 2 == 0)
                {
                    _server.Burst("groove_hat", Math.Max(5, (int)(level * 0.28)), 45, 1);
                }
                if (i % 4 == 2)
                {
                    _server.Burst("groove_tick", Math.Max(4, (int)(level * 0.18)), 35, 1);
                }
                if (!WaitSeconds(step)) return;
            }
        }
    }

    private void Warp()
    {
        while (_running && _pattern == 2)
        {
            double gap = Math.Max(0.11, 60.0 / _bpm / 2.0);
            int level = _intensity;
            for (int i = 0; i < 26 && _running && _pattern == 2; i++)
            {
                double x = i / 25.0;
                int tickLevel = (int)Math.Round(10 + (level - 10) * Math.Pow(x, 1.4));
                _server.Burst("warp_tick", tickLevel, 60, 1);
                if (!WaitSeconds(gap)) return;
                gap = Math.Max(0.075, gap * 0.88);
            }
            if (!_running || _pattern != 2) return;
            _server.Burst("warp_drop", level, 520, 3);
            if (!WaitSeconds(1.0)) return;
        }
    }

    private void Rain()
    {
        Random random = new();
        while (_running && _pattern == 3)
        {
            for (int i = 0; i < 24 && _running && _pattern == 3; i++)
            {
                int level = random.Next(8, Math.Max(12, _intensity));
                int duration = 50 + random.Next(0, 80);
                _server.Burst("rain_drop", level, duration, 1);
                if (!WaitSeconds((70 + random.Next(0, 260)) / 1000.0)) return;
            }
            if (!WaitSeconds(0.9)) return;
        }
    }

    private void Purr()
    {
        try
        {
            double t = 0;
            while (_running && _pattern == 4)
            {
                double wobble = 0.5 + 0.5 * Math.Sin(t * Math.PI * 2 * 6.0);
                int level = (int)Math.Round(_intensity * (0.62 + 0.38 * wobble));
                _server.Constant("purr", Math.Clamp(level, 1, 100));
                Thread.Sleep(110);
                t += 0.11;
            }
        }
        finally
        {
            _server.ConstantEnd("purr");
        }
    }

    private void Riser()
    {
        try
        {
            while (_running && _pattern == 5)
            {
                const double ramp = 6.0;
                long start = Stopwatch.GetTimestamp();
                while (_running && _pattern == 5)
                {
                    double elapsed = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
                    if (elapsed >= ramp) break;
                    double x = elapsed / ramp;
                    int level = (int)Math.Round(4 + (_intensity - 4) * Math.Pow(x, 1.7));
                    _server.Constant("riser", Math.Clamp(level, 1, 100));
                    Thread.Sleep(90);
                }

                _server.ConstantEnd("riser");
                if (!_running || _pattern != 5) return;

                if (!WaitSeconds(0.25)) return;
                _server.Burst("riser_drop", _intensity, 650, 3);
                if (!WaitSeconds(0.9)) return;
            }
        }
        finally
        {
            _server.ConstantEnd("riser");
        }
    }

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
