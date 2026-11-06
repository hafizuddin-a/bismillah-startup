using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bismillah;

/// <summary>The few Windows calls WPF doesn't wrap.</summary>
internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    public const int VK_ESCAPE = 0x1B;
    public const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    public const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int WTSSessionInfoEx = 25;

    // display on/off, from "power setting" notifications: 0 off, 1 on, 2 dimmed
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);
    [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);

    /// <summary>
    /// Why the session counts as locked right now, or "" if it doesn't. Windows 10/11 show the lock screen in two
    /// stages: first a picture (LockApp.exe, on the normal desktop, with the session not yet flagged locked) and only
    /// after a key or click the password/PIN box. A fingerprint can unlock straight from the picture. So several
    /// independent signals are checked:
    ///  - the foreground window is the lock screen picture (LockApp.exe);
    ///  - the input desktop is not ours to open (the password/PIN screen);
    ///  - Windows' sign-in UI (LogonUI.exe) is running in this session;
    ///  - the session's own state flag says locked (WTSSessionInfoEx; documented, Windows 10 and later).
    /// </summary>
    public static string LockState()
    {
        if (ForegroundProgram() == "LockApp") return "lock screen";
        if (InputDesktopIsSecure()) return "secure desktop";
        if (LogonUiRunning()) return "sign-in UI";
        if (SessionFlagSaysLocked()) return "session flag";
        return "";
    }

    /// <summary>True while the lock screen, the sign-in screen (password, PIN, fingerprint) or another secure desktop is up.</summary>
    public static bool IsSessionLocked() => LockState() != "";

    private static bool InputDesktopIsSecure()
    {
        IntPtr desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (desktop == IntPtr.Zero) return true;
        CloseDesktop(desktop);
        return false;
    }

    private static bool LogonUiRunning()
    {
        try
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("LogonUI"))
            {
                using (process) { try { if (process.SessionId == session) return true; } catch { } }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// The SessionFlags field of WTSINFOEX_LEVEL1: 0 = locked, 1 = unlocked, 0xFFFFFFFF = unknown (which must not count
    /// as locked). The struct is read by offset (Level at 0, then the level-1 block at 8: SessionId, SessionState,
    /// SessionFlags), and trusted only if its SessionId matches ours.
    /// </summary>
    public static int? SessionFlag()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint session = (uint)Process.GetCurrentProcess().SessionId;
            if (!WTSQuerySessionInformationW(IntPtr.Zero, session, WTSSessionInfoEx, out buffer, out uint bytes) || bytes < 20) return null;
            if (Marshal.ReadInt32(buffer, 0) != 1 || Marshal.ReadInt32(buffer, 8) != (int)session) return null;
            return Marshal.ReadInt32(buffer, 16);
        }
        catch { return null; }
        finally { if (buffer != IntPtr.Zero) WTSFreeMemory(buffer); }
    }

    private static bool SessionFlagSaysLocked() => SessionFlag() == 0;

    /// <summary>The taskbar exists, so the desktop has finished loading (it doesn't yet while Windows is still signing in).</summary>
    public static bool ShellReady => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>The program of the window the user is in, e.g. "StartMenuExperienceHost" while the Start menu is open.</summary>
    public static string ForegroundProgram()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            if (pid == 0) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch { return ""; }
    }

    /// <summary>True while the Start menu or search is open: a popup centred on the screen would be half hidden behind it.</summary>
    public static bool StartMenuOpen() =>
        ForegroundProgram() is "StartMenuExperienceHost" or "SearchHost" or "SearchApp" or "ShellExperienceHost";

    /// <summary>The scale factor (1.0 = 100%) of the monitor containing this screen's centre.</summary>
    public static double ScaleOf(System.Windows.Forms.Screen screen)
    {
        var centre = new POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
        IntPtr monitor = MonitorFromPoint(centre, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0) return dpiX / 96.0;
        return 1.0;
    }
}
