using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// The theme of the login screen (<see cref="TrackPath"/>): it fades in, loops while the screen is open and fades out when the player goes on to the world. A speaker button in the window controls mutes it
/// (the choice is remembered). The general volume of the game is applied to the audio bus as soon as the screen opens, so the volume chosen in the settings is respected from the start.
/// </summary>
public partial class LoginMusic : Node
{
    public const string TrackPath = "res://Assets/audio/login_theme.mp3";
    public const float PlayingDb = -9f;
    private const string ConfigPath = "user://login_music.cfg";

    private AudioStreamPlayer _player = null!;
    private Tween? _fade;

    public static bool Muted { get; private set; } = ReadMuted();
    public bool Playing => _player is not null && _player.Playing;
    public bool Looping => _player?.Stream is AudioStreamMP3 { Loop: true };
    public float VolumeDb => _player.VolumeDb;

    private static bool ReadMuted() { var file = new ConfigFile(); return file.Load(ConfigPath) == Error.Ok && (bool)file.GetValue("music", "muted", false); }

    public override void _Ready()
    {
        if (GetNodeOrNull<LifeVerse.Client.Autoloads.ClientSettings>("/root/ClientSettings") is { } settings) ApplyBusVolume(settings.MasterVolume);
        var stream = ResourceLoader.Exists(TrackPath) ? GD.Load<AudioStreamMP3>(TrackPath) : null; if (stream is null) return;
        stream.Loop = true; _player = new AudioStreamPlayer { Stream = stream, VolumeDb = -60f, Bus = "Master" }; AddChild(_player);
        if (!Muted) Start();
    }

    /// <summary>The volume of the settings applies to the whole game, so it is set on the master bus.</summary>
    public static void ApplyBusVolume(float volume) { AudioServer.SetBusVolumeDb(0, volume <= 0f ? -80f : Mathf.LinearToDb(volume)); AudioServer.SetBusMute(0, volume <= 0f); }

    private void Start() { if (_player is null) return; if (!_player.Playing) _player.Play(); _fade?.Kill(); _fade = CreateTween(); _fade.TweenProperty(_player, "volume_db", PlayingDb, 2.5).SetTrans(Tween.TransitionType.Sine); }

    public void SetMuted(bool muted)
    {
        Muted = muted; var file = new ConfigFile(); file.Load(ConfigPath); file.SetValue("music", "muted", muted); file.Save(ConfigPath);
        if (_player is null) return;
        if (!muted) { Start(); return; }
        _fade?.Kill(); _fade = CreateTween(); _fade.TweenProperty(_player, "volume_db", -60f, .6); _fade.TweenCallback(Callable.From(() => _player.Stop()));
    }

    /// <summary>Fades the music out and frees it, even though the login screen is already being replaced: the node moves to the root first.</summary>
    public void FadeOutAndFree(double seconds = 1.2)
    {
        if (_player is null || !_player.Playing) { QueueFree(); return; }
        var root = GetTree().Root; GetParent()?.RemoveChild(this); root.AddChild(this);
        _fade?.Kill(); _fade = CreateTween(); _fade.TweenProperty(_player, "volume_db", -60f, seconds).SetTrans(Tween.TransitionType.Sine); _fade.TweenCallback(Callable.From(QueueFree));
    }
}
