using Godot;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Scripts;

/// One speaker's voice in the world: decoded frames go through a small jitter buffer into a generator stream played
/// from the speaker's avatar, so volume and direction follow where they stand. It also feeds the dot and rings above
/// their name with how loud they are (red when it is too loud). Hearing someone at all turns their dot on.
public partial class VoicePlayback : Node3D
{
    private const int PrebufferFrames = 3;
    private const int MaxQueuedFrames = 12;
    private readonly Queue<short[]> _queue = new();
    private AudioStreamPlayer3D _player = null!;
    private AudioStreamGeneratorPlayback? _playback;
    private bool _started;
    private double _sinceLastFrame = 10;
    private float _level;
    private float _peak;
    private float _pendingLevel;
    private float _pendingPeak;

    public bool Speaking => _sinceLastFrame < .35;
    public int Queued => _queue.Count;
    public bool Started => _started;
    public float Level => _level;
    public float Peak => _peak;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator { MixRate = VoiceSettings.SampleRate, BufferLength = .4f },
            MaxDistance = VoiceSettings.HearingRange, UnitSize = 6f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Bus = "Master"
        };
        AddChild(_player);
    }

    /// Queues a decoded 20 ms frame. The queue is bounded: when the network bursts, the oldest audio is dropped so the delay never grows.
    public void Push(short[] frame)
    {
        _sinceLastFrame = 0;
        _pendingLevel = Mathf.Max(_pendingLevel, VoiceLevel.Of(frame)); _pendingPeak = Mathf.Max(_pendingPeak, VoiceMeter.Peak(frame));
        _queue.Enqueue(frame);
        while (_queue.Count > MaxQueuedFrames) _queue.Dequeue();
    }

    public override void _Process(double delta)
    {
        _sinceLastFrame += delta;
        _level = _pendingLevel; _peak = _pendingPeak; _pendingLevel = _pendingPeak = 0;
        FeedIndicator();
        if (_sinceLastFrame > .5) { if (_started) Stop(); return; }
        if (!_started)
        {
            if (_queue.Count < PrebufferFrames) return;
            _player.Play(); _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback; _started = _playback is not null;
        }
        if (_playback is null) return;
        while (_queue.Count > 0 && _playback.GetFramesAvailable() >= VoiceSettings.FrameSamples)
        {
            var frame = _queue.Dequeue(); var samples = new Vector2[frame.Length];
            for (var i = 0; i < frame.Length; i++) { var value = frame[i] / (float)short.MaxValue; samples[i] = new Vector2(value, value); }
            _playback.PushBuffer(samples);
        }
    }

    /// The avatar this voice plays from carries the dot; having heard it at all means that person has voice on.
    private void FeedIndicator()
    {
        if (GetParent() is not { } parent) return;
        foreach (var child in parent.GetChildren())
            if (child is AvatarRig rig) { rig.SetVoice(true, _level, _peak); return; }
    }

    private void Stop() { _player.Stop(); _playback = null; _started = false; _queue.Clear(); }
}
