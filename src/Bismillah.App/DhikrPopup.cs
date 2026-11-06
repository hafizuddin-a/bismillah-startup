using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Bismillah;

/// <summary>
/// The black shape that opens out of the top edge of the screen and holds the dhikr. `Progress` draws any moment of
/// the animation, 0 = a small tab, 1 = fully open, so the whole thing can also be rendered offscreen.
/// (Windows has no notch, so this always opens from the top centre, like the Mac app on a Mac without one.)
/// </summary>
internal sealed class DhikrShape : FrameworkElement
{
    public const double ClosedWidth = 120, ClosedHeight = 4, OpenWidth = 370, TopInset = 12, TextAreaHeight = 70, ShadowMargin = 36;
    public const double OpenHeight = TopInset + TextAreaHeight;

    public static double WindowWidth => OpenWidth + 2 * ShadowMargin;
    public static double WindowHeight => OpenHeight + ShadowMargin;

    private double _progress;
    private Geometry _shape = Geometry.Empty;

    /// <summary>0 = closed tab, 1 = fully open.</summary>
    public double Progress
    {
        get => _progress;
        set { _progress = Math.Clamp(value, 0, 1); _shape = Build(_progress); InvalidateVisual(); }
    }

    public Geometry ShapeGeometry => _shape;

    public static Geometry Build(double p)
    {
        double Lerp(double a, double b) => a + (b - a) * p;
        double width = Lerp(ClosedWidth, OpenWidth), height = Lerp(ClosedHeight, OpenHeight);
        double radius = Math.Min(Lerp(6, 30), Math.Min(height / 2, width / 2));
        double x0 = (WindowWidth - width) / 2, x1 = x0 + width;
        // flush with the top edge of the window (= the top edge of the screen); only the bottom corners are round
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x0, 0), true, true);
            ctx.LineTo(new Point(x1, 0), true, false);
            ctx.LineTo(new Point(x1, height - radius), true, false);
            ctx.ArcTo(new Point(x1 - radius, height), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(x0 + radius, height), true, false);
            ctx.ArcTo(new Point(x0, height - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawGeometry(Brushes.Black, null, _shape);

    // Only the black shape takes clicks, so the transparent margin around it never gets in the way.
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        _shape.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
}

/// <summary>The shape plus its text: what the dhikr window shows, and what the headless renderer draws.</summary>
internal sealed class DhikrView : Grid
{
    public readonly DhikrShape Shape = new();
    private readonly TextBlock _label;

    public DhikrView(Dhikr phrase)
    {
        Width = DhikrShape.WindowWidth;
        Height = DhikrShape.WindowHeight;
        Shape.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5, Color = Colors.Black };
        Children.Add(Shape);

        _label = new TextBlock
        {
            Text = phrase.Arabic,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI, Sakkal Majalla, Arial"),
            FontSize = FitFontSize(phrase.Arabic),
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Center,
            Width = DhikrShape.OpenWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, DhikrShape.TopInset + (DhikrShape.TextAreaHeight - 44) / 2, 0, 0),
            Height = 44,
            IsHitTestVisible = false,
            Opacity = 0,
        };
        Children.Add(_label);
        System.Windows.Automation.AutomationProperties.SetName(this, phrase.Reading);
        Progress = 0;
    }

    /// <summary>30 pt, or smaller if a longer phrase wouldn't fit the open shape with some air around it.</summary>
    private static double FitFontSize(string text)
    {
        double Width(double size)
        {
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("ar"), FlowDirection.RightToLeft,
                new Typeface(new FontFamily("Segoe UI, Sakkal Majalla, Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size, Brushes.White, 1.0);
            return formatted.WidthIncludingTrailingWhitespace;
        }
        double fit = DhikrShape.OpenWidth - 56, natural = Width(30);
        return natural > fit ? Math.Floor(30 * fit / natural) : 30;
    }

    public double Progress
    {
        get => Shape.Progress;
        set
        {
            Shape.Progress = value;
            _label.Opacity = Math.Clamp((Shape.Progress - 0.55) / 0.35, 0, 1);       // the text arrives once the shape is mostly open
        }
    }
}

internal sealed class DhikrWindow : Window
{
    public readonly DhikrView View;

    public DhikrWindow(Dhikr phrase)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Title = "Bismillah dhikr";
        Width = DhikrShape.WindowWidth;
        Height = DhikrShape.WindowHeight;
        View = new DhikrView(phrase);
        Content = View;
        SourceInitialized += (_, _) => ScreenPlacement.MakeNonActivating(this);
    }
}

