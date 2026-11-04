using System.Globalization;
using System.Text.Json;

namespace LifeVerse.Shared.Land;

/// <summary>
/// The look of a parcel's surroundings: any of the time of day, the weather, the fog, the strength of the light and the colour of the sky.
/// A setting that is not given is inherited from the island; a parcel only overrides what it names. Values are kept inside sane limits.
/// </summary>
public sealed record ParcelEnvironment(float? Hour = null, string? Weather = null, float? Fog = null, float? Light = null, string? Sky = null)
{
    public static readonly string[] Weathers = ["clear", "cloudy", "rain", "storm", "fog"];
    public const float MinLight = .2f, MaxLight = 1.6f;

    public bool IsEmpty => Hour is null && Weather is null && Fog is null && Light is null && Sky is null;

    /// <summary>Reads settings leniently: a field that is missing, of the wrong kind or out of range is ignored (hours wrap, other numbers are clamped).</summary>
    public static ParcelEnvironment Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ParcelEnvironment();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new ParcelEnvironment();
            float? Number(string key, float min, float max, bool wrap = false)
            {
                if (!root.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d)) return null;
                var value = wrap ? (float)(((d % 24) + 24) % 24) : (float)Math.Clamp(d, min, max);
                return MathF.Round(value, 2);
            }
            string? weather = root.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.String && Weathers.Contains(w.GetString()) ? w.GetString() : null;
            string? sky = root.TryGetProperty("sky", out var s) && s.ValueKind == JsonValueKind.String && IsColour(s.GetString()) ? s.GetString()!.ToUpperInvariant() : null;
            return new ParcelEnvironment(Number("hour", 0, 24, wrap: true), weather, Number("fog", 0, 1), Number("light", MinLight, MaxLight), sky);
        }
        catch (JsonException) { return new ParcelEnvironment(); }
    }

    public static bool IsColour(string? text) => text is { Length: 7 } && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>The normalized JSON with only the settings that are set.</summary>
    public string ToJson()
    {
        var parts = new List<string>();
        if (Hour is { } h) parts.Add($"\"hour\":{h.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (Weather is { } w) parts.Add($"\"weather\":\"{w}\"");
        if (Fog is { } f) parts.Add($"\"fog\":{f.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (Light is { } l) parts.Add($"\"light\":{l.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (Sky is { } s) parts.Add($"\"sky\":\"{s}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>What a person sees: the island's settings with this parcel's own settings on top.</summary>
    public ParcelEnvironment Over(ParcelEnvironment island) => new(Hour ?? island.Hour, Weather ?? island.Weather, Fog ?? island.Fog, Light ?? island.Light, Sky ?? island.Sky);
}
