using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bismillah;

/// <summary>
/// A drawn Windows 11 style desktop, and the frames of the two README animations on it. The popups are the real
/// views the app shows, so the animations show exactly what the app draws. Run by CI (`--render-gif-frames`), which
/// saves numbered PNG files and a list of how long each is shown; tools/make_gifs.sh turns those into GIFs.
/// </summary>
internal static class DemoScene
{
    private const double Width = 760, Height = 475, TaskbarHeight = 40, OutputWidth = 800, OutputHeight = 500;

    private static Brush Hex(string color, double opacity = 1)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static TextBlock Text(string text, double size, Brush brush, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = brush, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontWeight = weight ?? FontWeights.Normal,
    };

    private static Border Box(double width, double height, Brush fill, double radius = 4) =>
        new() { Width = width, Height = height, Background = fill, CornerRadius = new CornerRadius(radius) };

    /// <summary>The wallpaper, an optional plain app window, the taskbar and the tray.</summary>
    private static Grid Desktop(bool withAppWindow)
    {
        var root = new Grid { Width = Width, Height = Height, ClipToBounds = true };

        // wallpaper: a Windows 11 style blue bloom
        var wallpaper = new Grid { Background = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0xDC, 0xE8, 0xFF), 0), new GradientStop(Color.FromRgb(0x7F, 0xA6, 0xF5), 0.45), new GradientStop(Color.FromRgb(0x2A, 0x3F, 0xB5), 1)],
            new Point(0, 0), new Point(1, 1)) };
        wallpaper.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
            { Center = new Point(0.28, 0.2), GradientOrigin = new Point(0.28, 0.2), RadiusX = 0.5, RadiusY = 0.6 } });
        wallpaper.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(120, 190, 120, 255), Color.FromArgb(0, 190, 120, 255))
            { Center = new Point(0.8, 0.85), GradientOrigin = new Point(0.8, 0.85), RadiusX = 0.55, RadiusY = 0.5 } });
        root.Children.Add(wallpaper);

        if (withAppWindow)
        {
            var window = new Border
            {
                Width = 520, Height = 300, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 70, 0, 0), Background = Hex("#FFFFFF"), CornerRadius = new CornerRadius(8),
                BorderBrush = Hex("#000000", 0.18), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.3 },
            };
            var inside = new Grid();
            inside.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            inside.RowDefinitions.Add(new RowDefinition());
            var title = new Border { Background = Hex("#F3F3F3"), CornerRadius = new CornerRadius(8, 8, 0, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(Box(14, 14, Hex("#3B82F6"), 3));
            var name = Text("Notes", 12, Hex("#1F1F1F")); name.Margin = new Thickness(8, 0, 0, 0);
            titleRow.Children.Add(name);
            title.Child = titleRow;
            inside.Children.Add(title);
            var body = new StackPanel { Margin = new Thickness(28, 26, 28, 0) };
            foreach (double w in new[] { 230.0, 380, 330, 400, 260, 360, 300 })
            {
                var line = Box(w, 8, Hex("#000000", 0.1), 4);
                line.HorizontalAlignment = HorizontalAlignment.Left;
                line.Margin = new Thickness(0, 0, 0, 16);
                body.Children.Add(line);
            }
            Grid.SetRow(body, 1);
            inside.Children.Add(body);
            window.Child = inside;
            root.Children.Add(window);
        }

        // taskbar
        var taskbar = new Grid { Height = TaskbarHeight, VerticalAlignment = VerticalAlignment.Bottom, Background = Hex("#F3F6FB", 0.9) };
        taskbar.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Hex("#000000", 0.08) });
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var start = new UniformGrid { Rows = 2, Columns = 2, Width = 18, Height = 18, Margin = new Thickness(0, 0, 14, 0) };
        for (int i = 0; i < 4; i++) start.Children.Add(Box(8, 8, Hex("#1478D4"), 1));
        icons.Children.Add(start);
        foreach (string color in new[] { "#E2E8F0", "#F59E0B", "#10B981", "#6366F1", "#EF4444" })
            icons.Children.Add(Box(22, 22, Hex(color), 6).WithMargin(new Thickness(7, 0, 7, 0)));
        taskbar.Children.Add(icons);
        var tray = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        tray.Children.Add(Text("^", 12, Hex("#202020")).WithMargin(new Thickness(0, 0, 14, 0)));
        tray.Children.Add(Box(14, 14, Hex("#202020", 0.75), 7).WithMargin(new Thickness(0, 0, 10, 0)));
        tray.Children.Add(Box(14, 12, Hex("#202020", 0.75), 3).WithMargin(new Thickness(0, 0, 14, 0)));
        var clock = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        clock.Children.Add(Text("9:41 AM", 10.5, Hex("#202020")).WithAlign(TextAlignment.Right));
        clock.Children.Add(Text("11/6/2026", 10.5, Hex("#202020")).WithAlign(TextAlignment.Right));
        tray.Children.Add(clock);
        taskbar.Children.Add(tray);
        root.Children.Add(taskbar);
        return root;
    }

    private static T WithMargin<T>(this T element, Thickness margin) where T : FrameworkElement { element.Margin = margin; return element; }
    private static TextBlock WithAlign(this TextBlock text, TextAlignment alignment) { text.TextAlignment = alignment; return text; }

    private static BitmapSource Snapshot(FrameworkElement root)
    {
        double scale = OutputWidth / Width;
        var bitmap = new RenderTargetBitmap((int)OutputWidth, (int)OutputHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static void Save(BitmapSource image, string folder, string name, int index, double delay, List<string> manifest)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string file = $"{name}-{index:000}.png";
        using (var stream = File.Create(Path.Combine(folder, file))) encoder.Save(stream);
        manifest.Add($"{file} {delay.ToString("0.###", CultureInfo.InvariantCulture)}");
    }

    private static double EaseOut(double u) => 1 - Math.Pow(1 - Math.Clamp(u, 0, 1), 2);

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        Motion.ForceAnimation = true;
        RenderWake(folder);
        RenderDhikr(folder);
        Console.WriteLine($"wrote the README animation frames to {folder}");
        return 0;
    }

    /// <summary>The screen wakes, then the bismillah is written out.</summary>
    private static void RenderWake(string folder)
    {
        var root = Desktop(withAppWindow: false);
        var popup = new PopupView();
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Margin = new Thickness(0, 0, 0, TaskbarHeight);
        popup.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        popup.RenderTransform = scale;
        popup.Opacity = 0;
        var blackout = new Border { Background = Brushes.Black, Opacity = 1 };
        root.Children.Add(popup);
        root.Children.Add(blackout);
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        var manifest = new List<string>();
        int index = 0;
        void Frame(double? tp, double black, double delay)
        {
            blackout.Opacity = black;
            if (tp is double t)
            {
                // the same envelope as the app: a quick pop in, hold, fade out
                double total = BismillahCanvas.Animation.TotalDuration, popIn = 0.164, fadeStart = total + 1.862, fadeEnd = fadeStart + 0.41;
                double opacity = 1, size = 1;
                if (t < popIn) { double e = EaseOut(t / popIn); opacity = e; size = 0.94 + 0.06 * e; }
                else if (t > fadeStart) { double e = EaseOut((t - fadeStart) / (fadeEnd - fadeStart)); opacity = 1 - e; size = 1 - 0.015 * e; }
                popup.Opacity = opacity;
                scale.ScaleX = scale.ScaleY = size;
                popup.Canvas.Time = Math.Max(t, 0) + StrokeAnimation.LeadIn;
            }
            else popup.Opacity = 0;
            root.UpdateLayout();
            Save(Snapshot(root), folder, "wake", index++, delay, manifest);
        }

        Frame(null, 1, 0.8);                                             // the screen is off
        foreach (double veil in new[] { 0.75, 0.5, 0.3, 0.12 }) Frame(null, veil, 0.1);      // it lights up
        Frame(null, 0, 0.5);
        Frame(0.06, 0, 0.06);
        Frame(0.12, 0, 0.06);
        double written = BismillahCanvas.Animation.TotalDuration;
        for (double t = 0.16; t < written - 0.15; t += 0.07) Frame(t, 0, 0.07);              // the bismillah is written out
        Frame(written, 0, 1.4);                                          // held
        foreach (double t in new[] { written + 1.9, written + 2.02, written + 2.14 }) Frame(t, 0, 0.1);      // fades away
        File.WriteAllLines(Path.Combine(folder, "wake.txt"), manifest);
    }

    /// <summary>Three dhikr open out of the top of the screen, one after another.</summary>
    private static void RenderDhikr(string folder)
    {
        var root = Desktop(withAppWindow: true);
        var manifest = new List<string>();
        int index = 0;
        var views = Dhikr.Phrases.Select(p => new DhikrView(p) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top }).ToList();
        foreach (var view in views) { view.Visibility = Visibility.Collapsed; root.Children.Add(view); }
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        void Frame(int phrase, double progress, double delay)
        {
            for (int i = 0; i < views.Count; i++) views[i].Visibility = i == phrase ? Visibility.Visible : Visibility.Collapsed;
            views[phrase].Progress = progress;
            root.UpdateLayout();
            Save(Snapshot(root), folder, "dhikr", index++, delay, manifest);
        }

        Frame(0, 0, 0.9);                                                // just the small tab
        for (int phrase = 0; phrase < views.Count; phrase++)
        {
            for (int step = 1; step <= 10; step++) Frame(phrase, 1 - Math.Pow(1 - step / 10.0, 3), 0.05);      // opens
            Frame(phrase, 1, 1.7);                                       // held (shorter than the real 4 s)
            for (int step = 1; step <= 8; step++) Frame(phrase, 1 - Math.Pow(step / 8.0, 3), 0.05);            // folds back
            Frame(phrase, 0, 0.6);
        }
        File.WriteAllLines(Path.Combine(folder, "dhikr.txt"), manifest);
    }
}
