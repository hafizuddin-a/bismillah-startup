using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bismillah;

/// <summary>One pen stroke: a polyline in the glyph's design space, drawn between Start and Start + Duration seconds.</summary>
public sealed class PenStroke
{
    /// <summary>x0, y0, x1, y1, ... in the 1000-wide design space, y pointing down.</summary>
    [JsonPropertyName("points")] public double[] Points { get; set; } = [];
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }

    public int PointCount => Points.Length / 2;
}

/// <summary>
/// The bismillah as the Mac app draws it: the glyph outline (SVG path data) and the pen strokes that write it out.
/// Exported from the Mac app's generated BismillahData.swift by tools/export_data.py.
/// </summary>
public sealed class BismillahData
{
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
    [JsonPropertyName("penEnd")] public double PenEnd { get; set; }
    [JsonPropertyName("outline")] public string Outline { get; set; } = "";
    [JsonPropertyName("strokes")] public List<PenStroke> Strokes { get; set; } = [];

    private static BismillahData? _shared;

    /// <summary>The data embedded in this assembly.</summary>
    public static BismillahData Shared => _shared ??= Load();

    public static BismillahData Load()
    {
        using var stream = typeof(BismillahData).Assembly.GetManifestResourceStream("bismillah.json")
                           ?? throw new InvalidOperationException("bismillah.json is not embedded.");
        return JsonSerializer.Deserialize<BismillahData>(stream) ?? throw new InvalidOperationException("bismillah.json is empty.");
    }
}
