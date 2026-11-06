namespace Bismillah;

/// <summary>A tiny log next to the settings, for working out what went wrong on someone else's PC.</summary>
internal static class Log
{
    public static void Write(string message)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Settings.DefaultPath)!, "bismillah.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
