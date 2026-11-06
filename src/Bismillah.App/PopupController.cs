using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;

namespace Bismillah;

/// <summary>One black rounded window holding the writing animation.</summary>
internal sealed class PopupWindow : Window
{
    public const double PopupWidth = 520, PopupHeight = 180, ShadowMargin = 60;
    public readonly BismillahCanvas Canvas = new();

    public PopupWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Width = PopupWidth + 2 * ShadowMargin;
        Height = PopupHeight + 2 * ShadowMargin;
        Title = "Bismillah";
        AutomationProperties_Name();

        var grid = new Grid();
        // the shadow sits on its own static layer so it isn't redrawn every frame
        grid.Children.Add(new Border
        {
            Margin = new Thickness(ShadowMargin), Background = Brushes.Black, CornerRadius = new CornerRadius(30),
            Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 12, Direction = 270, Opacity = 0.45, Color = Colors.Black },
        });
        var body = new Border
        {
            Margin = new Thickness(ShadowMargin), Background = Brushes.Black, CornerRadius = new CornerRadius(30),
            BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Canvas,
        };
        grid.Children.Add(body);
        Content = grid;
        SourceInitialized += (_, _) => ScreenPlacement.MakeNonActivating(this);
    }

    private void AutomationProperties_Name() => System.Windows.Automation.AutomationProperties.SetName(this, "Bismillah");
}

/// <summary>
/// Shows the bismillah popup centred on one screen (or every screen), plays the animation, then fades it out.
/// A click, or Esc, dismisses it early.
/// </summary>
internal sealed class PopupController
{
    private const double Fade = 0.35, HoldAfterWriting = 2.0;

    private readonly List<PopupWindow> _windows = [];
    private FrameClock? _clock;
    private bool _reduceMotion;
    private double? _hideStart;         // set once the popup starts fading out
    private bool _escapeWasDown;

    public bool IsShowing => _windows.Count > 0;

    public void Show(bool allScreens)
    {
        Close();
        _reduceMotion = Motion.Reduce;
        _hideStart = null;
        _escapeWasDown = (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0;

        foreach (var screen in ScreenPlacement.Targets(allScreens))
        {
            var window = new PopupWindow { Opacity = 0 };
            window.MouseLeftButtonDown += (_, _) => BeginHide();
            window.Show();
            ScreenPlacement.Place(window, screen, new Size(window.Width, window.Height), ScreenPlacement.Centered);
            window.DpiChanged += (_, _) => ScreenPlacement.Place(window, screen, new Size(window.Width, window.Height), ScreenPlacement.Centered);
            _windows.Add(window);
        }
        _clock = new FrameClock(Tick);
    }

    private double WritingStart => Fade * 0.6;
    private double HideAt => BismillahCanvas.Animation.TotalDuration + HoldAfterWriting;

    private void Tick(double t)
    {
        if (_windows.Count == 0) return;
        // Esc, wherever the keyboard focus is: this window never takes focus, so it can't receive the key itself
        bool escape = (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0;
        if (escape && !_escapeWasDown && _hideStart is null) BeginHide();
        _escapeWasDown = escape;

        double opacity;
        if (_hideStart is double start)
        {
            double u = (t - start) / Fade;
            if (u >= 1) { Close(); return; }
            opacity = 1 - u;
        }
        else
        {
            opacity = Math.Min(t / Fade, 1);
            if (t >= HideAt) { BeginHide(); }
        }
        double animationTime = _reduceMotion ? BismillahCanvas.Animation.TotalDuration : t - WritingStart;
        foreach (var window in _windows)
        {
            window.Opacity = opacity;
            window.Canvas.Time = animationTime;
        }
    }

    private void BeginHide()
    {
        if (_hideStart is null && _clock is not null) _hideStart = _clock.Elapsed;
    }

    public void Close()
    {
        _clock?.Dispose();
        _clock = null;
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }
}
