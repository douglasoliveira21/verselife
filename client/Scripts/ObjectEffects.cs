using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// What scripts make objects do that people see and hear: the island server announces "sound" (play a sound stored in the object's Conteúdo), "sound_stop" and "text"
/// (floating text over the object) through the build-operation channel. Sounds are downloaded once, decoded (WAV or OGG) and played in 3D at the object, so they fade
/// with distance; texts are billboards. Everything is cleared when the person changes island.
/// </summary>
public partial class ObjectEffects : Node
{
    public const int MaxSounds = 16;
    public const int MaxTexts = 64;
    private NetworkManager? _network;
    private readonly Dictionary<Guid, AudioStream?> _streams = [];
    private readonly Dictionary<Guid, List<Action<AudioStream?>>> _waiting = [];
    private readonly Dictionary<Guid, AudioStreamPlayer3D> _players = [];
    private readonly Dictionary<Guid, Label3D> _labels = [];

    /// <summary>Tests answer for the download: the bytes of the asset, or null.</summary>
    public Action<Guid, Action<byte[]?>>? FetchOverride { get; set; }

    public int PlayingForTesting => _players.Count;
    public int TextsForTesting => _labels.Count;
    public string TextOfForTesting(Guid objectId) => _labels.TryGetValue(objectId, out var label) ? label.Text : "";
    public AudioStreamPlayer3D? PlayerForTesting(Guid objectId) => _players.GetValueOrDefault(objectId);

    public override void _Ready()
    {
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        if (_network is null) return;
        _network.BuildOperationReceived += OnOperation; _network.ConnectionStateChanged += OnState;
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.BuildOperationReceived -= OnOperation; _network.ConnectionStateChanged -= OnState;
    }

    private void OnState(string state, string detail) { if (state is "switching" or "offline" or "failed") Clear(); }

    public void Clear()
    {
        foreach (var player in _players.Values) if (IsInstanceValid(player)) player.QueueFree();
        foreach (var label in _labels.Values) if (IsInstanceValid(label)) label.QueueFree();
        _players.Clear(); _labels.Clear(); _waiting.Clear(); ClearEffects(); ClearMedia();
    }

    public void OnOperation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            var operation = root.GetProperty("operation").GetString();
            if (operation == "effect") { OnEffect(root); return; }
            if (operation == "media") { OnMedia(root); return; }
            if (operation is not ("sound" or "sound_stop" or "text")) return;
            if (!Guid.TryParse(root.GetProperty("objectId").GetString(), out var objectId)) return;
            switch (operation)
            {
                case "sound_stop": Stop(objectId); break;
                case "text": ShowText(objectId, root.GetProperty("text").GetString() ?? "", root.TryGetProperty("color", out var c) ? c.GetString() ?? "#FFFFFF" : "#FFFFFF", Read(root)); break;
                default:
                    if (!Guid.TryParse(root.GetProperty("assetId").GetString(), out var assetId)) return;
                    var volume = root.TryGetProperty("volume", out var v) && v.TryGetSingle(out var vol) ? Mathf.Clamp(vol, 0, 1) : 1f;
                    Play(objectId, assetId, Read(root), volume, root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.True);
                    break;
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
    }

    private static Vector3 Read(JsonElement root) =>
        new(root.TryGetProperty("x", out var x) ? x.GetSingle() : 0, root.TryGetProperty("y", out var y) ? y.GetSingle() : 0, root.TryGetProperty("z", out var z) ? z.GetSingle() : 0);

    // ---- sounds ------------------------------------------------------------------------------------------------

    public void Play(Guid objectId, Guid assetId, Vector3 position, float volume, bool loop)
    {
        Stop(objectId);
        if (_players.Count >= MaxSounds) return;
        Load(assetId, stream =>
        {
            if (stream is null || !IsInsideTree() || _players.Count >= MaxSounds) return;
            Stop(objectId);
            var player = new AudioStreamPlayer3D
            {
                Stream = loop ? Looping(stream) : stream, VolumeDb = Mathf.LinearToDb(Mathf.Max(volume, .001f)),
                MaxDistance = 60f, UnitSize = 6f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Name = "Sound_" + objectId.ToString("N")
            };
            AddChild(player); player.GlobalPosition = position; _players[objectId] = player;
            player.Finished += () => { var ended = _players.TryGetValue(objectId, out var current) && current == player; if (ended) _players.Remove(objectId); player.QueueFree(); if (ended && !loop) _network?.SendScriptDialogResponse(System.Text.Json.JsonSerializer.Serialize(new { sound_finished = true, objectId })); };
            player.Play();
        });
    }

    public void Stop(Guid objectId)
    {
        if (!_players.Remove(objectId, out var player)) return;
        if (IsInstanceValid(player)) { player.Stop(); player.QueueFree(); }
    }

