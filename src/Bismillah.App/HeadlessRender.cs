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
        Console.WriteLine($"wrote frames to {folder}");
        return 0;
    }

    /// <summary>Opens the real popup windows on the real desktop, waits, takes a picture of the screen, and exits.</summary>
    public static int Smoke(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                var popup = new PopupController();
                popup.Show(allScreens: true);
                await Task.Delay(3200);                       // mid-writing
                Capture(Path.Combine(folder, "screen-writing.png"));
                await Task.Delay(3500);                       // finished
                Capture(Path.Combine(folder, "screen-finished.png"));
                popup.Close();

                var dhikr = new DhikrPopupController();
                dhikr.Show(allScreens: true);
                await Task.Delay(1500);
                Capture(Path.Combine(folder, "screen-dhikr.png"));
                dhikr.Close();
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