/// <summary>Shows the dhikr: the shape grows out of the top of the screen, holds, then folds back into it.</summary>
internal sealed class DhikrPopupController
{
    private const double Expand = 0.5, Hold = 4.0, Collapse = 0.4, DismissTime = 0.3;

    private readonly List<DhikrWindow> _windows = [];
    private FrameClock? _clock;
    private bool _reduceMotion;
    private int? _lastPhrase;
    private double _lastProgress;
    private (double Start, double From)? _closing;          // set once the popup has been clicked

    public bool IsShowing => _windows.Count > 0;

    /// <summary>0 = closed, 1 = open, `t` seconds into the animation.</summary>
    public static double ProgressAt(double t)
    {
        if (t < Expand) return 1 - Math.Pow(1 - t / Expand, 3);
        if (t < Expand + Hold) return 1;
        double u = (t - Expand - Hold) / Collapse;
        return u >= 1 ? 0 : 1 - u * u * u;
    }

    /// <summary>Shows one dhikr, the same on every screen it appears on.</summary>
    public void Show(bool allScreens)
    {
        if (IsShowing) return;
        int index = Dhikr.NextPhrase(_lastPhrase, Dhikr.Phrases.Count);
        _lastPhrase = index;
        var phrase = Dhikr.Phrases[index];
        _reduceMotion = Motion.Reduce;
        _closing = null;
        _lastProgress = 0;

        foreach (var screen in ScreenPlacement.Targets(allScreens))
        {
            var window = new DhikrWindow(phrase) { Opacity = _reduceMotion ? 0 : 1 };
            window.View.Progress = _reduceMotion ? 1 : 0;
            window.View.Shape.MouseLeftButtonDown += (_, _) => Dismiss();
            window.Show();
            var size = new Size(window.Width, window.Height);
            ScreenPlacement.Place(window, screen, size, ScreenPlacement.TopCentre);
            window.DpiChanged += (_, _) => ScreenPlacement.Place(window, screen, size, ScreenPlacement.TopCentre);
            _windows.Add(window);
        }
        _clock = new FrameClock(Tick);
    }

    /// <summary>A click on the popup: fold it away now instead of waiting out the hold.</summary>
    public void Dismiss()
    {
        if (!IsShowing || _closing is not null || _clock is null) return;
        _closing = (_clock.Elapsed, _reduceMotion ? 1 : _lastProgress);
    }

    private void Tick(double t)
    {
        if (_windows.Count == 0) return;
        if (_reduceMotion)
        {
            // fade in, hold, fade out; the shape stays fully open
            double total = Expand + Hold + 0.3, opacity;
            double end = _closing is { } c ? c.Start + 0.2 : total;
            if (t >= end) { Close(); return; }
            opacity = _closing is { } cl ? 1 - (t - cl.Start) / 0.2
                    : t < 0.25 ? t / 0.25 : t > Expand + Hold ? 1 - (t - Expand - Hold) / 0.3 : 1;
            foreach (var window in _windows) window.Opacity = Math.Clamp(opacity, 0, 1);
            return;
        }
        if (_closing is { } closing)
        {
            double u = (t - closing.Start) / DismissTime;
            if (u >= 1) { Close(); return; }
            _lastProgress = closing.From * (1 - u * u * u);
        }
        else
        {
            _lastProgress = ProgressAt(t);
            if (t >= Expand + Hold + Collapse) { Close(); return; }
        }
        foreach (var window in _windows) window.View.Progress = _lastProgress;
    }

    public void Close()
    {
        _clock?.Dispose();
        _clock = null;
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        _closing = null;
    }
}
