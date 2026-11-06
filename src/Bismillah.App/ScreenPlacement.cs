using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Bismillah;

/// <summary>Puts a borderless window on a particular screen, in physical pixels, whatever each monitor's scaling.</summary>
internal static class ScreenPlacement
{
    /// <summary>The screens a popup should appear on: every screen, or just the one the mouse is on.</summary>
    public static IReadOnlyList<Forms.Screen> Targets(bool allScreens)
    {
        if (allScreens && Forms.Screen.AllScreens.Length > 0) return Forms.Screen.AllScreens;
        // the cursor position can't be read while Windows is still switching desktops at sign-in; use the main screen then
        try { return [Forms.Screen.FromPoint(Forms.Cursor.Position)]; }
        catch (Exception e) { Log.Write("cursor position unavailable: " + e.Message); return [Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]]; }
    }

    /// <summary>A window that is never activated, stays above other windows, and has no taskbar button.</summary>
    public static void MakeNonActivating(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
    }

    /// <summary>
    /// Moves and sizes the window in physical pixels. `sizeDip` is the size at 100% scaling; `place` gets the screen's
    /// bounds in pixels and the window's pixel size and returns the pixel position of its top-left corner.
    /// </summary>
    public static void Place(Window window, Forms.Screen screen, Size sizeDip,
                             Func<System.Drawing.Rectangle, System.Drawing.Size, System.Drawing.Point> place)
    {
        double scale = Native.ScaleOf(screen);
        var pixels = new System.Drawing.Size((int)Math.Round(sizeDip.Width * scale), (int)Math.Round(sizeDip.Height * scale));
        var at = place(screen.Bounds, pixels);
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, at.X, at.Y, pixels.Width, pixels.Height, Native.SWP_NOACTIVATE);
    }

    public static System.Drawing.Point Centered(System.Drawing.Rectangle bounds, System.Drawing.Size size) =>
        new(bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + (bounds.Height - size.Height) / 2);

    public static System.Drawing.Point TopCentre(System.Drawing.Rectangle bounds, System.Drawing.Size size) =>
        new(bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top);
}
