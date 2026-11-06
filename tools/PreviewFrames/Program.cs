// Draws frames of the writing animation with Skia, so the timeline and the data can be checked on any machine
// (the app itself draws with WPF, which only runs on Windows).
//
//   dotnet run --project tools/PreviewFrames -- out.png
using Bismillah;
using SkiaSharp;

var animation = new StrokeAnimation();
var data = animation.Data;
double[] times = [1.0, 2.0, 3.0, 3.95, animation.TotalDuration - 0.01, animation.TotalDuration];
const int width = 520, height = 180, scale = 2;
using var surface = SKSurface.Create(new SKImageInfo(width * scale, height * scale * times.Length));
var canvas = surface.Canvas;
canvas.Clear(SKColors.Black);
var outline = SKPath.ParseSvgPathData(data.Outline);

for (int i = 0; i < times.Length; i++)
{
    canvas.Save();
    canvas.Translate(0, i * height * scale);
    canvas.Scale(scale);
    canvas.ClipRect(new SKRect(0, 0, width, height));
    var frame = animation.FrameAt(times[i]);
    double s = width * 0.88 / data.Width;
    float ox = (float)((width - data.Width * s) / 2), oy = (float)((height - data.Height * s) / 2);

    // mask layer: strokes (and the fill-in) in alpha, then the outline drawn through it
    canvas.SaveLayer();
    canvas.Save();
    canvas.Translate(ox, oy);
    canvas.Scale((float)s);
    canvas.DrawPath(outline, new SKPaint { Color = SKColors.White, IsAntialias = true });
    canvas.Restore();
    using var maskLayer = new SKPaint { BlendMode = SKBlendMode.DstIn };
    canvas.SaveLayer(maskLayer);
    if (frame.Complete) canvas.Clear(SKColors.White);
    else
    {
        canvas.Save();
        canvas.Translate(ox, oy);
        canvas.Scale((float)s);
        foreach (var stroke in frame.Strokes)
        {
            using var pen = new SKPaint { Color = SKColors.White, IsAntialias = true, IsStroke = true, StrokeWidth = (float)stroke.Width,
                                          StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            using var path = new SKPath();
            path.MoveTo((float)stroke.Points[0].X, (float)stroke.Points[0].Y);
            if (stroke.Points.Count == 1) path.LineTo((float)stroke.Points[0].X + 0.01f, (float)stroke.Points[0].Y);
            foreach (var p in stroke.Points.Skip(1)) path.LineTo((float)p.X, (float)p.Y);
            canvas.DrawPath(path, pen);
        }
        canvas.Restore();
        if (frame.FillAlpha > 0) canvas.DrawRect(0, 0, width, height, new SKPaint { Color = SKColors.White.WithAlpha((byte)(frame.FillAlpha * 255)) });
    }
    canvas.Restore();
    canvas.Restore();
    canvas.Restore();
}
using var image = surface.Snapshot();
using var png = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(args.Length > 0 ? args[0] : "frames.png", png.ToArray());
Console.WriteLine($"wrote {args.ElementAtOrDefault(0) ?? "frames.png"} ({times.Length} frames, total {animation.TotalDuration:0.00}s)");
