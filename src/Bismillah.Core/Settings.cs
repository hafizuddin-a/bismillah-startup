using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bismillah;

/// <summary>
/// Everything Bismillah remembers, saved as JSON in %APPDATA%\Bismillah\settings.json.
/// Reads never throw (a missing or damaged file means the defaults) and writes never take the app down.
/// </summary>
public sealed class Settings
{
    // user choices
    public int DhikrPerDay { get; set; }                 // 0 = off
    public int DhikrStartHour { get; set; } = 8;
    public int DhikrEndHour { get; set; } = 22;
    public bool ShowOnAllScreens { get; set; }
    public bool UpdateAutomatically { get; set; } = true;

    // bookkeeping
    public DateTime? LastUpdateCheck { get; set; }
    public string DhikrDay { get; set; } = "";
    public int DhikrShownToday { get; set; }
    public List<DateTime> DhikrPending { get; set; } = [];

    [JsonIgnore] public string? Path { get; private set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bismillah", "settings.json");

    public static Settings Load(string? path = null)
    {
        path ??= DefaultPath;
        Settings settings;
        try
        {
            settings = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new() : new();
        }
        catch (Exception)
        {
            settings = new();
        }
        settings.Path = path;
        return settings;
    }

    public void Save()
    {
        if (Path is null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception)
        {
            // a read-only profile just means the choices aren't remembered
        }
    }
}
