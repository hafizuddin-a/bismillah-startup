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

    /// <summary>True while the lock screen (or another secure desktop) has the input: nothing of ours can be seen then.</summary>
    public static bool IsSessionLocked()
    {
        IntPtr desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (desktop == IntPtr.Zero) return true;
        CloseDesktop(desktop);
        return false;
    }

    /// <summary>The scale factor (1.0 = 100%) of the monitor containing this screen's centre.</summary>
    public static double ScaleOf(System.Windows.Forms.Screen screen)
    {
        var centre = new POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
        IntPtr monitor = MonitorFromPoint(centre, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0) return dpiX / 96.0;
        return 1.0;
    }
}
