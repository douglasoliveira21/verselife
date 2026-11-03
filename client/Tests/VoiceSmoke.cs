using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Tests;

/// Voice on the client without a microphone or a network: synthetic samples are framed, encoded and "sent" to a sink,
/// the same packets are received as another player's voice, and the playback node reacts (marker, buffering, silence).
public partial class VoiceSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var avatar = new Node3D { Name = "Speaker" }; AddChild(avatar);
            var chat = new VoiceChat(); AddChild(chat);
            var sent = new List<(long Sequence, byte[] Data)>();
            chat.Sink = (sequence, data) => sent.Add((sequence, data));
            chat.SpeakerNodeProvider = peer => peer == 7 ? avatar : null;
            await Frames(2);

            var rate = (int)AudioServer.GetMixRate();
            var tone = Enumerable.Range(0, rate).Select(i => MathF.Sin(2f * MathF.PI * 440f * i / rate) * .3f).ToArray();
            chat.SendSamples(tone);
            Require(sent.Count is >= 48 and <= 50, $"one second of speech should be about 50 packets: {sent.Count}");
            Require(sent.All(p => p.Data.Length is > 0 and <= VoiceSettings.MaxPacketBytes), "every packet must be a sane size");
            Require(sent.Select(p => p.Sequence).SequenceEqual(Enumerable.Range(1, sent.Count).Select(i => (long)i)), "sequence numbers count up by one");
            Require(chat.Talking, "sending audio means talking");

            var streamStart = sent.Count; var step = rate / 50;
            for (var i = 0; i + step <= tone.Length; i += step) chat.SendSamples(tone.AsSpan(i, step));
            Require(sent.Count - streamStart >= 47, $"streaming 20 ms pieces should give about 50 packets a second: {sent.Count - streamStart} (mix rate {rate})");
            var before = sent.Count;
            chat.SendSamples(new float[rate], openMicGate: true);
            Require(sent.Count - before <= 11, $"an open microphone must not send silence (only the short hangover): {sent.Count - before}");

            Require(chat.KnownSpeakers == 0 && !chat.IsSpeaking(7), "nobody is speaking yet");
            foreach (var (sequence, data) in sent.Take(20)) chat.Receive(7, sequence, data);
            Require(chat.KnownSpeakers == 1 && avatar.GetNodeOrNull<VoicePlayback>("Voice") is not null, "a playback should hang on the speaker's avatar");
            var playback = avatar.GetNode<VoicePlayback>("Voice");
            await Frames(3);
            Require(chat.IsSpeaking(7) && playback.Speaking, "the speaker should be marked as speaking");
            var queued = playback.Queued;
            chat.Receive(7, sent[5].Sequence, sent[5].Data);
            Require(playback.Queued <= queued, "a repeated or late packet must be ignored");

            Require(chat.KnownSpeakers == 1, "still one speaker");
            chat.Receive(8, 1, sent[0].Data);
            Require(chat.KnownSpeakers == 2 && avatar.GetChildren().OfType<VoicePlayback>().Count() == 1, "an unknown avatar means no playback, only bookkeeping");
            chat.Receive(7, 9999, [1, 2, 3]);
            chat.Receive(7, 10000, new byte[VoiceSettings.MaxPacketBytes + 5]);

            await ToSignal(GetTree().CreateTimer(.9), SceneTreeTimer.SignalName.Timeout);
            Require(!chat.IsSpeaking(7) && !playback.Started, "after a pause the marker disappears and playback stops");

            chat.Enabled = false;
            chat.Receive(7, 20000, sent[0].Data);
            chat.Forget(7);
            await Frames(2);
            Require(avatar.GetNodeOrNull<VoicePlayback>("Voice") is null && !chat.IsSpeaking(7), "forgetting a speaker removes the playback");
            chat.Reset();
            Require(chat.KnownSpeakers == 0 && !chat.Talking, "reset clears everything");
            GD.Print("event=voice_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=voice_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
