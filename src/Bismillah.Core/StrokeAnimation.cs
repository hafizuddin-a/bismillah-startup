namespace Bismillah;

/// <summary>A point in the glyph's design space.</summary>
public readonly record struct DesignPoint(double X, double Y);

/// <summary>What has been drawn of one stroke at some moment: the points of its first part.</summary>
public sealed record StrokeProgress(int Index, double Width, IReadOnlyList<DesignPoint> Points, bool Done);

/// <summary>Everything a renderer needs to draw one frame of the writing animation.</summary>
public sealed record AnimationFrame(IReadOnlyList<StrokeProgress> Strokes, double FillAlpha, bool Complete);

/// <summary>
/// The writing animation's timeline. It knows nothing about drawing; a renderer asks for the frame at time t and
/// draws the strokes (as a mask over the glyph outline) plus the final fade-in of the whole glyph.
/// Same timings as the Mac app: a short lead-in, the recorded pen strokes played 25% faster, then the outline fades in.
/// </summary>
public sealed class StrokeAnimation
{
    /// <summary>Seconds between the start and the first pen stroke.</summary>
    public const double LeadIn = 0.15;
    /// <summary>How much faster than the recorded pen timings the writing is played.</summary>
    public const double Speed = 1.25;
    private const double FillDuration = 0.45;

    private readonly BismillahData _data;

    public StrokeAnimation(BismillahData? data = null) => _data = data ?? BismillahData.Shared;

    public BismillahData Data => _data;

    /// <summary>Recorded time at which the outline starts to fade in.</summary>
    public double FillStart => _data.PenEnd + 0.25;

    /// <summary>Seconds from the start until the text is completely drawn.</summary>
    public double TotalDuration => LeadIn + (FillStart + FillDuration) / Speed;

    /// <summary>The frame `t` seconds after the animation started. Negative t is the empty start.</summary>
    public AnimationFrame FrameAt(double t)
    {
        double local = (t - LeadIn) * Speed;               // time in the recorded pen timings
        if (local >= FillStart + FillDuration) return new AnimationFrame([], 1, true);
        var strokes = new List<StrokeProgress>();
        double fill = 0;
        if (local > 0)
        {
            for (int i = 0; i < _data.Strokes.Count; i++)
            {
                var pen = _data.Strokes[i];
                double f = Math.Clamp((local - pen.Start) / pen.Duration, 0, 1);
                if (f <= 0 || pen.PointCount == 0) continue;
                strokes.Add(new StrokeProgress(i, pen.Width, Prefix(pen, f), f >= 1));
            }
            double fillT = (local - FillStart) / FillDuration;
            if (fillT > 0) fill = 1 - Math.Pow(1 - Math.Min(fillT, 1), 2);
        }
        return new AnimationFrame(strokes, fill, false);
    }

    /// <summary>The first `fraction` (by arc length) of the pen's polyline.</summary>
    public static List<DesignPoint> Prefix(PenStroke pen, double fraction)
    {
        var pts = new List<DesignPoint>(pen.PointCount);
        for (int i = 0; i + 1 < pen.Points.Length; i += 2) pts.Add(new DesignPoint(pen.Points[i], pen.Points[i + 1]));
        if (pts.Count <= 1 || fraction >= 1) return pts;

        var cumulative = new double[pts.Count];
        for (int k = 1; k < pts.Count; k++)
            cumulative[k] = cumulative[k - 1] + Math.Sqrt(Math.Pow(pts[k].X - pts[k - 1].X, 2) + Math.Pow(pts[k].Y - pts[k - 1].Y, 2));
        double target = cumulative[^1] * fraction;
        var result = new List<DesignPoint> { pts[0] };
        for (int k = 1; k < pts.Count; k++)
        {
            if (cumulative[k] >= target)
            {
                double seg = cumulative[k] - cumulative[k - 1];
                double u = seg > 0 ? (target - cumulative[k - 1]) / seg : 0;
                result.Add(new DesignPoint(pts[k - 1].X + (pts[k].X - pts[k - 1].X) * u, pts[k - 1].Y + (pts[k].Y - pts[k - 1].Y) * u));
                break;
            }
            result.Add(pts[k]);
        }
        return result;
    }
}
