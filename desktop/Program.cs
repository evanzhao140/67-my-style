using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlantPulse;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--test"))
        {
            RunTest();
            return;
        }

        if (args.Contains("--osu-test"))
        {
            RunOsuTest();
            return;
        }

        if (args.Contains("--osu-ui-test"))
        {
            RunOsuUiTest();
            return;
        }

        if (args.Contains("--sketch-test"))
        {
            RunSketchTest();
            return;
        }

        if (args.Contains("--music-test"))
        {
            RunMusicTest();
            return;
        }

        if (args.Contains("--music-device-test"))
        {
            int seconds = 9;
            int index = Array.IndexOf(args, "--music-device-test");
            if (index >= 0 && index + 1 < args.Length &&
                int.TryParse(args[index + 1], out int requested))
            {
                seconds = Math.Clamp(requested, 3, 120);
            }
            RunMusicDeviceTest(seconds);
            return;
        }

        if (args.Contains("--music-ui-test"))
        {
            RunMusicUiTest();
            return;
        }

        if (args.Contains("--sketch-ui-test"))
        {
            RunSketchUiTest();
            return;
        }

        HideConsole();

        using Mutex mutex = new(true, "PlantPulse.Transmitter", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Plant Pulse is already running.",
                "Plant Pulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }

    private static void HideConsole()
    {
        try
        {
            IntPtr handle = GetConsoleWindow();
            if (handle != IntPtr.Zero) ShowWindow(handle, 0);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Headless smoke test: serves the protocol and plays each texture for a
    /// couple of seconds so a logging client can verify the stream.
    /// </summary>
    private static void RunTest()
    {
        Log.Line("selftest: starting");
        using HapticServer server = new();
        server.Start();
        using PatternEngine engine = new(server);

        Console.WriteLine("listening; connect within 3 s...");
        Thread.Sleep(3000);

        for (int i = 0; i < PatternEngine.Patterns.Length; i++)
        {
            Console.WriteLine($"pattern {PatternEngine.Patterns[i].Name}");
            engine.Start(i, 96, i == 6 ? 100 : 72);
            Thread.Sleep(4500);
            engine.Stop();
            Thread.Sleep(300);
        }

        server.Clear();
        Thread.Sleep(400);
        Log.Line("selftest: done");
    }

    /// <summary>
    /// Headless osu! sync test. Expects a mock tosu server on 24051 that speaks
    /// the v2 / v2-precise websocket payloads.
    /// </summary>
    private static void RunOsuTest()
    {
        Log.Line("osutest: starting");
        using HapticServer server = new();
        server.Start();

        using OsuSyncEngine engine = new(server, 24051);
        engine.SetOptions(70, 4, true, true, 50, true);
        engine.Enabled = true;

        Thread.Sleep(20000);
        engine.Enabled = false;
        server.Clear();
        Thread.Sleep(300);

        Log.Line($"osutest: done state={engine.StateName} beatmap={engine.BeatmapLabel} " +
                 $"combo={engine.Combo} hits={engine.TotalHits} misses={engine.Misses} " +
                 $"event={engine.LastEvent}");
    }

    /// <summary>Opens the osu! sync window for three seconds to smoke-test its layout.</summary>
    private static void RunOsuUiTest()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using HapticServer server = new();
        server.Start();

        Settings settings = Settings.Load();
        using OsuSyncForm form = new(server, settings);
        using System.Windows.Forms.Timer timer = new() { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            form.Close();
        };
        timer.Start();

        Application.Run(form);
        Log.Line("osu-ui-test: window opened and closed cleanly");
        server.Clear();
    }

    /// <summary>
    /// Pure analysis test: feeds a synthetic 128 BPM kick/pad track through the
    /// same analyzer the live loopback uses, then checks tempo, volume response
    /// and the 50% bed floor. No audio device or phone is required.
    /// </summary>
    private static void RunMusicTest()
    {
        Log.Line("musictest: starting");
        const int sampleRate = 48000;
        const double bpm = 128;
        const double beat = 60.0 / bpm;

        MusicAnalyzer analyzer = new(sampleRate);
        int onsets = 0;
        analyzer.Onset += _ => Interlocked.Increment(ref onsets);

        const int blockSize = 480;
        float[] block = new float[blockSize];
        Random random = new();
        double[] volumes = new double[10];
        double[] beds = new double[10];
        int[] bpms = new int[10];
        bool[] locks = new bool[10];
        int blockIndex = 0;

        for (int second = 0; second < 10; second++)
        {
            for (int b = 0; b < 100; b++)
            {
                double start = (double)blockIndex * blockSize / sampleRate;
                bool silent = second >= 9;

                for (int i = 0; i < blockSize; i++)
                {
                    double t = start + i / (double)sampleRate;
                    float sample = 0;

                    if (!silent)
                    {
                        double amp = second < 3 ? 0.015 : second < 6 ? 0.060 : 0.018;
                        sample += (float)(Math.Sin(2 * Math.PI * 55 * t) * amp);

                        double beatPosition = (t - 0.25) % beat;
                        if (beatPosition < 0) beatPosition += beat;
                        if (beatPosition < 0.09)
                        {
                            double decay = Math.Exp(-beatPosition / 0.035);
                            double kickAmp = second < 3 ? 0.16 : second < 6 ? 0.42 : 0.18;
                            sample += (float)(Math.Sin(2 * Math.PI * 62 * t) * kickAmp * decay);
                        }

                        if (beatPosition < 0.008)
                        {
                            sample += (float)((random.NextDouble() * 2 - 1)
                                * (second < 3 ? 0.03 : second < 6 ? 0.09 : 0.035));
                        }

                        // A quiet background so "silence" never has a fake beat.
                        sample += (float)((random.NextDouble() * 2 - 1) * 0.0004);
                    }

                    block[i] = Math.Clamp(sample, -1f, 1f);
                }

                analyzer.Process(block, blockSize);
                blockIndex++;
            }

            MusicSnapshot snapshot = analyzer.Snapshot();
            volumes[second] = snapshot.Volume;
            beds[second] = MusicSyncEngine.BedForVolume(30, snapshot.Volume);
            bpms[second] = snapshot.Bpm;
            locks[second] = snapshot.TempoLocked;

            Console.WriteLine(
                $"t={second + 1,2}s  vol={snapshot.Volume * 100,5:0.0}%  " +
                $"bed={MusicSyncEngine.BedForVolume(30, snapshot.Volume),3}%  " +
                $"bpm={(snapshot.TempoLocked ? snapshot.Bpm.ToString() : "—"),4}  " +
                $"onsets={snapshot.OnsetCount}");
        }

        int lockedBpm = bpms[5] != 0 ? bpms[5] : bpms[4] != 0 ? bpms[4] : bpms[6];
        bool tempoOk = locks[4] && locks[5] && Math.Abs(lockedBpm - bpm) <= 4;
        bool floorOk = MusicSyncEngine.BedForVolume(30, 0) == 50
            && MusicSyncEngine.BedForVolume(0, 1) == 50;
        bool volumeOk = volumes[4] > volumes[2] + 0.05
            && volumes[6] < volumes[4] - 0.05;

        Console.WriteLine($"tempo lock: {(tempoOk ? "PASS" : "FAIL")} ({lockedBpm} BPM)");
        Console.WriteLine($"volume response: {(volumeOk ? "PASS" : "FAIL")}");
        Console.WriteLine($"50% bed floor: {(floorOk ? "PASS" : "FAIL")}");
        Console.WriteLine($"onsets captured: {onsets}");

        Log.Line($"musictest: done tempo={(tempoOk ? "pass" : "fail")} " +
                 $"volume={(volumeOk ? "pass" : "fail")} floor={(floorOk ? "pass" : "fail")}");
    }

    /// <summary>Attempts a real loopback capture and reports the device/error.</summary>
    private static void RunMusicDeviceTest(int seconds)
    {
        Log.Line("musicdev: starting");
        using HapticServer server = new();
        server.Start();
        using MusicSyncEngine engine = new(server);
        engine.SetOptions(30, 84, 40);

        bool started = engine.Start(out string error);
        Console.WriteLine(started
            ? $"music capture started on {engine.DeviceName}"
            : "music capture failed: " + error);
        if (started)
        {
            Thread.Sleep(seconds * 1000);
            MusicStatus status = engine.GetStatus();
            Console.WriteLine($"running={status.Running} bpm={status.Bpm} " +
                              $"locked={status.TempoLocked} vol={status.Volume} " +
                              $"bed={status.Bed} beat={status.Peak}");
            engine.Stop();
        }
        Log.Line("musicdev: done started=" + started);
    }

    /// <summary>Opens the music panel for a few seconds to smoke-test its layout.</summary>
    private static void RunMusicUiTest()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using HapticServer server = new();
        server.Start();

        Settings settings = Settings.Load();
        settings.MusicEnabled = false;
        using MusicSyncForm form = new(server, settings, persist: false);
        using System.Windows.Forms.Timer timer = new() { Interval = 6000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            form.Close();
        };
        timer.Start();

        Application.Run(form);
        Log.Line("music-ui-test: window opened and closed cleanly");
        server.Clear();
    }

    /// <summary>
    /// Headless drawing test: streams a high-energy sketch as taps, then as a
    /// smooth bed, so a TCP client can verify both modes and their ids.
    /// </summary>
    private static void RunSketchTest()
    {
        Log.Line("sketcht: starting");
        using HapticServer server = new();
        server.Start();
        using SketchEngine engine = new(server);

        int[] pattern =
        {
            100, 85, 90, 85, 100, 85, 95, 85,
            100, 90, 90, 85, 100, 85, 95, 90,
        };

        Console.WriteLine("sketch taps; connect a client (up to 20 s)...");
        for (int i = 0; i < 200 && server.ClientCount == 0; i++) Thread.Sleep(100);
        Thread.Sleep(300);
        engine.Start(pattern, 120, false);
        Thread.Sleep(6000);
        engine.Stop();
        Thread.Sleep(300);

        Console.WriteLine("sketch smooth");
        engine.Start(pattern, 120, true);
        Thread.Sleep(6000);
        engine.Stop();
        server.Clear();
        Thread.Sleep(300);

        Log.Line("sketcht: done");
    }

    /// <summary>Opens the drawing editor for twelve seconds to smoke-test its layout.</summary>
    private static void RunSketchUiTest()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using HapticServer server = new();
        server.Start();

        Settings settings = Settings.Load();
        using SketchForm form = new(server, settings);
        using System.Windows.Forms.Timer timer = new() { Interval = 12000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            form.Close();
        };
        timer.Start();

        Application.Run(form);
        Log.Line("sketch-ui-test: window opened and closed cleanly");
        server.Clear();
    }
}
