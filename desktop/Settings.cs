using System.Text.Json;

namespace PlantPulse;

/// <summary>Small persisted UI state (pattern, tempo, intensity).</summary>
public sealed class Settings
{
    public int Pattern { get; set; }
    public int Bpm { get; set; } = 90;
    public int Intensity { get; set; } = 75;

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlantPulse");

    public static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Settings settings = JsonSerializer.Deserialize<Settings>(
                    File.ReadAllText(FilePath), Options);
                if (settings != null)
                {
                    settings.Bpm = Math.Clamp(settings.Bpm, 40, 180);
                    settings.Intensity = Math.Clamp(settings.Intensity, 10, 100);
                    settings.Pattern = Math.Clamp(settings.Pattern, 0, PatternEngine.Patterns.Length - 1);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Line("settings: could not read (" + ex.Message + ")");
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Line("settings: could not save (" + ex.Message + ")");
        }
    }
}

public static class Log
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Settings.Dir, "log.txt");

    public static void Line(string message)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        try
        {
            Console.WriteLine(line);
        }
        catch
        {
        }
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Dir);
                FileInfo info = new(LogPath);
                if (info.Exists && info.Length > 2_000_000) info.Delete();
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
