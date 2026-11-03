using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Scripts;

/// Proximity voice for the client, driven by the voice bar: hold the talk button to speak, or lock the microphone open
/// with the padlock. The microphone is read through Godot's own capture, squeezed with Opus and sent to the island
/// server, which forwards it to players nearby. Incoming voice is decoded per speaker and played from their avatar.
/// There is no echo cancellation: use headphones.
public partial class VoiceChat : Node
{
    public const string CaptureBus = "VoiceCapture";
    private const float OpenMicThreshold = .06f;
    private const int HangoverFrames = 10;

    [Signal] public delegate void TalkingChangedEventHandler(bool talking);
    [Signal] public delegate void SettingsChangedEventHandler();

    private readonly VoiceEncoder _encoder = new();
    private readonly Dictionary<int, SpeakerState> _speakers = [];
    private VoiceFramer? _framer;
    private AudioEffectCapture? _capture;
    private AudioStreamPlayer? _mic;
    private long _sequence;
    private int _hangover;
    private bool _talking;
    private bool _enabled = true;
    private bool _openMic;
    private bool _pushToTalk;

    /// Master switch: off means you neither speak nor hear.
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; if (!value) { _pushToTalk = false; Reset(); } EmitSignal(SignalName.SettingsChanged); } }
    /// The padlock: the microphone stays open without holding the button.
    public bool OpenMic { get => _openMic; set { if (_openMic == value) return; _openMic = value; EmitSignal(SignalName.SettingsChanged); } }
    /// The talk button is being held.
    public bool PushToTalk { get => _pushToTalk; set { if (_pushToTalk == value) return; _pushToTalk = value; EmitSignal(SignalName.SettingsChanged); } }
    /// Tests feed samples themselves through SendSamples; the microphone is then ignored.
    public bool ExternalInput { get; set; }
    public bool Talking => _talking;
    public int SentPackets { get; private set; }
    public int ReceivedPackets { get; private set; }
    /// Smoothed loudness of what you are sending, 0..1, and the loudest recent sample. Both fall back to zero when you stop.
    public float LocalLevel { get; private set; }
    public float LocalPeak { get; private set; }

    /// Tests plug in here to see packets instead of sending them.
    public Action<long, byte[]>? Sink { get; set; }
    /// Tests can answer "does this speaker have an avatar to play from" without the network.
    public Func<int, Node3D?>? SpeakerNodeProvider { get; set; }

    // ---- speaking -------------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        // The meter always settles when nothing new arrives.
        LocalLevel = Mathf.MoveToward(LocalLevel, 0f, (float)delta * 2.5f);
        LocalPeak = Mathf.MoveToward(LocalPeak, 0f, (float)delta * 2.5f);
        if (ExternalInput) return;
        // A button held while the mouse is released elsewhere (window lost focus) must not leave the microphone on.
        if (_pushToTalk && !Input.IsMouseButtonPressed(MouseButton.Left)) PushToTalk = false;
        var wantsToTalk = Enabled && Online && (OpenMic || PushToTalk);
        if (!wantsToTalk) { _capture?.ClearBuffer(); ReleaseTalking(); return; }
        if (!EnsureMicrophone()) return;
        var available = _capture!.GetFramesAvailable();
        if (available <= 0) return;
        var stereo = _capture.GetBuffer(available);
        var mono = new float[stereo.Length];
        for (var i = 0; i < stereo.Length; i++) mono[i] = (stereo[i].X + stereo[i].Y) * .5f;
        SendSamples(mono, openMicGate: OpenMic && !PushToTalk);
    }

    /// Frames the samples, optionally drops silence (open microphone), encodes and sends. Public so tests can feed audio.
    public void SendSamples(ReadOnlySpan<float> mono, bool openMicGate = false)
    {
        _framer ??= new VoiceFramer((int)AudioServer.GetMixRate());
        var any = false;
        foreach (var frame in _framer.Push(mono))
        {
            var level = VoiceLevel.Of(frame); var peak = VoiceMeter.Peak(frame);
            if (openMicGate)
            {
                if (level >= OpenMicThreshold) _hangover = HangoverFrames;
                else if (_hangover > 0) _hangover--; else continue;
            }
            var packet = _encoder.Encode(frame);
            if (packet.Length == 0) continue;
            LocalLevel = Mathf.Max(level, LocalLevel * .7f); LocalPeak = Mathf.Max(peak, LocalPeak);
            Transmit(++_sequence, packet); SentPackets++; any = true;
        }
        if (any && !_talking) { _talking = true; EmitSignal(SignalName.TalkingChanged, true); }
    }

    private void ReleaseTalking()
    {
        if (!_talking) return;
        _talking = false; _hangover = 0; _framer?.Reset();
        EmitSignal(SignalName.TalkingChanged, false);
    }

    private void Transmit(long sequence, byte[] packet)
    {
        if (Sink is not null) { Sink(sequence, packet); return; }
        GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.SendVoice(sequence, packet);
    }

    private bool Online => Sink is not null || GetNodeOrNull<NetworkManager>("/root/NetworkManager") is { IsAuthenticated: true };

    /// The microphone is only opened the first time someone actually talks.
    private bool EnsureMicrophone()
    {
        if (_capture is not null) return true;
        if (!ProjectSettings.GetSetting("audio/driver/enable_input", false).AsBool()) return false;
        var index = AudioServer.GetBusIndex(CaptureBus);
        if (index < 0) { AudioServer.AddBus(); index = AudioServer.BusCount - 1; AudioServer.SetBusName(index, CaptureBus); AudioServer.SetBusMute(index, true); AudioServer.AddBusEffect(index, new AudioEffectCapture { BufferLength = .5f }); }
        _capture = AudioServer.GetBusEffect(index, 0) as AudioEffectCapture;
        _mic = new AudioStreamPlayer { Stream = new AudioStreamMicrophone(), Bus = CaptureBus };
        AddChild(_mic); _mic.Play();
        return _capture is not null;
    }

    // ---- listening ------------------------------------------------------------------------------------------------

    private sealed class SpeakerState
    {
        public VoiceDecoder Decoder { get; } = new();
        public long LastSequence { get; set; } = -1;
        public VoicePlayback? Playback { get; set; }
    }

    public bool IsSpeaking(int peer) => _speakers.TryGetValue(peer, out var state) && state.Playback is { } playback && IsInstanceValid(playback) && playback.Speaking;
    public int KnownSpeakers => _speakers.Count;
    public int FirstSpeaker => _speakers.Keys.FirstOrDefault();
    public VoicePlayback? PlaybackOf(int peer) => _speakers.TryGetValue(peer, out var state) && state.Playback is { } playback && IsInstanceValid(playback) ? playback : null;

    /// A packet from the server. Late or repeated packets are ignored; a short gap is filled with concealment.
    public void Receive(int speaker, long sequence, byte[] data)
    {
        if (!Enabled) return;
        if (!_speakers.TryGetValue(speaker, out var state)) _speakers[speaker] = state = new SpeakerState();
        if (sequence <= state.LastSequence) return;
        var gap = state.LastSequence < 0 ? 0 : sequence - state.LastSequence - 1;
        state.LastSequence = sequence;
        if (PlaybackFor(speaker, state) is not { } playback) return;
        if (gap is > 0 and <= 3) for (var i = 0; i < gap; i++) playback.Push(state.Decoder.Conceal());
        if (state.Decoder.Decode(data) is { } frame) { playback.Push(frame); ReceivedPackets++; }
    }

    private VoicePlayback? PlaybackFor(int speaker, SpeakerState state)
    {
        if (state.Playback is { } existing && IsInstanceValid(existing) && existing.IsInsideTree()) return existing;
        var node = SpeakerNodeProvider?.Invoke(speaker) ?? GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.RemoteNode(speaker);
        if (node is null || !IsInstanceValid(node)) return null;
        var playback = new VoicePlayback { Name = "Voice" };
        node.AddChild(playback);
        state.Playback = playback;
        return playback;
    }

    public void Forget(int speaker)
    {
        if (_speakers.Remove(speaker, out var state) && state.Playback is { } playback && IsInstanceValid(playback)) playback.QueueFree();
    }

    public void Reset()
    {
        foreach (var speaker in _speakers.Keys.ToArray()) Forget(speaker);
        ReleaseTalking();
    }
}
