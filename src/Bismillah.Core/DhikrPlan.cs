namespace Bismillah;

/// <summary>The dhikr phrases, with how each is read aloud.</summary>
public sealed record Dhikr(string Arabic, string Reading)
{
    public static readonly IReadOnlyList<Dhikr> Phrases =
    [
        new("سُبْحَانَ ٱللَّهِ وَبِحَمْدِهِ", "Subhanallahi wa bihamdihi"),
        new("أَسْتَغْفِرُ ٱللَّهَ ٱلْعَظِيمَ", "Astaghfirullah al-Azim"),
        new("ٱللَّهُمَّ صَلِّ عَلَىٰ مُحَمَّدٍ", "Allahumma salli ala Muhammad"),
    ];

    /// <summary>A random phrase number out of `count`, never the same as `last`, so you never see one twice in a row.</summary>
    public static int NextPhrase(int? last, int count, Random? rng = null)
    {
        rng ??= Random.Shared;
        var choices = Enumerable.Range(0, count).Where(i => i != last).ToList();
        return choices.Count == 0 ? 0 : choices[rng.Next(choices.Count)];
    }
}

/// <summary>Picks the random moments for a day's dhikr popups.</summary>
public static class DhikrPlan
{
    /// <summary>
    /// `count` random times between `start` and `end`, in order.
    /// The span is cut into `count` equal slices and one time is drawn from the middle 80% of each, so the popups are
    /// random but spread over the whole day rather than bunched together. If the span is too short to keep `minGap`
    /// between popups, fewer are planned.
    /// </summary>
    public static List<DateTime> Times(int count, DateTime start, DateTime end, TimeSpan minGap, Random rng)
    {
        double span = (end - start).TotalSeconds;
        if (count <= 0 || span <= 0) return [];
        int n = Math.Min(count, Math.Max(1, (int)(span / minGap.TotalSeconds)));
        double slice = span / n;
        var times = new List<DateTime>(n);
        for (int i = 0; i < n; i++)
        {
            double low = slice * (i + 0.1), high = slice * (i + 0.9);
            times.Add(start.AddSeconds(low + rng.NextDouble() * (high - low)));
        }
        return times;
    }
}
