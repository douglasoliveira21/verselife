using System;
using System.Collections.Generic;
using LifeVerse.Shared.Voice;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>
    /// Proximity voice, sending: hold the talk key (or lock the microphone open) and what the microphone hears is cut into 20 ms frames of 16 kHz mono, squeezed with Opus and sent to the island, which passes it to the people
    /// within 25 m. An open microphone sends only while something is being said (a gate with a short hangover, set by the sensitivity); a held key sends everything. Nothing is recorded or kept, and the microphone is only opened
    /// the first time somebody actually talks.
    /// </summary>
    public sealed class VoiceChat : MonoBehaviour
    {
        public const int HangoverFrames = 12;
        private WorldClient _client; private readonly VoiceEncoder _encoder = new VoiceEncoder(); private VoiceFramer _framer; private AudioClip _mic; private string _device; private int _readAt; private uint _sequence; private int _hangover;

        /// <summary>The talk key (or button) is held.</summary>
        public bool TalkHeld { get; set; }
        /// <summary>The padlock: the microphone stays open without holding the key.</summary>
        public bool Locked { get; set; }
        /// <summary>How loud the voice must be for an open microphone to start sending, 0 to 1: lower is more sensitive.</summary>
        public float Gate { get; set; } = .12f;
        /// <summary>While other people are heard, an open microphone does not send (what it hears is probably the speakers).</summary>
        public bool EchoGuard { get; set; }
        public Func<bool> HearingOthers { get; set; }
        public bool Muted { get; set; }
        public int Sent { get; private set; }
        public int Dropped { get; private set; }
        public float Level { get; private set; }
        public float Peak { get; private set; }
        public bool Clipping => VoiceMeter.IsClipping(Peak);
        public bool Talking { get; private set; }
        public bool MicrophoneOpen => _mic != null;
        public string Problem { get; private set; } = "";
        /// <summary>Tests feed samples themselves through <see cref="SendSamples"/>; the microphone is then ignored.</summary>
        public bool ManualInput { get; set; }

        public void Attach(WorldClient client) { _client = client; }

        /// <summary>"Ativar voz" off: nothing is sent and the microphone stays closed.</summary>
        public bool Off { get; set; }
        private bool Wanted => !Off && !Muted && (TalkHeld || Locked);

        private void Update()
        {
            Level = Mathf.MoveTowards(Level, 0f, Time.unscaledDeltaTime * 2.5f); Peak = Mathf.MoveTowards(Peak, 0f, Time.unscaledDeltaTime * 2f);
            if (ManualInput) return;
            if (!Wanted) { Release(); Talking = false; return; }
            if (!EnsureMicrophone()) return;
            var position = Microphone.GetPosition(_device); if (position < 0 || position == _readAt) return;
            var length = _mic.samples; var count = position >= _readAt ? position - _readAt : length - _readAt + position; if (count <= 0) return;
            var data = new float[count * _mic.channels]; _mic.GetData(data, _readAt); _readAt = position; if (_mic.channels > 1) data = Mono(data, _mic.channels);
            SendSamples(data);
        }

        private static float[] Mono(float[] data, int channels) { var mono = new float[data.Length / channels]; for (var i = 0; i < mono.Length; i++) { var sum = 0f; for (var c = 0; c < channels; c++) sum += data[i * channels + c]; mono[i] = sum / channels; } return mono; }

        private string _openedPreferred = MicDevices.Auto;

        private bool EnsureMicrophone()
        {
            if (_mic != null && _openedPreferred != MicDevices.Preferred) Release();   // another microphone was chosen: open that one
            if (_mic != null) return true;
            if (MicDevices.Names().Length == 0) { Problem = "Nenhum microfone foi encontrado."; return false; }
            _device = MicDevices.Pick(); _openedPreferred = MicDevices.Preferred; Microphone.GetDeviceCaps(_device, out var low, out var high); var rate = high == 0 ? 48000 : Mathf.Clamp(48000, low, high); if (rate < 8000) rate = 16000;
            _mic = Microphone.Start(_device, true, 1, rate); if (_mic == null) { Problem = "Não foi possível abrir o microfone."; return false; }
            _framer = new VoiceFramer(_mic.frequency); _readAt = 0; Problem = ""; return true;
        }

        private void Release() { if (_mic == null) return; Microphone.End(_device); _mic = null; _framer?.Reset(); }
        private void OnDestroy() => Release();

        /// <summary>Frames, optionally drops silence (open microphone), encodes and sends. <paramref name="sourceRate"/> is the rate of the samples (the microphone's, or 16000 in tests).</summary>
        public void SendSamples(float[] samples, int sourceRate = 0)
        {
            if (sourceRate > 0 && (_framer == null || _framer.SourceRate != sourceRate)) _framer = new VoiceFramer(sourceRate); if (_framer == null) _framer = new VoiceFramer(VoiceSettings.SampleRate);
            foreach (var frame in _framer.Push(samples)) SendFrame(frame);
        }

        private void SendFrame(short[] frame)
        {
            var level = VoiceLevel.Of(frame); var peak = VoiceMeter.Peak(frame); Level = Mathf.Max(Level, Mathf.Clamp01(level)); Peak = Mathf.Max(Peak, peak);
            if (!Wanted) return;   // muted, or neither the key nor the padlock: nothing leaves
            if (!TalkHeld)
            {
                if (EchoGuard && HearingOthers != null && HearingOthers()) { _hangover = 0; Talking = false; return; }
                if (level >= Gate) _hangover = HangoverFrames; else if (_hangover > 0) _hangover--; else { Talking = false; Dropped++; return; }   // silence stays off the network
            }
            var packet = _encoder.Encode(frame); if (packet.Length == 0) return;
            Talking = true; if (_client != null && _client.SendVoice(++_sequence, packet)) Sent++;
        }
    }

    /// <summary>Proximity voice, listening: one voice per speaker. Packets are decoded to 16 kHz frames, a small buffer (60 ms) rides out the jitter of the network, a lost packet is filled with the codec's best guess, and the voice comes out of the speaker's position in 3D so it fades with distance.</summary>
    public sealed class VoiceListener : MonoBehaviour
    {
        private sealed class Speaker
        {
            public VoiceDecoder Decoder = new VoiceDecoder(); public GameObject Host; public BusSource Bus; public VoiceOut Out; public uint LastSequence; public float LastHeard; public bool Started;
        }

        private WorldClient _client; private readonly Dictionary<int, Speaker> _speakers = new Dictionary<int, Speaker>();
        /// <summary>Where a speaker is (game space), so the voice comes from them. Set by the game.</summary>
        public Func<int, Vector3?> PositionOf { get; set; }
        public int Heard { get; private set; }
        public int Corrupt { get; private set; }
        public int Concealed { get; private set; }
        public int Speakers => _speakers.Count;
        public const float SpeakingSeconds = .35f; private AudioListener _ear;

        public void Attach(WorldClient client) { if (_client != null) _client.VoiceReceived -= OnVoice; _client = client; if (client != null) client.VoiceReceived += OnVoice; }
        private void OnDestroy() { if (_client != null) _client.VoiceReceived -= OnVoice; }

        public bool IsSpeaking(int peer) => _speakers.TryGetValue(peer, out var s) && Time.realtimeSinceStartup - s.LastHeard < SpeakingSeconds;
        public bool AnySpeaking() { foreach (var s in _speakers.Values) if (Time.realtimeSinceStartup - s.LastHeard < SpeakingSeconds) return true; return false; }
        public VoiceOut OutOf(int peer) => _speakers.TryGetValue(peer, out var s) ? s.Out : null;
        public AudioSource SourceOf(int peer) => _speakers.TryGetValue(peer, out var s) ? s.Bus?.Source : null;

        public void Forget(int peer) { if (_speakers.TryGetValue(peer, out var s)) { if (s.Host != null) Destroy(s.Host); _speakers.Remove(peer); } }

        /// <summary>"Ativar voz" off: nobody is heard.</summary>
        public bool Off { get; set; }
        /// <summary>"Distância de audição" (metres, at most the 25 m the island sends).</summary>
        public float HearingDistance { get; set; } = VoiceSettings.HearingRange;
        /// <summary>"Voz espacial": the voice comes from where the speaker is; off, everybody is heard the same in both ears.</summary>
        public bool Spatial { get; set; } = true;

        private void OnVoice(ReceiveVoiceMessage message) { if (!Off) Hear(message.SpeakerPeer, message.Sequence, message.Data); }

        /// <summary>One packet from one speaker (public so tests feed packets).</summary>
        public void Hear(int peer, uint sequence, byte[] data)
        {
            if (!_speakers.TryGetValue(peer, out var speaker))
            {
                if (_speakers.Count >= 32) return; var host = new GameObject("Voice_" + peer); host.transform.SetParent(transform, false);
                speaker = new Speaker { Host = host, Out = host.AddComponent<VoiceOut>() }; speaker.Bus = BusSource.Make(host, Bus.Voice, 1f, true, VoiceSettings.HearingRange, true); speaker.Out.Begin(speaker.Bus.Source); _speakers[peer] = speaker;
            }
            if (sequence <= speaker.LastSequence && speaker.LastSequence - sequence < 1000) return;   // late or repeated
            var frame = speaker.Decoder.Decode(data); if (frame == null) { Corrupt++; return; }
            if (speaker.LastSequence != 0 && sequence == speaker.LastSequence + 2) { speaker.Out.Push(speaker.Decoder.Conceal()); Concealed++; }
            speaker.LastSequence = sequence; speaker.LastHeard = Time.realtimeSinceStartup; speaker.Out.Push(frame); Heard++;
        }

        private void Update()
        {
            if (_ear == null || !_ear.isActiveAndEnabled) _ear = FindAnyObjectByType<AudioListener>(); var listener = _ear != null ? _ear.transform : null;
            foreach (var pair in _speakers)
            {
                var at = PositionOf?.Invoke(pair.Key); if (at.HasValue) pair.Value.Host.transform.position = GameSpace.ToUnity(at.Value);
                var source = pair.Value.Bus != null ? pair.Value.Bus.Source : null; if (source == null) continue;
                var range = Mathf.Clamp(HearingDistance, 1f, VoiceSettings.HearingRange); source.spatialBlend = Spatial ? 1f : 0f; source.maxDistance = range; source.minDistance = Mathf.Min(source.minDistance, range);
                source.mute = Off || (listener != null && Vector3.Distance(listener.position, pair.Value.Host.transform.position) > range);
            }
        }
    }

    /// <summary>The playing end of one voice: a ring of samples at 16 kHz that the audio thread reads at the rate of the sound card.</summary>
    public sealed class VoiceOut : MonoBehaviour
    {
        private readonly Queue<float> _queue = new Queue<float>(); private readonly object _gate = new object(); private int _rate = 48000; private bool _playing; private double _position; private float _previous;
        public const int StartFrames = 3, MaxFrames = 25;
        public int Buffered { get { lock (_gate) return _queue.Count; } }
        public int Delivered { get; private set; }
        public int Underruns { get; private set; }

        public void Begin(AudioSource source)
        {
            _rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000; var clip = AudioClip.Create("voice", _rate, 1, _rate, false); var ones = new float[_rate]; for (var i = 0; i < ones.Length; i++) ones[i] = 1f; clip.SetData(ones, 0); source.clip = clip; source.loop = true; source.Play();
        }

        public void Push(short[] frame)
        {
            lock (_gate)
            {
                if (_queue.Count > MaxFrames * VoiceSettings.FrameSamples) { _queue.Clear(); _playing = false; }   // far behind: drop to catch up
                foreach (var s in frame) _queue.Enqueue(s / (float)short.MaxValue);
                if (!_playing && _queue.Count >= StartFrames * VoiceSettings.FrameSamples) _playing = true;
            }
        }

        /// <summary>Makes <paramref name="frames"/> mono output samples at the sound card's rate, the way the audio thread does (tests read them).</summary>
        public float[] Render(int frames) { var mono = new float[frames]; Fill(mono); return mono; }

        private void Fill(float[] mono)
        {
            var step = VoiceSettings.SampleRate / (double)_rate;
            lock (_gate)
            {
                for (var i = 0; i < mono.Length; i++)
                {
                    if (!_playing || _queue.Count < 2) { if (_playing) { _playing = false; Underruns++; } mono[i] = 0f; continue; }
                    mono[i] = _queue.Peek();   // 16 kHz is speech: holding the sample is enough for the card's rate
                    _position += step; while (_position >= 1.0 && _queue.Count > 0) { _previous = _queue.Dequeue(); _position -= 1.0; }
                }
                Delivered += mono.Length;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return; var frames = data.Length / channels; var mono = new float[frames]; Fill(mono);
            for (var i = 0; i < frames; i++) for (var c = 0; c < channels; c++) data[i * channels + c] = mono[i];
        }
    }
}
