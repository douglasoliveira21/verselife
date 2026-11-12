using System.Net;
using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// What the parcel under the avatar does to the surroundings: its own sky and weather (faded in and out over a few seconds) and its music.
/// The music is a file the owner points to; this computer downloads it, so the address is checked again here (HTTPS, no machine inside a
/// network, mp3 or ogg, a size limit, no redirects) and the person can turn all parcel music off. There is no video.
/// <summary>The music of the whole island: the address, the volume (0 to 1), whether it starts by itself and its name.</summary>
public sealed record IslandMusic(string Url, float Volume, bool Autoplay, string Name);

public partial class LandPresence : Node
{
    public const int MaxMusicBytes = 12 * 1024 * 1024;
    private const string SettingsPath = "user://land_media.cfg";

    private LandManager _land = null!;
    private PlayerController? _player;
    private IslandEnvironmentController? _environment;
    private AudioStreamPlayer _audio = null!;
    private Guid _current;
    private string _loadedUrl = string.Empty;
    private double _since;
    private int _request;
    private bool _enabled = true;
    private readonly Dictionary<string, byte[]> _cache = [];

    /// Tests: answers a download without a network (null: failed).
    public Func<string, byte[]?>? FetcherForTesting { get; set; }
    /// <summary>The look of the whole island, where no parcel sets its own (null: none set, the game keeps its own sky).</summary>
    public Func<ParcelEnvironment?>? BaseLook { get; set; }
    /// <summary>The music of the whole island, where no parcel plays its own (null: none).</summary>
    public Func<IslandMusic?>? BaseMedia { get; set; }

    /// <summary>Plays an address for the person who is testing it in the management window; <see cref="EndPreview"/> goes back to what the place plays.</summary>
    public void Preview(string url, float volume) => PlayMusic(url, volume);
    public void EndPreview() { StopMusic(); Evaluate(true); }
    /// What the player was told about the music, for the info window.
    public string Status { get; private set; } = string.Empty;
    public event Action? Changed;

    public void ClearCacheForTesting() => _cache.Clear();
    public Guid CurrentParcel => _current;
    /// <summary>False while the avatar stands on a parcel where voice is switched off.</summary>
    public bool VoiceAllowed { get; private set; } = true;
    /// <summary>The name of the parcel that has voice off, for the notice (empty when voice works here).</summary>
    public string VoiceOffIn { get; private set; } = string.Empty;
    public string PlayingUrl => _audio?.Playing == true ? _loadedUrl : string.Empty;
    public bool Playing => _audio?.Playing == true;

