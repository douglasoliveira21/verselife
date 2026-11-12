using System.Text.Json;

namespace LifeVerse.Shared.Land;

/// <summary>
/// Rules of a parcel that the island server has to apply on its own, as the API's land-sync tells them: so far, where proximity voice is switched off. On such a parcel nobody speaks and nobody hears: the server drops the audio
/// of a speaker standing there and does not play anybody's voice to a listener standing there.
/// </summary>
public sealed class LandRules
{
    private readonly HashSet<long> _noVoice = [];

    public static LandRules Empty { get; } = new();
    public bool HasZones => _noVoice.Count > 0;

    /// <summary>Reads { parcels: [{ id, voice: false, runs: [[cz, x0, x1]] }] } (only parcels with something switched off are sent).</summary>
    public static LandRules Parse(JsonElement rules)
    {
        var map = new LandRules();
        if (rules.ValueKind != JsonValueKind.Object || !rules.TryGetProperty("parcels", out var parcels) || parcels.ValueKind != JsonValueKind.Array) return map;
        foreach (var item in parcels.EnumerateArray())
        {
            try
            {
                if (item.TryGetProperty("voice", out var voice) && voice.ValueKind == JsonValueKind.False)
                    foreach (var cell in LandMath.FromRuns(item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()))) map._noVoice.Add(cell);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException) { /* a damaged entry is skipped */ }
        }
        return map;
    }

    /// <summary>False when voice is off at this spot.</summary>
    public bool VoiceAllowedAt(double x, double z) => !HasZones || !_noVoice.Contains(LandMath.Key(LandMath.CellOf(x), LandMath.CellOf(z)));
}
