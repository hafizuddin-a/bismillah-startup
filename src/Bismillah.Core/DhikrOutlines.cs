using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bismillah;

/// <summary>
/// One dhikr phrase as glyph outlines: SVG path data in a y-down space where the baseline is y = 0 and the font size is
/// <see cref="DhikrOutlineSet.FontSize"/>. Exported from the Mac with Geeza Pro by tools/export_dhikr.swift, so the Arabic
/// looks the same on every PC and doesn't depend on which fonts Windows has.
/// </summary>
public sealed class DhikrOutline
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("ascent")] public double Ascent { get; set; }
    [JsonPropertyName("descent")] public double Descent { get; set; }
}

public sealed class DhikrOutlineSet
{
    [JsonPropertyName("fontSize")] public double FontSize { get; set; }
    [JsonPropertyName("phrases")] public List<DhikrOutline> Phrases { get; set; } = [];

    private static DhikrOutlineSet? _shared;
    public static DhikrOutlineSet Shared => _shared ??= Load();

    public static DhikrOutlineSet Load()
    {
        using var stream = typeof(DhikrOutlineSet).Assembly.GetManifestResourceStream("dhikr.json")
                           ?? throw new InvalidOperationException("dhikr.json is not embedded.");
        return JsonSerializer.Deserialize<DhikrOutlineSet>(stream) ?? throw new InvalidOperationException("dhikr.json is empty.");
    }

    /// <summary>The outline for a phrase of <see cref="Dhikr.Phrases"/>.</summary>
    public DhikrOutline For(Dhikr phrase) => Phrases.First(p => p.Text == phrase.Arabic);
}
