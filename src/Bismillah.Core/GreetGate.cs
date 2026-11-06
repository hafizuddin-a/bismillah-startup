namespace Bismillah;

/// <summary>
/// Decides when the greeting should appear after the PC wakes (or after the app starts at sign-in).
///
/// A wake is reported a moment before the lock screen appears, so "not locked right now" straight after waking proves
/// nothing: the popup would be drawn behind the lock screen and be over by the time you sign in. So after every wake
/// signal the gate waits out a settling period, and if the lock screen turns up (a poll sees it locked, or the app is
/// told) it waits for the unlock instead, then a short beat so the desktop is back.
///
/// The gate owns no timer: the host polls <see cref="ShouldGreet"/> a few times a second while <see cref="Pending"/>.
/// </summary>
public sealed class GreetGate
{
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan AfterUnlock = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(5);

    private DateTime _lastWake, _lastGreeting = DateTime.MinValue;
    private TimeSpan _settle = Settle;
    private DateTime? _unlockedSince;
    private bool _sawLock;

    /// <summary>A greeting is waiting for the right moment.</summary>
    public bool Pending { get; private set; }

    /// <summary>
    /// The PC woke, the display came back, or the app just started at sign-in. <paramref name="settle"/> overrides the
    /// settling period (at sign-in there is no lock screen about to appear, so it can be short).
    /// </summary>
    public void Woke(DateTime now, TimeSpan? settle = null)
    {
        _settle = settle ?? Settle;
        Pending = true;
        _lastWake = now;
        _unlockedSince = null;
        _sawLock = false;
    }

    /// <summary>The user asked for the popup themselves, so a wake signal right after shouldn't repeat it.</summary>
    public void Greeted(DateTime now) => _lastGreeting = now;

    /// <summary>The session was locked (told by Windows, or seen by a poll).</summary>
    public void Locked()
    {
        if (!Pending) return;
        _sawLock = true;
        _unlockedSince = null;
    }

    /// <summary>
    /// One look at the world. Returns true exactly once per wake, when the popup should be shown now.
    /// <paramref name="deskReady"/> is false while the desktop is still loading or the Start menu is open.
    /// </summary>
    public bool ShouldGreet(DateTime now, bool locked, bool displayOn, bool deskReady = true)
    {
        if (!Pending) return false;
        if (locked) { Locked(); return false; }
        if (!displayOn || !deskReady) { _unlockedSince = null; _lastWake = now; return false; }      // it settles again once the display is back

        _unlockedSince ??= now;
        bool ready = _sawLock
            ? now - _unlockedSince >= AfterUnlock
            : now - _lastWake >= _settle;
        if (!ready) return false;

        Pending = false;
        // resume and display-on usually both fire; don't greet twice
        if (now - _lastGreeting < MinimumGap) return false;
        _lastGreeting = now;
        return true;
    }
}