    /// Whether parcel music may play at all (a setting of this computer).
    public bool MusicEnabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; SaveSettings(); if (!value) StopMusic(); else Evaluate(force: true); Changed?.Invoke(); }
    }

    public override void _Ready()
    {
        _land = GetParent<LandManager>();
        _audio = new AudioStreamPlayer { Name = "ParcelMusic", Bus = "Master" }; AddChild(_audio);
        LoadSettings();
        _land.Changed += OnLandChanged;
    }

    public override void _ExitTree() { if (_land is not null) _land.Changed -= OnLandChanged; }

    private void LoadSettings()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) == Error.Ok) _enabled = (bool)config.GetValue("music", "enabled", true);
    }

    private void SaveSettings()
    {
        try { var config = new ConfigFile(); config.SetValue("music", "enabled", _enabled); config.Save(SettingsPath); }
        catch (Exception) { /* a setting that does not persist is not worth an error */ }
    }

    private void OnLandChanged() => Evaluate(force: true);

    public override void _Process(double delta)
    {
        _since += delta;
        if (_since < .25) return;
        _since = 0;
        Evaluate(force: false);
    }

    /// <summary>Tests: where the avatar stands, when there is no player in the scene.</summary>
    public Func<Vector3?>? PositionForTesting { get; set; }

    private Vector3? Position()
    {
        if (PositionForTesting is not null) return PositionForTesting();
        _player ??= _land.GetParent()?.GetNodeOrNull<PlayerController>("Player");
        return _player?.GlobalPosition;
    }

    /// Looks at where the avatar stands and applies the parcel there. force: do it again even if it is the same parcel (its settings may have changed).
    public void Evaluate(bool force)
    {
        if (Position() is not { } at) return;
        Apply(_land.ParcelAt(at.X, at.Z), force);
    }

    /// Applies the settings of a parcel (null: open ground, the island's own look and no music).
    public void Apply(LandParcelData? parcel, bool force = false)
    {
        var id = parcel?.Id ?? Guid.Empty;
        if (id == _current && !force) return;
        var sameParcel = id == _current; _current = id;
        VoiceAllowed = parcel?.AllowVoice ?? true; VoiceOffIn = VoiceAllowed || parcel is null ? string.Empty : parcel.Name;
        _environment ??= _land.GetParent()?.GetNodeOrNull<IslandEnvironmentController>("EnvironmentController");
        // What a person sees: the island's look (set in the management window) with the parcel's own settings on top; open ground gets the island's look alone.
        var islandLook = BaseLook?.Invoke();
        var own = parcel is { EnvironmentAllowed: true } p && !p.Environment.IsEmpty ? p.Environment : null;
        _environment?.SetParcelLook(own is not null ? own.Over(islandLook ?? new ParcelEnvironment()) : islandLook);

        // The music a person hears: the parcel's own, otherwise the island's (open ground, or a parcel that plays nothing).
        var (musicUrl, musicVolume, musicAutoplay, whose) = parcel is { MusicUrl.Length: > 0 } own2 ? (own2.MusicUrl, own2.MusicVolume, own2.MusicAutoplay, "deste terreno") : BaseMedia?.Invoke() is { Url.Length: > 0 } island ? (island.Url, island.Volume, island.Autoplay, "da ilha") : (string.Empty, 0f, true, "");
        if (musicUrl.Length == 0 || !_enabled) { if (!sameParcel || musicUrl.Length == 0) StopMusic(); return; }
        if (!musicAutoplay && !sameParcel) { StopMusic(); Status = $"A música {whose} não toca sozinha."; Changed?.Invoke(); return; }
        if (_audio.Playing && _loadedUrl == musicUrl) { _audio.VolumeDb = Volume(musicVolume); return; }
        PlayMusic(musicUrl, musicVolume);
    }

    private static float Volume(float linear) => linear <= .001f ? -80f : Mathf.LinearToDb(linear);

    /// Plays the music of the current parcel on request (also when it does not start by itself).
    public void PlayCurrent()
    {
        if (!_enabled) return;
        if (_land.Parcels.FirstOrDefault(p => p.Id == _current) is { MusicUrl.Length: > 0 } parcel) PlayMusic(parcel.MusicUrl, parcel.MusicVolume);
        else if (BaseMedia?.Invoke() is { Url.Length: > 0 } island) PlayMusic(island.Url, island.Volume);
    }

    public void StopMusic()
    {
        _request++;
        if (_audio is null) return;
        if (_audio.Playing) _audio.Stop();
        _loadedUrl = string.Empty; Status = string.Empty; Changed?.Invoke();
    }

    /// Why an address would not be played, or null when it is fine. Resolves the name too, so an address that points into a network is refused.
    public static string? Refuse(string url)
    {
        if (LandMediaUrl.Check(url) is { } why) return why;
        var host = new Uri(url).Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out _)) return null;
        var resolved = IP.ResolveHostname(host);
        if (string.IsNullOrEmpty(resolved) || !IPAddress.TryParse(resolved, out var address) || LandMediaUrl.Private(address)) return "bad_host";
        return null;
    }

    private void PlayMusic(string url, float volume)
    {
        var ticket = ++_request;
        if (FetcherForTesting is null && Refuse(url) is { } why) { Status = MediaMessage(why); Changed?.Invoke(); return; }
        if (FetcherForTesting is null && LandMediaUrl.Check(url) is { } bad) { Status = MediaMessage(bad); Changed?.Invoke(); return; }
        if (_cache.TryGetValue(url, out var known)) { Start(url, known, volume, ticket); return; }
        Status = "Carregando a música…"; Changed?.Invoke();
        if (FetcherForTesting is not null) { Finish(url, FetcherForTesting(url), volume, ticket); return; }
        var http = new HttpRequest { Timeout = 25, MaxRedirects = 0, BodySizeLimit = MaxMusicBytes, UseThreads = true };
        AddChild(http);
        http.RequestCompleted += (result, code, _, body) => { http.QueueFree(); Finish(url, result == (long)HttpRequest.Result.Success && code == 200 ? body : null, volume, ticket); };
        if (http.Request(url) != Error.Ok) { http.QueueFree(); Finish(url, null, volume, ticket); }
    }

    private void Finish(string url, byte[]? bytes, float volume, int ticket)
    {
        if (ticket != _request) return; // the person moved on while it downloaded
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxMusicBytes) { Status = "Não foi possível carregar a música deste terreno."; Changed?.Invoke(); return; }
        if (_cache.Count >= 3) _cache.Clear();
        _cache[url] = bytes;
        Start(url, bytes, volume, ticket);
    }

    private void Start(string url, byte[] bytes, float volume, int ticket)
    {
        if (ticket != _request) return;
        AudioStream? stream = null;
        try
        {
            if (new Uri(url).AbsolutePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) { var ogg = AudioStreamOggVorbis.LoadFromBuffer(bytes); if (ogg is not null) ogg.Loop = true; stream = ogg; }
            else stream = new AudioStreamMP3 { Data = bytes, Loop = true };
        }
        catch (Exception) { stream = null; }
        if (stream is null) { Status = "O arquivo de música não pôde ser tocado."; Changed?.Invoke(); return; }
        _audio.Stream = stream; _audio.VolumeDb = Volume(volume); _audio.Play(); _loadedUrl = url;
        Status = _audio.Playing ? "Tocando a música do terreno." : "O arquivo de música não pôde ser tocado."; Changed?.Invoke();
    }

    public static string MediaMessage(string code) => code switch
    {
        "not_https" => "O endereço da música precisa começar com https://.",
        "bad_host" => "Esse endereço não é permitido (rede interna ou nome inválido).",
        "bad_type" => "Só arquivos .mp3 ou .ogg podem ser usados.",
        "too_long" => "O endereço é longo demais.",
        _ => "Não foi possível usar esse endereço."
    };
}
