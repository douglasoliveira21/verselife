namespace LifeVerse.Shared.Voice;

public readonly record struct VoicePeer(int PeerId, float X, float Y, float Z);

/// What the island server checks before passing a voice packet on: it must be a sane size, the speaker must not
/// be flooding, and only players inside hearing range receive it. Pure rules, tested without Godot.
public sealed class VoiceRelay
{
    private readonly Dictionary<int, Queue<long>> _recent = [];

    /// `nowMilliseconds` comes from the caller so tests can control time.
    public bool Allow(int speaker, long nowMilliseconds, int packetLength)
    {
        if (packetLength is <= 0 or > VoiceSettings.MaxPacketBytes) return false;
        if (!_recent.TryGetValue(speaker, out var times)) _recent[speaker] = times = new Queue<long>();
        while (times.Count > 0 && nowMilliseconds - times.Peek() >= 1000) times.Dequeue();
        if (times.Count >= VoiceSettings.MaxPacketsPerSecond) return false;
        times.Enqueue(nowMilliseconds);
        return true;
    }

    /// Everyone but the speaker within range, optionally skipping pairs where either side has blocked the other.
    public static IReadOnlyList<int> Hearers(int speaker, IReadOnlyCollection<VoicePeer> players, Func<int, int, bool>? blocked = null)
    {
        var source = players.FirstOrDefault(p => p.PeerId == speaker);
        if (source.PeerId != speaker) return [];
        var range = VoiceSettings.HearingRange * VoiceSettings.HearingRange;
        var result = new List<int>();
        foreach (var other in players)
        {
            if (other.PeerId == speaker) continue;
            var dx = other.X - source.X; var dy = other.Y - source.Y; var dz = other.Z - source.Z;
            var distance = dx * dx + dy * dy + dz * dz;
            if (!float.IsFinite(distance) || distance > range) continue;
            if (blocked is not null && (blocked(speaker, other.PeerId) || blocked(other.PeerId, speaker))) continue;
            result.Add(other.PeerId);
        }
        return result;
    }

    public void Forget(int peer) => _recent.Remove(peer);
}
