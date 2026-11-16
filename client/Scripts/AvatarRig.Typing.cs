using Godot;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The sound of typing: while an avatar types a message in the chat (its state is Typing), a recording of a keyboard plays at its hands, in 3D (it fades with distance, so everybody near hears it and the
/// others do not). It is the same on everybody's screen because the state comes from the island. The recording is a little quieter than it is in its file, so a busy room is not a wall of clicks, and it
/// fades out as soon as the person stops.
/// </summary>
public partial class AvatarRig
{
    public const string TypingSoundPath = "res://Assets/audio/keyboard_typing.mp3";
    /// <summary>A little below the loudness of the recording (0 dB is the file as it is) so the room is not a wall of clicks; and where it fades to when it stops.</summary>
    public const float TypingLoudDb = -8f, TypingSilentDb = -45f, TypingFadePerSecond = 90f;

    private static AudioStream? _typingStream;
    private AudioStreamPlayer3D? _typing;
    private bool _typingWanted;
    /// <summary>Tests: turn the typing sound off or replace the sound.</summary>
    public static bool TypingSoundEnabled { get; set; } = true;
    public static AudioStream? TypingStreamForTesting { get => _typingStream; set => _typingStream = value; }

    public bool TypingSoundPlayingForTesting => _typing is { Playing: true } && _typingWanted;
    public float TypingSoundDbForTesting => _typing?.VolumeDb ?? TypingSilentDb;
    public bool TypingSoundWanted => _typingWanted;

    private static AudioStream? TypingStream()
    {
        if (_typingStream is not null) return _typingStream;
        if (!ResourceLoader.Exists(TypingSoundPath)) return null;
        var stream = GD.Load<AudioStream>(TypingSoundPath); if (stream is AudioStreamMP3 mp3) { mp3 = (AudioStreamMP3)mp3.Duplicate(); mp3.Loop = true; stream = mp3; }
        return _typingStream = stream;
    }

    /// <summary>Called when the state changes: typing (close enough to be shown) makes the sound; everything else stops it.</summary>
    private void UpdateTypingSound()
    {
        var wanted = TypingSoundEnabled && _state == AvatarMotionState.Typing && _detail != AvatarDetail.Hidden;
        if (wanted == _typingWanted && (_typing is null || !wanted)) { _typingWanted = wanted; return; }
        _typingWanted = wanted;
        if (!wanted) return;                                                // it fades out in _Process
        if (_typing is null)
        {
            var stream = TypingStream(); if (stream is null) { _typingWanted = false; return; }
            _typing = new AudioStreamPlayer3D { Name = "TypingSound", Bus = Settings.AudioBuses.Of(Settings.AudioBuses.Effects), Stream = stream, VolumeDb = TypingSilentDb, MaxDistance = 24f, UnitSize = 4f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Position = new Vector3(0, 1.1f, -.25f) };
            AddChild(_typing);
        }
        if (!_typing.Playing) _typing.Play((float)(GD.Randf() * Math.Max(1.0, (_typing.Stream?.GetLength() ?? 1.0) - 1.0)));      // each avatar starts at another point of the recording, so two people do not click as one
    }

    /// <summary>Fades the sound in and out (every frame).</summary>
    private void FadeTypingSound(double delta)
    {
        if (_typing is null || !IsInstanceValid(_typing)) return;
        var target = _typingWanted ? TypingLoudDb : TypingSilentDb;
        _typing.VolumeDb = Mathf.MoveToward(_typing.VolumeDb, target, (float)delta * TypingFadePerSecond);
        if (!_typingWanted && _typing.Playing && _typing.VolumeDb <= TypingSilentDb + .5f) _typing.Stop();
    }
}

public sealed partial class AvatarRig
{
    /// <summary>
    /// The turn of the hips around the vertical, to be taken off every bone: the recording's actor stands facing some other way, and every bone of the clip carries that turn, so the whole body is turned back
    /// together (taking it off the hips alone would twist the spine). The avatar keeps facing where it already was.
    /// </summary>
    internal static Quaternion HeadingOf(Quaternion hips)
    {
        var along = Vector3.Up * new Vector3(hips.X, hips.Y, hips.Z).Dot(Vector3.Up);
        var twist = new Quaternion(along.X, along.Y, along.Z, hips.W);
        return twist.LengthSquared() < 1e-8f ? Quaternion.Identity : twist.Normalized();
    }
}
