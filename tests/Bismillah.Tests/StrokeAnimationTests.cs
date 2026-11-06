using Xunit;

namespace Bismillah.Tests;

public class StrokeAnimationTests
{
    private readonly StrokeAnimation _animation = new();

    [Fact]
    public void DataIsTheMacAppsData()
    {
        var data = BismillahData.Shared;
        Assert.Equal(112, data.Strokes.Count);
        Assert.Equal(1000, data.Width);
        Assert.Equal(4.75, data.PenEnd);
        Assert.StartsWith("M24.5 194.1", data.Outline);
        Assert.All(data.Strokes, s => Assert.True(s.PointCount >= 1 && s.Points.Length % 2 == 0));
    }

    [Fact]
    public void TotalDurationMatchesTheMacApp()
    {
        // 0.15 lead-in + (4.75 + 0.25 + 0.45) / 1.25 speed
        Assert.Equal(4.51, _animation.TotalDuration, 3);
    }

    [Fact]
    public void BeforeTheStartNothingIsDrawn()
    {
        var frame = _animation.FrameAt(-1);
        Assert.Empty(frame.Strokes);
        Assert.Equal(0, frame.FillAlpha);
        Assert.False(frame.Complete);
        Assert.Empty(_animation.FrameAt(StrokeAnimation.LeadIn).Strokes);
    }

    [Fact]
    public void AtTheEndTheWholeGlyphIsShown()
    {
        var frame = _animation.FrameAt(_animation.TotalDuration);
        Assert.True(frame.Complete);
        Assert.Equal(1, frame.FillAlpha);
        Assert.True(_animation.FrameAt(60).Complete);
    }

    [Fact]
    public void TheWritingGrowsAsTimePasses()
    {
        double previous = -1;
        for (double t = 0; t <= _animation.TotalDuration; t += 0.1)
        {
            var frame = _animation.FrameAt(t);
            if (frame.Complete) break;
            double inked = frame.Strokes.Sum(s => Length(s.Points));
            Assert.True(inked >= previous - 1e-9, $"ink shrank at t={t}");
            previous = inked;
        }
        Assert.True(previous > 0);
    }

    [Fact]
    public void ThePenFinishesBeforeTheFillFinishes()
    {
        // at the end of the recorded pen timings every stroke is complete and the fade-in is under way
        double penDone = StrokeAnimation.LeadIn + _animation.Data.PenEnd / StrokeAnimation.Speed;
        var frame = _animation.FrameAt(penDone + 0.01);
        Assert.Equal(_animation.Data.Strokes.Count, frame.Strokes.Count);
        Assert.False(frame.Complete);
    }

    [Fact]
    public void PrefixCutsAStrokeByLength()
    {
        var pen = new PenStroke { Points = [0, 0, 10, 0, 10, 10], Width = 1, Start = 0, Duration = 1 };
        var half = StrokeAnimation.Prefix(pen, 0.5);          // total length 20, so 10: exactly the corner
        Assert.Equal(20, Length(StrokeAnimation.Prefix(pen, 1)));
        Assert.Equal(10, Length(half), 6);
        var quarter = StrokeAnimation.Prefix(pen, 0.25);
        Assert.Equal(new DesignPoint(5, 0), quarter[^1]);
        var threeQuarters = StrokeAnimation.Prefix(pen, 0.75);
        Assert.Equal(new DesignPoint(10, 5), threeQuarters[^1]);
    }

    [Fact]
    public void ADotStrokeStaysADot()
    {
        var pen = new PenStroke { Points = [3, 4], Width = 1, Start = 0, Duration = 1 };
        Assert.Single(StrokeAnimation.Prefix(pen, 0.5));
    }

    private static double Length(IReadOnlyList<DesignPoint> points)
    {
        double sum = 0;
        for (int i = 1; i < points.Count; i++) sum += Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
        return sum;
    }
}
