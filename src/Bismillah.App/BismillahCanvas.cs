using System.Windows;
using System.Windows.Media;

namespace Bismillah;

/// <summary>
/// Draws the writing animation: the glyph outline seen through a mask made of the pen strokes drawn so far, with the
/// whole outline fading in at the end. `Draw` takes any DrawingContext, so the same code renders on screen and into
/// pictures (see HeadlessRender).
/// </summary>
internal sealed class BismillahCanvas : FrameworkElement
{
    public static readonly StrokeAnimation Animation = new();
    private static readonly Geometry Glyph = BuildGlyph();
    private static readonly Dictionary<int, (Geometry Shape, Pen Pen)> FinishedStrokes = [];

    private double _time = -1;

    /// <summary>Seconds since the animation started; setting it redraws.</summary>
    public double Time
    {
        get => _time;
        set { _time = value; InvalidateVisual(); }
    }

    private static Geometry BuildGlyph()
    {
        // "F1" = non-zero winding, like the Mac renderer (WPF's default is even-odd)
        var geometry = Geometry.Parse("F1 " + Animation.Data.Outline);
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext dc) => Draw(dc, ActualWidth, ActualHeight, _time);

    public static void Draw(DrawingContext dc, double width, double height, double t)
    {
        var data = Animation.Data;
        double scale = width * 0.88 / data.Width;
        double ox = (width - data.Width * scale) / 2, oy = (height - data.Height * scale) / 2;
        var frame = Animation.FrameAt(t);
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, ox, oy));
        if (frame.Complete)
        {
            dc.DrawGeometry(Brushes.White, null, Glyph);
        }
        else if (frame.Strokes.Count > 0 || frame.FillAlpha > 0)
        {
            // the visible area in design coordinates, so the mask lines up with the glyph
            var area = new Rect(-ox / scale, -oy / scale, width / scale, height / scale);
            var mask = new DrawingGroup();
            using (var m = mask.Open())
            {
                m.DrawRectangle(Brushes.Transparent, null, area);        // fixes the mask's extent
                foreach (var stroke in frame.Strokes)
                {
                    var (shape, pen) = stroke.Done ? CachedStroke(stroke) : Build(stroke);
                    m.DrawGeometry(null, pen, shape);
                }
                if (frame.FillAlpha > 0)
                    m.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(frame.FillAlpha * 255), 255, 255, 255)), null, area);
            }
            mask.Freeze();
            var brush = new DrawingBrush(mask)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = area,
                ViewportUnits = BrushMappingMode.Absolute, Viewport = area,
                Stretch = Stretch.Fill,
            };
            brush.Freeze();
            dc.PushOpacityMask(brush);
            dc.DrawGeometry(Brushes.White, null, Glyph);
            dc.Pop();
        }
        dc.Pop();
    }

    private static (Geometry Shape, Pen Pen) CachedStroke(StrokeProgress stroke)
    {
        if (!FinishedStrokes.TryGetValue(stroke.Index, out var built))
            FinishedStrokes[stroke.Index] = built = Build(stroke);
        return built;
    }

    private static (Geometry Shape, Pen Pen) Build(StrokeProgress stroke)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = stroke.Points[0];
            ctx.BeginFigure(new Point(first.X, first.Y), false, false);
            if (stroke.Points.Count == 1) ctx.LineTo(new Point(first.X + 0.01, first.Y), true, true);        // a dot
            for (int i = 1; i < stroke.Points.Count; i++) ctx.LineTo(new Point(stroke.Points[i].X, stroke.Points[i].Y), true, true);
        }
        geometry.Freeze();
        var pen = new Pen(Brushes.White, stroke.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return (geometry, pen);
    }
}
