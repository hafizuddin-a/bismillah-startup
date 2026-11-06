using Microsoft.Win32;

namespace Bismillah;

/// <summary>"Launch at Login": a value in the current user's Run key. Needs no admin rights.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Bismillah";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>The command Windows runs at sign-in. --login tells the app it was started that way.</summary>
    private static string Command => $"\"{Environment.ProcessPath}\" --login";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>If Launch at Login is on, makes sure it points at this exe in the current form (an older version wrote it without --login).</summary>
    public static void KeepCurrent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && current != Command) key.SetValue(ValueName, Command);
        }
        catch (Exception e) { Log.Write("couldn't refresh Launch at Login: " + e.Message); }
    }
}
