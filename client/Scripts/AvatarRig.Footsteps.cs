using Godot;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The sound of walking: while an avatar walks, runs or walks backwards on the ground, a recording of sneakers on concrete plays at its feet, in 3D (it fades with distance, so other players' steps are heard when they are near).
/// It is one long loop, not tied to each step of the animation: a running avatar plays it faster, one walking backwards a little slower. It fades out quickly when the avatar stops.
/// </summary>
public partial class AvatarRig
{
    public const string FootstepsPath = "res://Assets/audio/footsteps_concrete.mp3";
    public const float FootstepsLoudDb = -9f, FootstepsSilentDb = -45f, FootstepsFadePerSecond = 120f;

    private static AudioStream? _footstepStream;
    private AudioStreamPlayer3D? _steps;
    private bool _stepsWanted;
    /// <summary>Tests: turn the footstep sound off or replace the sound.</summary>
    public static bool FootstepsEnabled { get; set; } = true;
    /// <summary>Whether the recording of steps is what the avatars play (then the synthesized ground steps of the ambient sound stay quiet, so nobody hears both).</summary>
    public static bool RecordedFootstepsAvailable => FootstepsEnabled && (_footstepStream is not null || ResourceLoader.Exists(FootstepsPath));
    public static AudioStream? FootstepStreamForTesting { get => _footstepStream; set => _footstepStream = value; }

    public bool FootstepsPlayingForTesting => _steps is { Playing: true } && _stepsWanted;
    public float FootstepsPitchForTesting => _steps?.PitchScale ?? 0f;

    private static AudioStream? FootstepStream()
    {
        if (_footstepStream is not null) return _footstepStream;
        if (!ResourceLoader.Exists(FootstepsPath)) return null;
        var stream = GD.Load<AudioStream>(FootstepsPath); if (stream is AudioStreamMP3 mp3) { mp3 = (AudioStreamMP3)mp3.Duplicate(); mp3.Loop = true; stream = mp3; }
        return _footstepStream = stream;
    }

    /// <summary>Called when the state changes: walking, running and walking backwards (on the ground, close enough to be shown) make the sound; everything else stops it.</summary>
    private void UpdateFootsteps()
    {
        var wanted = FootstepsEnabled && _state is AvatarMotionState.Walk or AvatarMotionState.Run or AvatarMotionState.WalkBack && _detail != AvatarDetail.Hidden;
        if (wanted == _stepsWanted && (_steps is null || !wanted)) { _stepsWanted = wanted; if (wanted) SetStepPitch(); return; }
        _stepsWanted = wanted;
        if (!wanted) return;                                                // it fades out in _Process
        if (_steps is null)
        {
            var stream = FootstepStream(); if (stream is null) { _stepsWanted = false; return; }
            _steps = new AudioStreamPlayer3D { Name = "Footsteps", Stream = stream, VolumeDb = FootstepsSilentDb, MaxDistance = 28f, UnitSize = 4f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
            AddChild(_steps);
        }
        SetStepPitch();
        if (!_steps.Playing) _steps.Play((float)(GD.Randf() * Math.Max(1.0, (_steps.Stream?.GetLength() ?? 1.0) - 1.0)));      // each avatar starts at another point, so a crowd does not step as one
    }

    private void SetStepPitch() { if (_steps is not null) _steps.PitchScale = _state == AvatarMotionState.Run ? 1.45f : _state == AvatarMotionState.WalkBack ? .85f : 1f; }

    /// <summary>Fades the sound in and out (every frame).</summary>
    private void FadeFootsteps(double delta)
    {
        if (_steps is null || !IsInstanceValid(_steps)) return;
        var target = _stepsWanted ? FootstepsLoudDb : FootstepsSilentDb;
        _steps.VolumeDb = Mathf.MoveToward(_steps.VolumeDb, target, (float)delta * FootstepsFadePerSecond);
        if (!_stepsWanted && _steps.Playing && _steps.VolumeDb <= FootstepsSilentDb + .5f) _steps.Stop();
    }
}
