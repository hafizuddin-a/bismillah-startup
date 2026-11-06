using Xunit;

namespace Bismillah.Tests;

public class DhikrPlanTests
{
    private static readonly DateTime Start = new(2026, 11, 6, 8, 0, 0);
    private static readonly DateTime End = new(2026, 11, 6, 22, 0, 0);

    [Fact]
    public void PlansTheRequestedCountInOrderWithinTheHours()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var times = DhikrPlan.Times(10, Start, End, TimeSpan.FromMinutes(15), new Random(seed));
            Assert.Equal(10, times.Count);
            Assert.Equal(times.OrderBy(t => t), times);
            Assert.All(times, t => Assert.InRange(t, Start, End));
        }
    }

    [Fact]
    public void PopupsAreSpreadAndNeverBunchedTooClose()
    {
        // slices are 1.4 h wide and times come from the middle 80%, so neighbours are at least 0.2 slice = 16.8 min apart
        for (int seed = 0; seed < 200; seed++)
        {
            var times = DhikrPlan.Times(10, Start, End, TimeSpan.FromMinutes(15), new Random(seed));
            for (int i = 1; i < times.Count; i++) Assert.True((times[i] - times[i - 1]).TotalMinutes >= 16.7);
        }
    }

    [Fact]
    public void ASpanTooShortGetsFewerPopups()
    {
        var times = DhikrPlan.Times(30, Start, Start.AddMinutes(50), TimeSpan.FromMinutes(15), new Random(1));
        Assert.Equal(3, times.Count);
        Assert.Single(DhikrPlan.Times(5, Start, Start.AddMinutes(5), TimeSpan.FromMinutes(15), new Random(1)));
    }

    [Fact]
    public void NothingForZeroOrAnEmptySpan()
    {
        Assert.Empty(DhikrPlan.Times(0, Start, End, TimeSpan.FromMinutes(15), new Random(1)));
        Assert.Empty(DhikrPlan.Times(3, End, Start, TimeSpan.FromMinutes(15), new Random(1)));
    }

    [Fact]
    public void EveryPhraseHasItsGlyphOutlines()
    {
        var outlines = DhikrOutlineSet.Shared;
        Assert.Equal(100, outlines.FontSize);
        foreach (var phrase in Dhikr.Phrases)
        {
            var outline = outlines.For(phrase);
            Assert.StartsWith("M", outline.Path);
            Assert.True(outline.Path.Length > 5000);
            Assert.InRange(outline.Width, 400, 900);
            Assert.True(outline.Ascent > 50 && outline.Descent > 10);
        }
    }

    [Fact]
    public void ThereAreThreePhrasesAndNeverTheSameTwiceInARow()
    {
        Assert.Equal(3, Dhikr.Phrases.Count);
        var rng = new Random(7);
        int? last = null;
        for (int i = 0; i < 500; i++)
        {
            int next = Dhikr.NextPhrase(last, Dhikr.Phrases.Count, rng);
            Assert.NotEqual(last, next);
            last = next;
        }
    }
}

public class DhikrSchedulerTests
{
    /// <summary>A scheduler on a fake clock, with the app's timer replaced by a recorded "wake me at" value.</summary>
    private sealed class Harness
    {
        public DateTime Now = new(2026, 11, 6, 7, 0, 0);
        public readonly Settings Settings = new();
        public readonly List<DateTime> Shown = [];
        public bool CanShow = true;
        public DateTime? WakeAt;
        public readonly DhikrScheduler Scheduler;

        public Harness(int seed = 1)
        {
            Scheduler = new DhikrScheduler(Settings, () => { if (CanShow) Shown.Add(Now); return CanShow; }, () => Now, new Random(seed));
            Scheduler.WakeAt += due => WakeAt = due;
        }

        /// <summary>Lets the clock reach the next wake-up and fires the timer, until `until`.</summary>
        public void RunUntil(DateTime until)
        {
            for (int guard = 0; guard < 1000 && WakeAt is { } due && due <= until; guard++)
            {
                if (due > Now) Now = due;
                Scheduler.TimerFired();
            }
            Now = until;
        }
    }

    [Fact]
    public void OffByDefaultAndNeedsNoTimer()
    {
        var h = new Harness();
        h.Scheduler.Start();
        Assert.Null(h.WakeAt);
        Assert.Null(h.Scheduler.NextPopup);
        Assert.Equal(0, h.Scheduler.PerDay);
    }

