using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bismillah;

/// <summary>
/// Helpers for continuous integration, where no human can look at the app: draw the real WPF renderers into PNG
/// files, or put the real popup windows on a real desktop and take a picture of the screen.
/// </summary>
internal static class HeadlessRender
{
    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static RenderTargetBitmap Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>Writes the animation's frames and the dhikr shapes as PNG files into `folder`.</summary>
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var animation = BismillahCanvas.Animation;
        double[] times = [1.0, 2.0, 3.0, 3.95, animation.TotalDuration - 0.01, animation.TotalDuration];
        const int width = 520, height = 180, scale = 2;

        // every frame, stacked in one tall picture (the same layout the Skia preview uses)
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height * times.Length));
            for (int i = 0; i < times.Length; i++)
            {
                dc.PushTransform(new TranslateTransform(0, i * height));
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
                BismillahCanvas.Draw(dc, width, height, times[i]);
                dc.Pop();
                dc.Pop();
            }
        }
        var sheetVisual = new DrawingVisual();
        using (var dc = sheetVisual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(sheet.Drawing);
            dc.Pop();
        }
        SavePng(Render(sheetVisual, width * scale, height * times.Length * scale), Path.Combine(folder, "bismillah-frames.png"));

        // the dhikr shape at several moments, for each phrase
        var rows = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(70, 76, 92)) };
        for (int phrase = 0; phrase < Dhikr.Phrases.Count; phrase++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (double p in new[] { 0.0, 0.5, 1.0 })
            {
                var view = new DhikrView(Dhikr.Phrases[phrase]) { Progress = p };
                row.Children.Add(view);
            }
            rows.Children.Add(row);
        }
        rows.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        rows.Arrange(new Rect(rows.DesiredSize));
        rows.UpdateLayout();
        SavePng(Render(rows, (int)Math.Ceiling(rows.DesiredSize.Width), (int)Math.Ceiling(rows.DesiredSize.Height)), Path.Combine(folder, "dhikr-shapes.png"));
        // the dhikr in every Arabic font that might suit it, to choose one by eye
        string[] fonts = ["Segoe UI", "Segoe UI Variable Text", "Sakkal Majalla", "Arabic Typesetting", "Traditional Arabic", "Simplified Arabic",
                          "Tahoma", "Arial", "Times New Roman", "Aldhabi", "Urdu Typesetting", "Microsoft Uighur", "Noto Naskh Arabic", "Amiri"];
        var fontSheet = new StackPanel { Background = Brushes.Black, Width = 1100 };
        foreach (string name in fonts)
        {
            bool installed = new Typeface(name).TryGetGlyphTypeface(out var glyphs) && glyphs.FamilyNames.Values.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
            row.Children.Add(new TextBlock { Text = installed ? name : name + " (not installed)", Foreground = Brushes.Gray, Width = 210, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            foreach (var phrase in Dhikr.Phrases)
                row.Children.Add(new TextBlock { Text = phrase.Arabic, Foreground = installed ? Brushes.White : Brushes.DimGray, FontFamily = new FontFamily(name), FontSize = 30,
                    FlowDirection = FlowDirection.RightToLeft, Width = 290, TextAlignment = TextAlignment.Center });
            fontSheet.Children.Add(row);
        }
        fontSheet.Measure(new Size(1100, double.PositiveInfinity));
        fontSheet.Arrange(new Rect(fontSheet.DesiredSize));
        fontSheet.UpdateLayout();
        SavePng(Render(fontSheet, 1100, (int)Math.Ceiling(fontSheet.DesiredSize.Height)), Path.Combine(folder, "dhikr-fonts.png"));
        Console.WriteLine($"wrote frames to {folder}");
        return 0;
    }

    /// <summary>Opens the real popup windows on the real desktop, takes pictures of the screen along the way, and exits.</summary>
    public static int Smoke(string folder)
    {
        Directory.CreateDirectory(folder);
        Motion.ForceAnimation = true;                 // the CI machine has animations switched off; test the animated path
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                var popup = new PopupController();
                popup.Show(allScreens: true);
                await Task.Delay(1500);                       // early in the writing
                Capture(Path.Combine(folder, "screen-1-writing-early.png"));
                await Task.Delay(1700);                       // mid-writing
                Capture(Path.Combine(folder, "screen-2-writing-late.png"));
                await Task.Delay(3000);                       // finished
                Capture(Path.Combine(folder, "screen-3-finished.png"));
                await Task.Delay(2500);                       // gone again
                Console.WriteLine($"popup still showing after its time: {popup.IsShowing}");
                if (popup.IsShowing) exit = 1;

                var dhikr = new DhikrPopupController();
                dhikr.Show(allScreens: true);
                await Task.Delay(250);
                Capture(Path.Combine(folder, "screen-4-dhikr-opening.png"));
                await Task.Delay(1200);
                Capture(Path.Combine(folder, "screen-5-dhikr-open.png"));
                dhikr.Dismiss();
                await Task.Delay(500);
                Console.WriteLine($"dhikr showing after a dismiss: {dhikr.IsShowing}");
                if (dhikr.IsShowing) exit = 1;
                Console.WriteLine("smoke test finished");
            }
            catch (Exception e)
            {
                Console.WriteLine("smoke test failed: " + e);
                exit = 1;
            }
            app.Shutdown();
        };
        app.Run();
        return exit;
    }

    /// <summary>Starts the whole app (tray icon, wake monitor, scheduler), writes down what it built, and exits.</summary>
    public static int SmokeHost(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                using var host = new AppHost(() => { }, afterUpdate: true);
                await Task.Delay(1500);
                var report = new System.Text.StringBuilder();
                report.AppendLine("== menu ==");
                report.Append(host.DescribeMenu());
                StartupRegistration.Set(true);
                bool on = StartupRegistration.IsEnabled;
                StartupRegistration.Set(false);
                bool off = !StartupRegistration.IsEnabled;
                report.AppendLine("== launch at login ==");
                report.AppendLine($"turned on: {on}, turned off: {off}");
                report.AppendLine("== session ==");
                report.AppendLine($"locked: {Native.IsSessionLocked()}");
                report.AppendLine($"settings file: {Settings.DefaultPath}");
                report.AppendLine($"screens: {System.Windows.Forms.Screen.AllScreens.Length}, scale of primary: {Native.ScaleOf(System.Windows.Forms.Screen.PrimaryScreen!)}");
                File.WriteAllText(Path.Combine(folder, "host-report.txt"), report.ToString());
                Console.WriteLine(report);
                if (!on || !off) exit = 1;
            }
            catch (Exception e)
            {
                Console.WriteLine("host smoke test failed: " + e);
                File.WriteAllText(Path.Combine(folder, "host-report.txt"), "FAILED: " + e);
                exit = 1;
            }
            app.Shutdown();
        };
        app.Run();
        return exit;
    }

    private static void Capture(string path)
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception e)
        {
            Console.WriteLine($"couldn't capture {path}: {e.Message}");
        }
    }
}
