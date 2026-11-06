namespace Bismillah;

/// <summary>
/// Decides when the dhikr popup appears: N random times a day, within the active hours (08:00 to 22:00 by default).
///
/// The day's plan is saved, so quitting and reopening the app doesn't reshuffle it or repeat popups. A time that
/// passes while the PC is asleep or the app is closed is skipped, not shown late. Changing the number mid-day, or the
/// hours, re-plans only the popups still to come (what was already shown today counts toward the total).
///
/// The scheduler owns no timer. It tells its host when to wake it (<see cref="WakeAt"/>) and the host calls
/// <see cref="TimerFired"/> then, so the whole thing can be tested with a fake clock.
/// </summary>
public sealed class DhikrScheduler
{
    public static readonly int[] Choices = [0, 1, 3, 5, 10, 15, 20, 30];     // 0 = off
    public static readonly int[] StartHourChoices = Enumerable.Range(0, 24).ToArray();
    public static readonly int[] EndHourChoices = Enumerable.Range(1, 24).ToArray();     // 24 = midnight at the end of the day

    private readonly Settings _settings;
    private readonly Func<DateTime> _now;
    private readonly Func<bool> _show;                  // shows a popup; false = couldn't right now, try again soon
    private readonly Func<DateTime, (DateTime Start, DateTime End)>? _windowOverride;      // tests only
    private readonly Random _rng;
    private readonly TimeSpan _minGap, _lead, _grace;

    /// <summary>Raised whenever the host should (re)arm its timer for this moment. Null = no timer needed.</summary>
    public event Action<DateTime?>? WakeAt;

    public DhikrScheduler(Settings settings, Func<bool> show, Func<DateTime>? now = null, Random? rng = null,
                          Func<DateTime, (DateTime Start, DateTime End)>? window = null,
                          TimeSpan? minGap = null, TimeSpan? lead = null, TimeSpan? grace = null)
    {
        _settings = settings;
        _show = show;
        _now = now ?? (() => DateTime.Now);
        _rng = rng ?? new Random();
        _windowOverride = window;
        _minGap = minGap ?? TimeSpan.FromMinutes(15);
        _lead = lead ?? TimeSpan.FromSeconds(60);
        _grace = grace ?? TimeSpan.FromMinutes(10);
    }

    /// <summary>The active hours on the day containing `date`: `startHour` o'clock to `endHour` o'clock.</summary>
    public static (DateTime Start, DateTime End) Hours(int startHour, int endHour, DateTime date)
    {
        var midnight = date.Date;
        return (midnight.AddHours(startHour), midnight.AddHours(endHour));
    }

    // MARK: settings

    /// <summary>Popups per day; 0 turns them off.</summary>
    public int PerDay
    {
        get => _settings.DhikrPerDay;
        set
        {
            if (value == _settings.DhikrPerDay) return;
            _settings.DhikrPerDay = value;
            _settings.Save();
            Rebuild();
            ScheduleNext();
        }
    }

    /// <summary>The hour popups may start appearing, 0...23.</summary>
    public int StartHour
    {
        get => Math.Clamp(_settings.DhikrStartHour, 0, 23);
        set
        {
            int hour = Math.Clamp(value, 0, 23);
            if (hour == StartHour) return;
            _settings.DhikrStartHour = hour;
            if (hour >= EndHour) _settings.DhikrEndHour = Math.Min(hour + 1, 24);        // keep start before end
            HoursChanged();
        }
    }

    /// <summary>The hour popups stop, 1...24 (24 = midnight at the end of the day).</summary>
    public int EndHour
    {
        get => Math.Clamp(_settings.DhikrEndHour, 1, 24);
        set
        {
            int hour = Math.Clamp(value, 1, 24);
            if (hour == EndHour) return;
            _settings.DhikrEndHour = hour;
            if (hour <= StartHour) _settings.DhikrStartHour = Math.Max(hour - 1, 0);
            HoursChanged();
        }
    }

    private void HoursChanged()
    {
        _settings.Save();
        if (PerDay <= 0) return;
        Rebuild();
        ScheduleNext();
    }

    private (DateTime Start, DateTime End) Window(DateTime date) => _windowOverride?.Invoke(date) ?? Hours(StartHour, EndHour, date);

    private List<DateTime> Pending
    {
        get => _settings.DhikrPending.OrderBy(d => d).ToList();
        set { _settings.DhikrPending = value; _settings.Save(); }
    }

    /// <summary>When the next popup is due, for the menu. Null when they're off.</summary>
    public DateTime? NextPopup
    {
        get
        {
            if (PerDay <= 0) return null;
            var pending = Pending;
            return pending.Count > 0 ? pending[0] : Window(_now().Date.AddDays(1)).Start;
        }
    }

    // MARK: scheduling

    /// <summary>Call at launch, and again after the PC wakes (the clock may have jumped).</summary>
    public void Start() => ScheduleNext();

    private static string DayString(DateTime date) => $"{date.Year}-{date.Month}-{date.Day}";

    private void RollDayIfNeeded()
    {
        string today = DayString(_now());
        if (_settings.DhikrDay == today) return;
        _settings.DhikrDay = today;
        _settings.DhikrShownToday = 0;
        Rebuild();
    }

    /// <summary>Plans what's left of today: the popups still owed, spread over the rest of the active hours.</summary>
    private void Rebuild()
    {
        var now = _now();
        _settings.DhikrDay = DayString(now);
        var hours = Window(now);
        var from = now + _lead > hours.Start ? now + _lead : hours.Start;
        int owed = Math.Max(0, PerDay - _settings.DhikrShownToday);
        Pending = from < hours.End ? DhikrPlan.Times(owed, from, hours.End, _minGap, _rng) : [];
    }

    private void ScheduleNext()
    {
        if (PerDay <= 0) { WakeAt?.Invoke(null); return; }
        RollDayIfNeeded();
        var now = _now();
        var pending = Pending.Where(t => t > now - _grace).ToList();        // slept through the others: skip
        Pending = pending;
        // nothing left today: wake up at the start of tomorrow's hours (never "now", which would spin)
        var tomorrowStart = Window(now.Date.AddDays(1)).Start;
        var due = pending.Count > 0 ? pending[0] : (tomorrowStart > now ? tomorrowStart : now.AddHours(1));
        WakeAt?.Invoke(due);
    }

    /// <summary>The host's timer fired: show the popup that is due (if any), then arm for the next one.</summary>
    public void TimerFired()
    {
        RollDayIfNeeded();
        var now = _now();
        var queue = Pending;
        if (queue.Count > 0 && queue[0] <= now.AddSeconds(1))
        {
            var first = queue[0];
            queue.RemoveAt(0);
            if (now - first <= _grace)
            {
                if (_show()) _settings.DhikrShownToday++;
                else if (now.AddSeconds(120) < Window(now).End) queue.Insert(0, now.AddSeconds(120));    // busy or locked: try again in a bit
            }
        }
        Pending = queue;
        ScheduleNext();
    }
}
