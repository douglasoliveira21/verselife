using System.Text.Json;
using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Sound from the internet that scripts put on objects ("media" from the island server): the client downloads the mp3 or ogg (HTTPS only, the same rules as the music of a parcel, at most 12 MB, a few at a time) and plays it in 3D at the
/// object, so it fades with distance. The island repeats the message when the object moves or the volume changes (the same play), and says "off" when the script stops it. When a stream without loop ends, this client tells the island.
/// </summary>
public partial class ObjectEffects
{
    public const int MaxStreams = 8, MaxStreamBytes = 12 * 1024 * 1024, MaxDownloads = 3;
    private sealed class Stream(string url, string playId, bool loop) { public string Url { get; } = url; public string PlayId { get; } = playId; public bool Loop { get; } = loop; public AudioStreamPlayer3D? Player; public bool Loading; }
    private readonly Dictionary<string, Stream> _streamsByKey = [];
    private readonly Dictionary<string, byte[]> _streamCache = [];
    private int _downloads;

    /// <summary>Tests answer for the download: the bytes of the file, or null.</summary>
    public Func<string, byte[]?>? MediaFetchOverride { get; set; }
    public int StreamsForTesting => _streamsByKey.Count(p => p.Value.Player is not null);
    public AudioStreamPlayer3D? StreamPlayerForTesting(string key) => _streamsByKey.GetValueOrDefault(key)?.Player;
    public string StreamPlayForTesting(string key) => _streamsByKey.GetValueOrDefault(key)?.PlayId ?? "";

    private void ClearMedia()
    {
        foreach (var stream in _streamsByKey.Values) if (stream.Player is not null && IsInstanceValid(stream.Player)) stream.Player.QueueFree();
        _streamsByKey.Clear(); _streamCache.Clear();
    }

    private void StopStream(string key) { if (_streamsByKey.Remove(key, out var stream) && stream.Player is not null && IsInstanceValid(stream.Player)) { stream.Player.Stop(); stream.Player.QueueFree(); } }

    private void OnMedia(JsonElement root)
    {
        var key = root.GetProperty("key").GetString() ?? ""; if (key.Length is 0 or > 40) return;
        if (!root.GetProperty("on").GetBoolean()) { StopStream(key); return; }
        var url = root.GetProperty("url").GetString() ?? ""; var playId = root.GetProperty("playId").GetString() ?? ""; if (playId.Length is 0 or > 64 || LandMediaUrl.Check(url) is not null) return;       // the address is checked again, with its name resolved, just before the download
        double Number(string name, double fallback, double min, double max) => root.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) && double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        var volume = (float)Number("volume", .8, 0, 1); var range = (float)Number("range", 25, 3, 60); var loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.True;
        var position = new Vector3((float)Number("x", 0, -10000, 10000), (float)Number("y", 0, -10000, 10000), (float)Number("z", 0, -10000, 10000));
        if (_streamsByKey.TryGetValue(key, out var current) && current.PlayId == playId)
        {
            if (current.Player is { } player && IsInstanceValid(player)) { player.GlobalPosition = position; player.VolumeDb = Mathf.LinearToDb(Mathf.Max(volume, .001f)); player.MaxDistance = range; }
            return;       // the same play: it moved or changed its volume
        }
        StopStream(key);
        if (_streamsByKey.Count >= MaxStreams) return;
        var stream = new Stream(url, playId, loop) { Loading = true }; _streamsByKey[key] = stream;
        FetchStream(url, bytes =>
        {
            stream.Loading = false;
            if (!_streamsByKey.TryGetValue(key, out var live) || live != stream || !IsInsideTree()) return;          // it was stopped or replaced while downloading
            var audio = Decode(url, bytes, loop); if (audio is null) { _streamsByKey.Remove(key); return; }
            var node = new AudioStreamPlayer3D { Stream = audio, VolumeDb = Mathf.LinearToDb(Mathf.Max(volume, .001f)), MaxDistance = range, UnitSize = Math.Max(3f, range / 4f), AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Name = "Media_" + key };
            AddChild(node); node.GlobalPosition = position; stream.Player = node;
            node.Finished += () =>
            {
                if (!_streamsByKey.TryGetValue(key, out var now) || now != stream) return;
                _streamsByKey.Remove(key); node.QueueFree();
                if (!stream.Loop) _network?.SendScriptDialogResponse(JsonSerializer.Serialize(new { media_finished = true, key, playId }));      // somebody near has to tell the island: it stops believing a far client
            };
            node.Play();
        });
    }

    private static AudioStream? Decode(string url, byte[]? bytes, bool loop)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxStreamBytes) return null;
        try
        {
            if (new Uri(url).AbsolutePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) { var ogg = AudioStreamOggVorbis.LoadFromBuffer(bytes); if (ogg is not null) ogg.Loop = loop; return ogg; }
            return new AudioStreamMP3 { Data = bytes, Loop = loop };
        }
        catch (Exception) { return null; }
    }

    private void FetchStream(string url, Action<byte[]?> done)
    {
        if (_streamCache.TryGetValue(url, out var known)) { done(known); return; }
        if (MediaFetchOverride is not null) { var bytes = MediaFetchOverride(url); if (bytes is not null) CacheStream(url, bytes); done(bytes); return; }
        if (_downloads >= MaxDownloads) { done(null); return; }
        if (LandPresence.Refuse(url) is not null) { done(null); return; }         // a name that points into a network is refused
        _downloads++;
        var http = new HttpRequest { Timeout = 25, MaxRedirects = 0, BodySizeLimit = MaxStreamBytes, UseThreads = true }; AddChild(http);
        http.RequestCompleted += (result, code, _, body) =>
        {
            _downloads--; http.QueueFree();
            var ok = result == (long)HttpRequest.Result.Success && code == 200 && body.Length > 0 && body.Length <= MaxStreamBytes;
            if (ok) CacheStream(url, body); done(ok ? body : null);
        };
        if (http.Request(url) != Error.Ok) { _downloads--; http.QueueFree(); done(null); }
    }

    private void CacheStream(string url, byte[] bytes) { if (_streamCache.Count >= 3) _streamCache.Clear(); _streamCache[url] = bytes; }
}
