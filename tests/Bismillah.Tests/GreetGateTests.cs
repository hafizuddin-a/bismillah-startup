using Xunit;

namespace Bismillah.Tests;

public class GreetGateTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 9, 0, 0);

    [Fact]
    public void NothingHappensWithoutAWake()
    {
        var gate = new GreetGate();
        Assert.False(gate.ShouldGreet(T0, locked: false, displayOn: true));
    }

    [Fact]
    public void GreetsOnceAfterTheSettlingPeriodWhenNeverLocked()
    {
        var gate = new GreetGate();
        gate.Woke(T0);
        Assert.False(gate.ShouldGreet(T0.AddSeconds(1), false, true));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(3.9), false, true));
        Assert.True(gate.ShouldGreet(T0.AddSeconds(4.1), false, true));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(5), false, true));      // only once
        Assert.False(gate.Pending);
    }

    [Fact]
    public void LockScreenAppearingJustAfterTheWakeDoesNotGetGreetedBehindIt()
    {
        // the case from a real laptop: wake, "not locked" for a moment, then the lock screen comes up
        var gate = new GreetGate();
        gate.Woke(T0);
        Assert.False(gate.ShouldGreet(T0.AddSeconds(0.5), false, true));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(1.5), true, true));     // lock screen is up
        Assert.False(gate.ShouldGreet(T0.AddSeconds(10), true, true));      // still typing the PIN
        Assert.False(gate.ShouldGreet(T0.AddSeconds(12), false, true));     // unlocked: a beat first
        Assert.True(gate.ShouldGreet(T0.AddSeconds(12.8), false, true));
    }

    [Fact]
    public void ALockReportedByWindowsIsRespectedEvenIfNoPollSawIt()
    {
        var gate = new GreetGate();
        gate.Woke(T0);
        gate.Locked();
        Assert.False(gate.ShouldGreet(T0.AddSeconds(6), false, true));      // past the settle time, but it was locked
        Assert.True(gate.ShouldGreet(T0.AddSeconds(6.8), false, true));
    }

    [Fact]
    public void WaitsForTheDisplayToComeBack()
    {
        var gate = new GreetGate();
        gate.Woke(T0);
        Assert.False(gate.ShouldGreet(T0.AddSeconds(10), false, displayOn: false));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(10.2), false, true));   // display just came on: settle first
        Assert.True(gate.ShouldGreet(T0.AddSeconds(14.5), false, true));
    }

    [Fact]
    public void ASecondWakeSignalRestartsTheWaitButGreetsOnlyOnce()
    {
        var gate = new GreetGate();
        gate.Woke(T0);                                                       // resume
        gate.Woke(T0.AddSeconds(2));                                         // display on
        Assert.False(gate.ShouldGreet(T0.AddSeconds(4.5), false, true));
        Assert.True(gate.ShouldGreet(T0.AddSeconds(6.1), false, true));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(7), false, true));
    }

    [Fact]
    public void AGreetingJustShownByHandIsNotRepeated()
    {
        var gate = new GreetGate();
        gate.Greeted(T0.AddSeconds(3));
        gate.Woke(T0);
        Assert.False(gate.ShouldGreet(T0.AddSeconds(4.5), false, true));
        Assert.False(gate.Pending);
    }

    [Fact]
    public void TheNextWakeGreetsAgain()
    {
        var gate = new GreetGate();
        gate.Woke(T0);
        Assert.True(gate.ShouldGreet(T0.AddSeconds(5), false, true));
        gate.Woke(T0.AddHours(2));
        Assert.True(gate.ShouldGreet(T0.AddHours(2).AddSeconds(5), false, true));
    }
}

public class GreetGateDesktopTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 9, 0, 0);

    [Fact]
    public void WaitsWhileTheDesktopIsStillLoadingOrTheStartMenuIsOpen()
    {
        var gate = new GreetGate();
        gate.Woke(T0);
        Assert.False(gate.ShouldGreet(T0.AddSeconds(10), false, true, deskReady: false));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(10.2), false, true));                    // ready again: settle first
        Assert.True(gate.ShouldGreet(T0.AddSeconds(14.5), false, true));
    }

    [Fact]
    public void ALaterWakeTimeDelaysTheGreeting()
    {
        // used at sign-in, where the app starts while Windows is still busy
        var gate = new GreetGate();
        gate.Woke(T0.AddSeconds(6));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(9), false, true));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(9.9), false, true));
        Assert.True(gate.ShouldGreet(T0.AddSeconds(10.1), false, true));
    }
}

public class GreetGateSignInTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 9, 0, 0);

    [Fact]
    public void AShortSettlingPeriodGreetsSoonerAtSignIn()
    {
        var gate = new GreetGate();
        gate.Woke(T0, settle: TimeSpan.FromSeconds(1));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(0.5), false, true));
        Assert.True(gate.ShouldGreet(T0.AddSeconds(1.1), false, true));
    }

    [Fact]
    public void ALockAtSignInIsStillWaitedOut()
    {
        var gate = new GreetGate();
        gate.Woke(T0, settle: TimeSpan.FromSeconds(1));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(2), locked: true, displayOn: true));
        Assert.False(gate.ShouldGreet(T0.AddSeconds(3), false, true));
        Assert.True(gate.ShouldGreet(T0.AddSeconds(3.8), false, true));
    }

    [Fact]
    public void ANormalWakeAfterASignInUsesTheNormalSettlingPeriod()
    {
        var gate = new GreetGate();
        gate.Woke(T0, settle: TimeSpan.FromSeconds(1));
        Assert.True(gate.ShouldGreet(T0.AddSeconds(2), false, true));
        gate.Woke(T0.AddHours(1));
        Assert.False(gate.ShouldGreet(T0.AddHours(1).AddSeconds(2), false, true));
        Assert.True(gate.ShouldGreet(T0.AddHours(1).AddSeconds(4.1), false, true));
    }
}
