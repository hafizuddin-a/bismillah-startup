using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Bismillah;

/// <summary>
/// Tells the app when the PC wakes up and when the session is unlocked.
///
/// Waking is detected two ways, because laptops differ: the classic "resume from sleep" event, and the display
/// turning back on (which is what a lid opening, or a Modern Standby wake, looks like). The app dedupes the two.
/// </summary>
internal sealed class WakeMonitor : IDisposable
{
    private HwndSource? _window;
    private IntPtr _displayNotification;

    /// <summary>The PC woke up, or its display came back on.</summary>
    public event Action? Woke;
    /// <summary>The lock screen came up.</summary>
    public event Action? Locked;
    /// <summary>The user signed back in after the lock screen.</summary>
    public event Action? Unlocked;

    /// <summary>False while the display is off (so a popup would be pointless).</summary>
    public bool DisplayOn { get; private set; } = true;

    public void Start()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // a hidden message-only window receives the display on/off notifications
        var parameters = new HwndSourceParameters("BismillahPower") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 };
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);
        var guid = Native.GUID_CONSOLE_DISPLAY_STATE;
        _displayNotification = Native.RegisterPowerSettingNotification(_window.Handle, ref guid, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Woke?.Invoke();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Locked?.Invoke();
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
            Unlocked?.Invoke();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_POWERBROADCAST && wParam.ToInt64() == Native.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<Native.POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == Native.GUID_CONSOLE_DISPLAY_STATE)
            {
                bool wasOn = DisplayOn;
                DisplayOn = setting.Data != 0;
                if (DisplayOn && !wasOn) Woke?.Invoke();
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_displayNotification != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_displayNotification);
        _window?.Dispose();
    }
}
