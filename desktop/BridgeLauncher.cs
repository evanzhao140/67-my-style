using System.Diagnostics;

namespace PlantPulse;

/// <summary>Finds, launches and detects the Plant Bridge (BLE relay) process.</summary>
internal static class BridgeLauncher
{
    public static bool IsRunning()
    {
        try
        {
            return Process.GetProcessesByName("PlantBridge").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Looks for the bridge next to the repo (works from bin/ and publish/).</summary>
    public static string Find()
    {
        DirectoryInfo dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "bridge", "publish", "PlantBridge.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        string home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "MCVibe", "bridge", "publish", "PlantBridge.exe");
        return File.Exists(home) ? home : null;
    }

    /// <summary>Starts the bridge. Returns false and sets <paramref name="error"/> on failure.</summary>
    public static bool Start(out string error)
    {
        error = null;
        if (IsRunning()) return true;

        string path = Find();
        if (path == null)
        {
            error = "PlantBridge.exe was not found. Build it once with:\n" +
                    "cd bridge\ndotnet publish -c Release -o publish";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true,
            });
            Log.Line("bridge: started " + path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
