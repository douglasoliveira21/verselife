using Godot;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Scripts;

/// One speaker's voice in the world: decoded frames go through a small jitter buffer into a generator stream played
/// from the speaker's avatar, so volume and direction follow where they stand. A "speaking" marker floats above them.
public partial class VoicePlayback : Node3D
{
    private const int PrebufferFrames = 3;
    private const int MaxQueuedFrames = 12;
    private readonly Queue<short[]> _queue = new();
    private AudioStreamPlayer3D _player = null!;
    private AudioStreamGeneratorPlayback? _playback;
    private Label3D _marker = null!;
    private bool _started;
    private double _sinceLastFrame = 10;

    public bool Speaking => _sinceLastFrame < .35;
    public int Queued => _queue.Count;
    public bool Started => _started;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator { MixRate = VoiceSettings.SampleRate, BufferLength = .4f },
            MaxDistance = VoiceSettings.HearingRange, UnitSize = 6f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Bus = "Master"
        };
        AddChild(_player);
        _marker = new Label3D { Text = "((•))", Position = new Vector3(0, 2.35f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = .006f, Modulate = new Color(.55f, 1f, .6f), NoDepthTest = true, Visible = false };
        AddChild(_marker);
    }

    /// Queues a decoded 20 ms frame. The queue is bounded: when the network bursts, the oldest audio is dropped so the delay never grows.
    public void Push(short[] frame)
    {
        _sinceLastFrame = 0;
        _queue.Enqueue(frame);
        while (_queue.Count > MaxQueuedFrames) _queue.Dequeue();
    }

    public override void _Process(double delta)
    {
        _sinceLastFrame += delta;
        _marker.Visible = Speaking;
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

    private void Stop() { _player.Stop(); _playback = null; _started = false; _queue.Clear(); }
}
