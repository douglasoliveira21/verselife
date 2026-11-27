using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Sound from the internet that scripts put on their objects (media.play): announced to everybody on the island (and to whoever arrives later, who hears it from the beginning), moved with the object, removed with the script. The clients download and play it; one of them near the object tells when it ends.</summary>
public sealed partial class IslandScripting
{
    private readonly Dictionary<Guid, (Guid Object, MediaSpec Spec, double Started)> _media = [];
    private readonly Dictionary<Guid, double> _soundFinishedAt = [];
    public const double MinMediaSeconds = 1, ReporterExtraRange = 10, MaxSoundReportDistance = 80;

    public bool PlayMedia(ScriptDefinition script, MediaSpec spec)
    {
        if (!_objects.TryGetValue(script.ObjectId, out var target)) return false;
        var started = _media.TryGetValue(script.Id, out var before) && before.Spec.PlayId == spec.PlayId ? before.Started : Now;      // a change of volume continues the same play
        _media[script.Id] = (script.ObjectId, spec, started);
        _broadcastApplied(MediaOp(script.Id, script.ObjectId, spec, true, target));
        return true;
    }

    public void StopMedia(ScriptDefinition script)
    {
        if (!_media.Remove(script.Id, out var gone)) return;
        _broadcastApplied(MediaOp(script.Id, gone.Object, gone.Spec, false, _objects.GetValueOrDefault(gone.Object)));
    }

    private static string MediaOp(Guid script, Guid objectId, MediaSpec spec, bool on, CachedObject? at) =>
        JsonSerializer.Serialize(new { operation = "media", key = script.ToString("N"), objectId, on, url = spec.Url, volume = spec.Volume, loop = spec.Loop, range = spec.Range, playId = spec.PlayId, x = at?.Position.X ?? 0, y = at?.Position.Y ?? 0, z = at?.Position.Z ?? 0 });

    /// <summary>The streams that are on now, as messages for somebody who has just arrived.</summary>
    public IEnumerable<string> MediaOps() => _media.Where(p => _objects.ContainsKey(p.Value.Object)).Select(p => MediaOp(p.Key, p.Value.Object, p.Value.Spec, true, _objects[p.Value.Object]));

    /// <summary>An object with a stream moved: the sound follows it.</summary>
    private void FollowMedia(Guid objectId)
    {
        if (!_objects.TryGetValue(objectId, out var at)) return;
        foreach (var (script, active) in _media.Where(p => p.Value.Object == objectId).ToList()) _broadcastApplied(MediaOp(script, objectId, active.Spec, true, at));
    }

    /// <summary>A client says the stream it was playing reached its end. Believed only for the play that is current, not a loop, after it had time to play, and from somebody near the object (so a faraway client cannot cut a stream short).</summary>
    private bool TryMediaFinished(AvatarPresence who, JsonElement root)
    {
        if (!root.TryGetProperty("media_finished", out var flag) || flag.ValueKind != JsonValueKind.True) return false;
        if (!root.TryGetProperty("key", out var k) || !Guid.TryParseExact(k.GetString(), "N", out var script) || !root.TryGetProperty("playId", out var p) || p.GetString() is not { } playId) return true;
        if (!_media.TryGetValue(script, out var active) || active.Spec.PlayId != playId || active.Spec.Loop || Now - active.Started < MinMediaSeconds) return true;
        if (!_objects.TryGetValue(active.Object, out var target) || Distance(who, target) > active.Spec.Range + ReporterExtraRange) return true;
        _host.MediaFinished(script, playId); return true;
    }

    /// <summary>A client says a sound of the object (audio.play) ended: its scripts hear on_sound_finished, at most twice a second per object, from somebody within 80 m.</summary>
    private bool TrySoundFinished(AvatarPresence who, JsonElement root)
    {
        if (!root.TryGetProperty("sound_finished", out var flag) || flag.ValueKind != JsonValueKind.True) return false;
        if (!root.TryGetProperty("objectId", out var o) || !Guid.TryParse(o.GetString(), out var objectId) || !_objects.TryGetValue(objectId, out var target) || Distance(who, target) > MaxSoundReportDistance || !_host.Wants(objectId, "sound_finished")) return true;
        var now = Now; if (_soundFinishedAt.TryGetValue(objectId, out var last) && now - last < .5) return true;
        if (_soundFinishedAt.Count > 500) _soundFinishedAt.Clear(); _soundFinishedAt[objectId] = now;
        _host.SoundFinished(objectId); return true;
    }
}
