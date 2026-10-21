using System.Text.Json;

namespace PlantPulse;

/// <summary>Small persisted UI state (pattern, tempo, intensity).</summary>
public sealed class Settings
{
    public int Pattern { get; set; }
    public int Bpm { get; set; } = 90;
    public int Intensity { get; set; } = 75;

    // osu! sync
    public bool OsuSyncEnabled { get; set; } = true;
    public int OsuIntensity { get; set; } = 55;
    public int OsuAccentEvery { get; set; } = 8;
    public bool OsuMissBuzz { get; set; } = true;
    public bool OsuStartPing { get; set; } = true;
    public int OsuFlow { get; set; } = 50;
    public bool OsuSwell { get; set; } = true;

    // Drawn pattern (16 sixteenth-note columns per bar).
    private static readonly int[] DefaultSketch =
    {
        100, 70, 85, 70, 95, 75, 85, 75,
        100, 70, 90, 70, 95, 80, 88, 80,
    };

    public int[] SketchSteps { get; set; } = (int[])DefaultSketch.Clone();
    public int SketchBpm { get; set; } = 120;
    public bool SketchSmooth { get; set; }

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
                    settings.OsuIntensity = Math.Clamp(settings.OsuIntensity, 10, 100);
                    settings.OsuAccentEvery = settings.OsuAccentEvery <= 4 ? 4
                        : settings.OsuAccentEvery <= 8 ? 8
                        : 16;
                    settings.OsuFlow = Math.Clamp(settings.OsuFlow, 0, 90);
                    settings.SketchSteps = NormalizeSketch(settings.SketchSteps);
                    settings.SketchBpm = Math.Clamp(settings.SketchBpm, 40, 180);
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

    private static int[] NormalizeSketch(int[] steps)
    {
        int[] normalized = new int[16];
        int[] source = steps ?? DefaultSketch;
        for (int i = 0; i < normalized.Length; i++)
        {
            normalized[i] = i < source.Length ? Math.Clamp(source[i], 0, 100) : 0;
        }
        return normalized;
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
