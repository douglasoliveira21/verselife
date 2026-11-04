using System.Net;
using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// What the parcel under the avatar does to the surroundings: its own sky and weather (faded in and out over a few seconds) and its music.
/// The music is a file the owner points to; this computer downloads it, so the address is checked again here (HTTPS, no machine inside a
/// network, mp3 or ogg, a size limit, no redirects) and the person can turn all parcel music off. There is no video.
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
    /// What the player was told about the music, for the info window.
    public string Status { get; private set; } = string.Empty;
    public event Action? Changed;

    public void ClearCacheForTesting() => _cache.Clear();
    public Guid CurrentParcel => _current;
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

    private Vector3? Position()
    {
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
        _environment ??= _land.GetParent()?.GetNodeOrNull<IslandEnvironmentController>("EnvironmentController");
        _environment?.SetParcelLook(parcel is { EnvironmentAllowed: true } p ? p.Environment : null);

        if (parcel is null || string.IsNullOrEmpty(parcel.MusicUrl) || !_enabled) { if (!sameParcel || parcel is null || string.IsNullOrEmpty(parcel.MusicUrl)) StopMusic(); return; }
        if (!parcel.MusicAutoplay && !sameParcel) { StopMusic(); Status = "A música deste terreno não toca sozinha."; Changed?.Invoke(); return; }
        if (sameParcel && _audio.Playing && _loadedUrl == parcel.MusicUrl) { _audio.VolumeDb = Volume(parcel.MusicVolume); return; }
        PlayMusic(parcel.MusicUrl, parcel.MusicVolume);
    }

    private static float Volume(float linear) => linear <= .001f ? -80f : Mathf.LinearToDb(linear);

    /// Plays the music of the current parcel on request (also when it does not start by itself).
    public void PlayCurrent()
    {
        if (_land.Parcels.FirstOrDefault(p => p.Id == _current) is { MusicUrl.Length: > 0 } parcel && _enabled) PlayMusic(parcel.MusicUrl, parcel.MusicVolume);
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
