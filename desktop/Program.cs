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