    private static AudioStream Looping(AudioStream stream)
    {
        var copy = (AudioStream)stream.Duplicate();
        switch (copy)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = true; break;
            case AudioStreamWav wav: wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; wav.LoopBegin = 0; wav.LoopEnd = Mathf.Max(1, WavFrames(wav)); break;
        }
        return copy;
    }

    private static int WavFrames(AudioStreamWav wav)
    {
        var bytesPerFrame = (wav.Format == AudioStreamWav.FormatEnum.Format16Bits ? 2 : 1) * (wav.Stereo ? 2 : 1);
        return wav.Data.Length / bytesPerFrame;
    }

    private void Load(Guid assetId, Action<AudioStream?> done)
    {
        if (_streams.TryGetValue(assetId, out var known)) { done(known); return; }
        if (_waiting.TryGetValue(assetId, out var list)) { list.Add(done); return; }
        _waiting[assetId] = [done];
        void Arrived(byte[]? bytes)
        {
            var stream = bytes is null ? null : Decode(bytes); _streams[assetId] = stream;
            if (_waiting.Remove(assetId, out var callbacks)) foreach (var callback in callbacks) callback(stream);
        }
        if (FetchOverride is not null) { FetchOverride(assetId, Arrived); return; }
        Download(assetId, Arrived);
    }

    private void Download(Guid assetId, Action<byte[]?> done)
    {
        var network = GetNodeOrNull<NetworkManager>("/root/NetworkManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (network is null || session is null || app is null || !session.IsAuthenticated || network.CurrentIslandId == Guid.Empty) { done(null); return; }
        var loader = GetParent()?.GetNodeOrNull<AssetLoadingService>("AssetLoadingService");
        if (loader?.TryReadCached(assetId) is { } onDisk) { done(onDisk); return; }
        var http = new HttpRequest { Timeout = 30, BodySizeLimit = 25 * 1024 * 1024 }; AddChild(http);
        http.RequestCompleted += (_, code, _, body) => { http.QueueFree(); if (code is < 200 or >= 300) { done(null); return; } loader?.StoreIfKnown(assetId, body); done(body); };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{network.CurrentIslandId}/assets/{assetId}/file", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); done(null); }
    }

    /// <summary>OGG Vorbis, PCM / float WAV or MP3; anything else is not a sound this client plays.</summary>
    public static AudioStream? Decode(byte[] bytes)
    {
        if (bytes.Length < 12) return null;
        if (bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S') return AudioStreamOggVorbis.LoadFromBuffer(bytes);
        if (bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3' || bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) return new AudioStreamMP3 { Data = bytes };
        return WavDecoder.TryParse(bytes);
    }

    // ---- floating text -----------------------------------------------------------------------------------------

    public void ShowText(Guid objectId, string text, string color, Vector3 position)
    {
        if (text.Length == 0) { if (_labels.Remove(objectId, out var old) && IsInstanceValid(old)) old.QueueFree(); return; }
        if (!_labels.TryGetValue(objectId, out var label) || !IsInstanceValid(label))
        {
            if (_labels.Count >= MaxTexts) return;
            label = new Label3D { Name = "Text_" + objectId.ToString("N"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = .006f, FontSize = 56, OutlineSize = 14, Shaded = false, DoubleSided = true };
            AddChild(label); _labels[objectId] = label;
        }
        label.Text = text.Length > 120 ? text[..120] : text; label.GlobalPosition = position + new Vector3(0, .4f, 0);
        try { label.Modulate = Color.FromHtml(color); } catch { label.Modulate = Colors.White; }
    }
}

/// <summary>A small reader for PCM WAV files (8 or 16 bit, mono or stereo), enough for the sounds put in objects.</summary>
public static class WavDecoder
{
    public static AudioStreamWav? TryParse(byte[] b)
    {
        if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return null;
        int channels = 0, rate = 0, bits = 0, format = 0; byte[]? data = null;
        var position = 12;
        while (position + 8 <= b.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(b, position, 4); var size = BitConverter.ToInt32(b, position + 4); var body = position + 8;
            if (size < 0 || body + (long)size > b.Length) size = Math.Max(0, b.Length - body);
            if (id == "fmt " && size >= 16) { format = BitConverter.ToInt16(b, body); channels = BitConverter.ToInt16(b, body + 2); rate = BitConverter.ToInt32(b, body + 4); bits = BitConverter.ToInt16(b, body + 14); if (format == unchecked((short)0xFFFE) && size >= 26) format = BitConverter.ToInt16(b, body + 24); /* extensible: the real format is in the sub-format */ }
            else if (id == "data") { data = new byte[size]; Array.Copy(b, body, data, 0, size); }
            position = body + size + (size & 1);
        }
        if (data is null || data.Length == 0 || format is not (1 or 3) || channels is < 1 or > 2 || bits is not (8 or 16 or 24 or 32) || (format == 3 && bits != 32) || rate is < 4000 or > 192000) return null;
        if (bits == 8) { for (var i = 0; i < data.Length; i++) data[i] = (byte)(data[i] - 128); } // WAV 8-bit is unsigned, Godot's is signed
        else if (bits != 16) data = ToPcm16(data, bits, format == 3); // 24 and 32 bit integer and 32 bit float sound is brought down to 16 bits, which is what the player mixes
        return new AudioStreamWav { Data = data, Format = bits == 8 ? AudioStreamWav.FormatEnum.Format8Bits : AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = channels == 2 };
    }

    /// <summary>24 or 32 bit integer samples, or 32 bit float samples, as 16 bit little-endian samples.</summary>
    public static byte[] ToPcm16(byte[] data, int bits, bool isFloat)
    {
        var bytesPerSample = bits / 8; var count = data.Length / bytesPerSample; var result = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var at = i * bytesPerSample; short sample;
            if (isFloat) sample = (short)Math.Clamp(BitConverter.ToSingle(data, at) * short.MaxValue, short.MinValue, short.MaxValue);
            else if (bits == 24) sample = BitConverter.ToInt16(data, at + 1); // the two most significant bytes
            else sample = BitConverter.ToInt16(data, at + 2);
            BitConverter.TryWriteBytes(result.AsSpan(i * 2), sample);
        }
        return result;
    }
}
