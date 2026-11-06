using System.Diagnostics;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Bismillah;

/// <summary>Owns everything the app does: the tray icon and menu, the popups, wake handling, dhikr and updates.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly Settings _settings = Settings.Load();
    private readonly PopupController _popup = new();
    private readonly DhikrPopupController _dhikrPopup = new();
    private readonly WakeMonitor _wake = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly DhikrScheduler _dhikr;
    private readonly Updater _updater;
    private readonly DispatcherTimer _dhikrTimer = new();
    private readonly Action _quit;

    private bool _greetOnceUnlocked;
    private DateTime _lastGreeting = DateTime.MinValue;

    public AppHost(Action quit, bool afterUpdate)
    {
        _quit = quit;
        _dhikr = new DhikrScheduler(_settings, ShowDhikrIfPossible);
        _updater = new Updater(_settings, () => _popup.IsShowing || _dhikrPopup.IsShowing, _quit);

        var icon = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Bismillah.ico"))!.Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(icon),
            Text = "Bismillah",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseUp += (_, e) =>
        {
            // like the Mac menu bar item, a left click opens the menu too
            if (e.Button == Forms.MouseButtons.Left)
                typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(_tray, null);
        };
        _menu.Opening += (_, _) => RebuildMenu();

        _dhikr.WakeAt += due =>
        {
            _dhikrTimer.Stop();
            if (due is not { } at) return;
            var wait = at - DateTime.Now;
            _dhikrTimer.Interval = wait > TimeSpan.FromMilliseconds(50) ? (wait > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : wait) : TimeSpan.FromMilliseconds(50);
            _dhikrTimer.Start();
        };
        _dhikrTimer.Tick += (_, _) =>
        {
            _dhikrTimer.Stop();
            _dhikr.TimerFired();
        };

        // After an automatic update the app reopens itself quietly; every other launch greets you.
        if (!afterUpdate) _popup.Show(_settings.ShowOnAllScreens);
        _updater.StartAutomaticChecks();
        _dhikr.Start();
        if (Environment.GetEnvironmentVariable("BISMILLAH_TEST_UPDATE_NOW") == "1") Later(1500, () => _updater.CheckNow());

        // Greet again whenever the PC wakes (lid opened, or the display woke up).
        _wake.Woke += OnWoke;
        _wake.Unlocked += () => Later(500, GreetIfPossible);
        _wake.Start();
    }

    private static void Later(int milliseconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }

    // MARK: wake / unlock

    private void OnWoke()
    {
        _greetOnceUnlocked = true;
        Later(1000, GreetIfPossible);
        Later(60_000, _updater.CheckIfDue);       // the PC may have slept past an update check
        _dhikr.Start();                           // the clock may have jumped while it slept
    }

    /// <summary>If the lid opens onto the lock screen, this waits for the unlock: a popup can't appear above it.</summary>
    private void GreetIfPossible()
    {
        if (!_greetOnceUnlocked || Native.IsSessionLocked()) return;
        _greetOnceUnlocked = false;
        // resume and display-on usually both fire; don't greet twice
        if ((DateTime.Now - _lastGreeting).TotalSeconds <= 5) return;
        _lastGreeting = DateTime.Now;
        _popup.Show(_settings.ShowOnAllScreens);
    }

    // MARK: dhikr

    /// <summary>Called by the scheduler. Returns false when it isn't a good moment, so the scheduler tries again soon.</summary>
    private bool ShowDhikrIfPossible()
    {
        if (_popup.IsShowing || _dhikrPopup.IsShowing || Native.IsSessionLocked() || !_wake.DisplayOn) return false;
        _dhikrPopup.Show(_settings.ShowOnAllScreens);
        return true;
    }

    // MARK: menu

    private static string HourLabel(int hour)
    {
        string text = DateTime.Today.AddHours(hour % 24).ToString("t");
        return hour == 24 ? $"{text} (end of day)" : text;
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add("Show Bismillah", null, (_, _) => { _lastGreeting = DateTime.Now; _popup.Show(_settings.ShowOnAllScreens); });

        var dhikr = new Forms.ToolStripMenuItem("Dhikr Reminders");
        foreach (int count in DhikrScheduler.Choices)
        {
            string title = count == 0 ? "Off" : count == 1 ? "1 per day" : $"{count} per day";
            dhikr.DropDownItems.Add(new Forms.ToolStripMenuItem(title, null, (_, _) => _dhikr.PerDay = count) { Checked = _dhikr.PerDay == count });
        }
        dhikr.DropDownItems.Add(new Forms.ToolStripSeparator());
        var starts = new Forms.ToolStripMenuItem($"Starts at {HourLabel(_dhikr.StartHour)}");
        foreach (int hour in DhikrScheduler.StartHourChoices)
            starts.DropDownItems.Add(new Forms.ToolStripMenuItem(HourLabel(hour), null, (_, _) => _dhikr.StartHour = hour)
                { Checked = hour == _dhikr.StartHour, Enabled = hour < _dhikr.EndHour });
        var ends = new Forms.ToolStripMenuItem($"Ends at {HourLabel(_dhikr.EndHour)}");
        foreach (int hour in DhikrScheduler.EndHourChoices)
            ends.DropDownItems.Add(new Forms.ToolStripMenuItem(HourLabel(hour), null, (_, _) => _dhikr.EndHour = hour)
                { Checked = hour == _dhikr.EndHour, Enabled = hour > _dhikr.StartHour });
        dhikr.DropDownItems.Add(starts);
        dhikr.DropDownItems.Add(ends);
        dhikr.DropDownItems.Add(new Forms.ToolStripSeparator());
        string next;
        if (_dhikr.NextPopup is { } due)
            next = due.Date == DateTime.Today ? $"Next: {due:t}" : $"Next: tomorrow, {due:t}";
        else next = "No reminders scheduled";
        dhikr.DropDownItems.Add(new Forms.ToolStripMenuItem(next) { Enabled = false });
        dhikr.DropDownItems.Add(new Forms.ToolStripMenuItem("Show Now", null, (_, _) => _dhikrPopup.Show(_settings.ShowOnAllScreens)));
        _menu.Items.Add(dhikr);

        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(new Forms.ToolStripMenuItem("Launch at Login", null, (_, _) => ToggleLaunchAtLogin()) { Checked = StartupRegistration.IsEnabled });
        _menu.Items.Add(new Forms.ToolStripMenuItem("Show on All Screens", null, (_, _) => { _settings.ShowOnAllScreens = !_settings.ShowOnAllScreens; _settings.Save(); })
            { Checked = _settings.ShowOnAllScreens });
        _menu.Items.Add(new Forms.ToolStripMenuItem("Update Automatically", null, (_, _) =>
        {
            _settings.UpdateAutomatically = !_settings.UpdateAutomatically;
            _settings.Save();
            if (_settings.UpdateAutomatically) _updater.CheckIfDue();
        }) { Checked = _settings.UpdateAutomatically });
        _menu.Items.Add("Check for Updates…", null, (_, _) => _updater.CheckManually());
        _menu.Items.Add(new Forms.ToolStripMenuItem($"Version {Updater.CurrentVersion}") { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Quit Bismillah", null, (_, _) => _quit());
        _menu.Items.Add("Sincerely, @madebyhafiz", null, (_, _) =>
            Process.Start(new ProcessStartInfo("https://www.instagram.com/madebyhafiz/") { UseShellExecute = true }));
    }

    /// <summary>The menu as indented text, for the CI smoke test.</summary>
    public string DescribeMenu()
    {
        RebuildMenu();
        var text = new System.Text.StringBuilder();
        void Walk(Forms.ToolStripItemCollection items, int depth)
        {
            foreach (Forms.ToolStripItem item in items)
            {
                if (item is Forms.ToolStripSeparator) { text.AppendLine(new string(' ', depth * 2) + "-----"); continue; }
                string tick = item is Forms.ToolStripMenuItem { Checked: true } ? "[x] " : "";
                string state = item.Enabled ? "" : " (disabled)";
                text.AppendLine($"{new string(' ', depth * 2)}{tick}{item.Text}{state}");
                if (item is Forms.ToolStripMenuItem { HasDropDownItems: true } sub) Walk(sub.DropDownItems, depth + 1);
            }
        }
        Walk(_menu.Items, 0);
        return text.ToString();
    }

    private static void ToggleLaunchAtLogin()
    {
        try { StartupRegistration.Set(!StartupRegistration.IsEnabled); }
        catch (Exception e)
        {
            System.Windows.MessageBox.Show(e.Message, "Couldn't change Launch at Login", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    public void Dispose()
    {
        _wake.Dispose();
        _popup.Close();
        _dhikrPopup.Close();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