    [Fact]
    public void ShowsTheChosenNumberOfPopupsThroughTheDay()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PerDay = 5;
        h.RunUntil(new DateTime(2026, 11, 6, 23, 0, 0));
        Assert.Equal(5, h.Shown.Count);
        Assert.All(h.Shown, t => Assert.InRange(t.Hour, 8, 21));
    }

    [Fact]
    public void NeverShowsOutsideTheChosenHours()
    {
        var h = new Harness();
        h.Scheduler.StartHour = 13;
        h.Scheduler.EndHour = 15;
        h.Scheduler.Start();
        h.Scheduler.PerDay = 3;
        h.RunUntil(new DateTime(2026, 11, 7, 23, 59, 0));
        Assert.NotEmpty(h.Shown);
        Assert.All(h.Shown, t => Assert.InRange(t.Hour, 13, 14));
    }

    [Fact]
    public void ADayRollsOverAndPlansTheNextOne()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PerDay = 3;
        h.RunUntil(new DateTime(2026, 11, 8, 23, 0, 0));          // three days
        Assert.Equal(9, h.Shown.Count);
        Assert.Equal(3, h.Shown.Count(t => t.Day == 6));
        Assert.Equal(3, h.Shown.Count(t => t.Day == 7));
    }

    [Fact]
    public void ARestartKeepsTodaysPlanInsteadOfReshufflingIt()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PerDay = 5;
        var plan = h.Settings.DhikrPending.ToList();
        // the app restarts: a fresh scheduler on the same saved settings
        var again = new DhikrScheduler(h.Settings, () => true, () => h.Now, new Random(999));
        again.Start();
        Assert.Equal(plan, h.Settings.DhikrPending);
    }

    [Fact]
    public void ChangingTheCountMidDayCountsWhatWasAlreadyShown()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PerDay = 3;
        h.RunUntil(new DateTime(2026, 11, 6, 15, 0, 0));
        int shownSoFar = h.Shown.Count;
        Assert.InRange(shownSoFar, 1, 3);
        h.Scheduler.PerDay = 5;
        h.RunUntil(new DateTime(2026, 11, 6, 23, 0, 0));
        Assert.Equal(5, h.Shown.Count);
    }

    [Fact]
    public void PopupsThatPassedWhileAsleepAreSkippedNotShownLate()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PerDay = 10;
        h.Now = new DateTime(2026, 11, 6, 20, 0, 0);               // the PC slept all day
        h.Scheduler.Start();
        Assert.Empty(h.Shown);
        Assert.All(h.Settings.DhikrPending, t => Assert.True(t > h.Now - TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void WhenBusyItTriesAgainInTwoMinutes()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PerDay = 1;
        var first = h.Scheduler.NextPopup!.Value;
        h.CanShow = false;
        h.Now = first;
        h.Scheduler.TimerFired();
        Assert.Empty(h.Shown);
        Assert.Equal(first.AddSeconds(120), h.WakeAt);
        h.CanShow = true;
        h.Now = h.WakeAt!.Value;
        h.Scheduler.TimerFired();
        Assert.Single(h.Shown);
    }

    [Fact]
    public void StartIsAlwaysBeforeEnd()
    {
        var h = new Harness();
        h.Scheduler.StartHour = 22;
        Assert.True(h.Scheduler.StartHour < h.Scheduler.EndHour);
        Assert.Equal(23, h.Scheduler.EndHour);
        h.Scheduler.EndHour = 3;
        Assert.True(h.Scheduler.StartHour < h.Scheduler.EndHour);
        Assert.Equal(2, h.Scheduler.StartHour);
        h.Scheduler.StartHour = 99;
        Assert.Equal(23, h.Scheduler.StartHour);
        Assert.Equal(24, h.Scheduler.EndHour);
        h.Scheduler.EndHour = -5;
        Assert.Equal(1, h.Scheduler.EndHour);
        Assert.Equal(0, h.Scheduler.StartHour);
    }

    [Fact]
    public void NothingLeftTodayWakesAtTomorrowsStartNeverRightNow()
    {
        var h = new Harness { Now = new DateTime(2026, 11, 6, 23, 0, 0) };
        h.Scheduler.Start();
        h.Scheduler.PerDay = 3;
        Assert.Equal(new DateTime(2026, 11, 7, 8, 0, 0), h.WakeAt);
        Assert.Equal(new DateTime(2026, 11, 7, 8, 0, 0), h.Scheduler.NextPopup);
    }

    [Fact]
    public void ChoicesMatchTheMacApp()
    {
        Assert.Equal([0, 1, 3, 5, 10, 15, 20, 30], DhikrScheduler.Choices);
        Assert.Equal(24, DhikrScheduler.StartHourChoices.Length);
        Assert.Equal(24, DhikrScheduler.EndHourChoices[^1]);
    }
}
