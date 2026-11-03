using Godot;
using LifeVerse.Shared.Audio;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// Plays the synthesized soundscape: surf near the shore, wind, rain and footsteps. No audio files are needed.
public partial class AmbientAudio : Node
{
    private const int MixRate = 22050;
    private readonly AmbientSynth _synth = new(MixRate, 1);
    private AudioStreamPlayer _output = null!;
    private AudioStreamGeneratorPlayback? _playback;
    private PlayerController? _avatar;
    private IslandTerrain? _terrain;
    private IslandEnvironmentController? _environment;
    private float _ocean, _wind, _rain, _stride, _splashTimer;

    public override void _Ready()
    {
        var world = GetParent();
        _avatar = world.GetNodeOrNull<PlayerController>("Player");
        _terrain = world.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        _environment = world.GetNodeOrNull<IslandEnvironmentController>("EnvironmentController");
        _output = new AudioStreamPlayer { Name = "Ambient", Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = .3f } };
        AddChild(_output);
        _output.Play();
        _playback = _output.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        UpdateLevels(dt);
        DetectSteps(dt);
        if (_playback is null) return;
        var frames = Math.Min(_playback.GetFramesAvailable(), 4096);
        if (frames <= 0) return;
        var mono = new float[frames];
        _synth.Fill(mono, new AmbientMix(_ocean, _wind, _rain));
        var stereo = new Vector2[frames];
        for (var i = 0; i < frames; i++) stereo[i] = new Vector2(mono[i], mono[i]);
        _playback.PushBuffer(stereo);
    }

    private void UpdateLevels(float dt)
    {
        var weather = _environment?.Weather ?? "clear";
        var oceanTarget = 0f;
        if (_avatar is not null && _terrain is not null)
        {
            var edge = _terrain.Map.HalfExtent - Mathf.Max(Mathf.Abs(_avatar.GlobalPosition.X), Mathf.Abs(_avatar.GlobalPosition.Z));
            oceanTarget = 1f - Mathf.SmoothStep(0f, 140f, edge);
            if (_avatar.IsSwimming) oceanTarget = 1f;
        }
        var windTarget = .16f + (_avatar is null ? 0f : .2f * Mathf.Clamp(_avatar.GlobalPosition.Y / 30f, 0f, 1f)) + weather switch { "cloudy" => .1f, "rain" => .25f, "storm" => .7f, "fog" => .05f, _ => 0f };
        var rainTarget = weather switch { "rain" => .75f, "storm" => 1f, _ => 0f };
        var blend = 1f - Mathf.Exp(-2f * dt);
        _ocean = Mathf.Lerp(_ocean, oceanTarget, blend); _wind = Mathf.Lerp(_wind, windTarget, blend); _rain = Mathf.Lerp(_rain, rainTarget, blend);
    }

    private void DetectSteps(float dt)
    {
        if (_avatar is null) return;
        var speed = new Vector2(_avatar.Velocity.X, _avatar.Velocity.Z).Length();
        if (_avatar.IsSwimming)
        {
            _splashTimer -= dt;
            if (speed > .5f && _splashTimer <= 0) { _synth.TriggerStep(.55f, soft: true); _splashTimer = 1.05f; }
            return;
        }
        if (!_avatar.IsOnFloor() || speed < .6f) { _stride = 0; return; }
        _stride += speed * dt;
        if (_stride < 1.7f) return;
        _stride = 0;
        var sand = _terrain is not null && OnSand(_terrain.Map, _avatar.GlobalPosition);
        _synth.TriggerStep(.45f + .55f * Mathf.Clamp(speed / 6.4f, 0f, 1f), soft: sand);
    }

    private static bool OnSand(TerrainMap map, Vector3 position)
    {
        var x = Mathf.RoundToInt((position.X + map.HalfExtent) / map.CellSize); var z = Mathf.RoundToInt((position.Z + map.HalfExtent) / map.CellSize);
        return map.SplatAt(x, z, 2) > 128;
    }
}
