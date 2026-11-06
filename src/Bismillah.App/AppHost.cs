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

    private readonly GreetGate _greet = new();
    private string _lastGreetState = "";
    private readonly DispatcherTimer _greetTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // menu items that change with the settings, kept so the open menu can be updated in place
    private readonly List<(int Count, Forms.ToolStripMenuItem Item)> _countItems = [];
    private readonly List<(int Hour, Forms.ToolStripMenuItem Item)> _startItems = [], _endItems = [];
    private Forms.ToolStripMenuItem _startParent = null!, _endParent = null!, _nextItem = null!;
    private Forms.ToolStripMenuItem _loginItem = null!, _allScreensItem = null!, _autoUpdateItem = null!;
    private readonly List<string> _closingLog = [];

    public AppHost(Action quit, bool afterUpdate, bool atLogin = false)
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
        BuildMenu();
        _menu.Opening += (_, _) => RefreshMenu();

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

        // After an automatic update the app reopens itself quietly. Opened by hand it greets you straight away; started
        // at sign-in it waits for the desktop to be ready first (Windows starts these apps while it is still setting up).
        _greetTimer.Tick += (_, _) => PollGreeting();
        if (atLogin) Wake("signed in", settle: TimeSpan.FromSeconds(1));      // the desktop is already up; the lock/taskbar checks still apply
        else if (!afterUpdate) ShowGreeting();
        StartupRegistration.KeepCurrent();
        _updater.StartAutomaticChecks();
        _dhikr.Start();
        if (Environment.GetEnvironmentVariable("BISMILLAH_TEST_UPDATE_NOW") == "1") Later(1500, () => _updater.CheckNow());

        // Greet again whenever the PC wakes (lid opened, or the display woke up).
        _wake.Woke += () => { Log.Write("wake signal"); OnWoke(); };
        _wake.Locked += () => { Log.Write("session locked"); _greet.Locked(); };
        _wake.Unlocked += () => Log.Write("session unlocked");
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
        Wake("woke");
        Later(60_000, _updater.CheckIfDue);       // the PC may have slept past an update check
        _dhikr.Start();                           // the clock may have jumped while it slept
    }

    /// <summary>Waits for the lock screen to be gone (see <see cref="GreetGate"/>) and then writes the bismillah.</summary>
    private void Wake(string why, TimeSpan? settle = null)
    {
        _greet.Woke(DateTime.Now, settle);
        _greetTimer.Start();
        Log.Write($"greeting pending ({why})");
    }

    private void PollGreeting()
    {
        string lockReason = Native.LockState();
        bool locked = lockReason != "";
        bool shell = Native.ShellReady, startMenu = Native.StartMenuOpen();
        string state = $"locked={(locked ? lockReason : "no")} display={_wake.DisplayOn} taskbar={shell} startMenu={startMenu} in={Native.ForegroundProgram()}";
        if (state != _lastGreetState) { _lastGreetState = state; Log.Write("waiting: " + state); }
        if (_greet.ShouldGreet(DateTime.Now, locked, _wake.DisplayOn, deskReady: shell && !startMenu))
        {
            Log.Write($"greeting now ({(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds:0.0} s after the app started)");
            ShowGreeting(markGreeted: false);
        }
        if (!_greet.Pending) _greetTimer.Stop();
    }

    private void ShowGreeting(bool markGreeted = true)
    {
        if (markGreeted) _greet.Greeted(DateTime.Now);
        try { _popup.Show(_settings.ShowOnAllScreens); }
        catch (Exception e) { Log.Write("couldn't show the greeting: " + e); }
    }

    // MARK: dhikr

    /// <summary>Called by the scheduler. Returns false when it isn't a good moment, so the scheduler tries again soon.</summary>
    private bool ShowDhikrIfPossible()
    {
        if (_popup.IsShowing || _dhikrPopup.IsShowing || Native.IsSessionLocked() || !_wake.DisplayOn) return false;
        try { _dhikrPopup.Show(_settings.ShowOnAllScreens); }
        catch (Exception e) { Log.Write("couldn't show the dhikr: " + e); return false; }
        return true;
    }

    // MARK: menu

    private static string HourLabel(int hour)
    {
        string text = DateTime.Today.AddHours(hour % 24).ToString("t");
        return hour == 24 ? $"{text} (end of day)" : text;
    }

    /// <summary>A menu item that closes the menu when clicked, like any other.</summary>
    private Forms.ToolStripMenuItem Plain(string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>A setting you may want to change several times: clicking it updates the ticks and leaves the menu open.</summary>
    private Forms.ToolStripMenuItem Sticky(string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text) { Tag = StickyTag };
        item.Click += (_, _) =>
        {
            action();
            RefreshMenu();
        };
        return item;
    }

    private const string StickyTag = "sticky";

    /// <summary>
    /// Stops a click on a sticky item from closing this menu or submenu. Windows announces the close before it runs the
    /// item's click handler, so the clicked item is found by what is highlighted (the item under the mouse, or the one
    /// the keyboard is on), following open submenus down to the item itself.
    /// </summary>
    private void KeepOpenWhenSticky(Forms.ToolStripDropDown dropDown) =>
        dropDown.Closing += (sender, e) =>
        {
            bool sticky = e.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked && HighlightedIsSticky(dropDown);
            if (_closingLog.Count < 40) _closingLog.Add($"{e.CloseReason}:sticky={sticky}");
            if (sticky) e.Cancel = true;
        };

    private static bool HighlightedIsSticky(Forms.ToolStripDropDown dropDown)
    {
        foreach (Forms.ToolStripItem item in dropDown.Items)
        {
            if (!item.Selected) continue;
            if (item is Forms.ToolStripMenuItem { HasDropDownItems: true } parent)
                return parent.DropDown.Visible && HighlightedIsSticky(parent.DropDown);
            return item.Tag is StickyTag;
        }
        return false;
    }

    private void BuildMenu()
    {
        KeepOpenWhenSticky(_menu);
        _menu.Items.Add(Plain("Show Bismillah", () => ShowGreeting()));

        var dhikr = new Forms.ToolStripMenuItem("Dhikr Reminders");
        foreach (int count in DhikrScheduler.Choices)
        {
            string title = count == 0 ? "Off" : count == 1 ? "1 per day" : $"{count} per day";
            var item = Sticky(title, () => _dhikr.PerDay = count);
            _countItems.Add((count, item));
            dhikr.DropDownItems.Add(item);
        }
        dhikr.DropDownItems.Add(new Forms.ToolStripSeparator());
        _startParent = new Forms.ToolStripMenuItem();
        foreach (int hour in DhikrScheduler.StartHourChoices)
        {
            var item = Sticky(HourLabel(hour), () => _dhikr.StartHour = hour);
            _startItems.Add((hour, item));
            _startParent.DropDownItems.Add(item);
        }
        _endParent = new Forms.ToolStripMenuItem();
        foreach (int hour in DhikrScheduler.EndHourChoices)
        {
            var item = Sticky(HourLabel(hour), () => _dhikr.EndHour = hour);
            _endItems.Add((hour, item));
            _endParent.DropDownItems.Add(item);
        }
        dhikr.DropDownItems.Add(_startParent);
        dhikr.DropDownItems.Add(_endParent);
        dhikr.DropDownItems.Add(new Forms.ToolStripSeparator());
        _nextItem = new Forms.ToolStripMenuItem { Enabled = false };
        dhikr.DropDownItems.Add(_nextItem);
        dhikr.DropDownItems.Add(Plain("Show Now", () => _dhikrPopup.Show(_settings.ShowOnAllScreens)));
        _menu.Items.Add(dhikr);

        // every submenu needs the same guard as the main menu
        foreach (var menu in new[] { dhikr, _startParent, _endParent }) KeepOpenWhenSticky(menu.DropDown);

        _menu.Items.Add(new Forms.ToolStripSeparator());
        _loginItem = Sticky("Launch at Login", ToggleLaunchAtLogin);
        _menu.Items.Add(_loginItem);
        _allScreensItem = Sticky("Show on All Screens", () => { _settings.ShowOnAllScreens = !_settings.ShowOnAllScreens; _settings.Save(); });
        _menu.Items.Add(_allScreensItem);
        _autoUpdateItem = Sticky("Update Automatically", () =>
        {
            _settings.UpdateAutomatically = !_settings.UpdateAutomatically;
            _settings.Save();
            if (_settings.UpdateAutomatically) _updater.CheckIfDue();
        });
        _menu.Items.Add(_autoUpdateItem);
        _menu.Items.Add(Plain("Check for Updates…", () => _updater.CheckManually()));
        _menu.Items.Add(new Forms.ToolStripMenuItem($"Version {Updater.CurrentVersion}") { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(Plain("Quit Bismillah", () => _quit()));
        _menu.Items.Add(Plain("Sincerely, @madebyhafiz", () =>
            Process.Start(new ProcessStartInfo("https://www.instagram.com/madebyhafiz/") { UseShellExecute = true })));
        RefreshMenu();
    }

    /// <summary>Brings every tick, greyed-out entry and label up to date with the settings, without rebuilding the menu.</summary>
    private void RefreshMenu()
    {
        foreach (var (count, item) in _countItems) item.Checked = _dhikr.PerDay == count;
        _startParent.Text = $"Starts at {HourLabel(_dhikr.StartHour)}";
        _endParent.Text = $"Ends at {HourLabel(_dhikr.EndHour)}";
        foreach (var (hour, item) in _startItems) { item.Checked = hour == _dhikr.StartHour; item.Enabled = hour < _dhikr.EndHour; }
        foreach (var (hour, item) in _endItems) { item.Checked = hour == _dhikr.EndHour; item.Enabled = hour > _dhikr.StartHour; }
        _nextItem.Text = _dhikr.NextPopup is { } due
            ? (due.Date == DateTime.Today ? $"Next: {due:t}" : $"Next: tomorrow, {due:t}")
            : "No reminders scheduled";
        _loginItem.Checked = StartupRegistration.IsEnabled;
        _allScreensItem.Checked = _settings.ShowOnAllScreens;
        _autoUpdateItem.Checked = _settings.UpdateAutomatically;
    }

    /// <summary>
    /// CI check that sticky items keep the menu open while ordinary ones close it. Opens the real menu, clicks a probe
    /// item (should close it) and then "Show on All Screens" (should not).
    /// </summary>
    public async Task<(bool Conclusive, bool Ok, string Report)> TestMenuStaysOpen()
    {
        var probe = Plain("probe", () => { });
        _menu.Items.Add(probe);
        try
        {
            _menu.Show(new System.Drawing.Point(60, 60));
            await Task.Delay(400);
            bool shown = _menu.Visible;
            probe.Select();                                       // like the mouse resting on it
            probe.PerformClick();
            await Task.Delay(400);
            bool plainClosed = !_menu.Visible;

            bool before = _settings.ShowOnAllScreens;
            _menu.Show(new System.Drawing.Point(60, 60));
            await Task.Delay(400);
            _allScreensItem.Select();
            _allScreensItem.PerformClick();
            await Task.Delay(400);
            bool stickyOpen = _menu.Visible;
            bool ticked = _allScreensItem.Checked != before;
            _allScreensItem.PerformClick();                       // put the setting back
            await Task.Delay(200);
            _menu.Close();

            string report = $"closing events: [{string.Join(", ", _closingLog)}]; menu shown: {shown}; ordinary click closed it: {plainClosed}; sticky click left it open: {stickyOpen}; tick followed the setting: {ticked}";
            return (shown && plainClosed, stickyOpen && ticked, report);
        }
        finally { _menu.Items.Remove(probe); }
    }

    /// <summary>The menu as indented text, for the CI smoke test.</summary>
    public string DescribeMenu()
    {
        RefreshMenu();
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
