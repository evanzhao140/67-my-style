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
            engine.Start(i, 96, 72);
            Thread.Sleep(4500);
            engine.Stop();
            Thread.Sleep(300);
        }

        server.Clear();
        Thread.Sleep(400);
        Log.Line("selftest: done");
    }
}
