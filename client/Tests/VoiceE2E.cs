using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Tests;

/// Needs a running stack and two players, so it is not part of godot-smoke.sh. Run two copies at the same time, one with
/// LIFEVERSE_E2E_ROLE=speaker and one with listener (each with its own LIFEVERSE_E2E_TOKEN). The speaker sends three
/// seconds of a tone through the real path (Opus, ENet, island server relay); the listener must receive and decode it.
public partial class VoiceE2E : Node3D
{
    public override async void _Ready()
    {
        var role = OS.GetEnvironment("LIFEVERSE_E2E_ROLE");
        try
        {
            var session = GetNode<SessionManager>("/root/SessionManager");
            var network = GetNode<NetworkManager>("/root/NetworkManager");
            session.Set(OS.GetEnvironment("LIFEVERSE_E2E_TOKEN"), "", role);
            var state = "";
            network.ConnectionStateChanged += (s, detail) => { state = s; GD.Print($"[{role}] state={s}"); };
            network.Voice.ExternalInput = true;
            Require(network.JoinIsland(Guid.Parse(OS.GetEnvironment("LIFEVERSE_E2E_ISLAND"))) == Error.Ok, "join failed to start");
            await Until(() => state == "online", 120, "never came online");
            await Until(() => network.RemotePositions.Any(), 120, "never saw the other player");
            GD.Print($"[{role}] sees the other player");

            if (role == "speaker")
            {
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                var rate = (int)AudioServer.GetMixRate(); var chunk = rate / 50; var phase = 0;
                for (var frame = 0; frame < 150; frame++)
                {
                    var samples = new float[chunk];
                    for (var i = 0; i < chunk; i++, phase++) samples[i] = MathF.Sin(2f * MathF.PI * 440f * phase / rate) * .3f;
                    network.Voice.SendSamples(samples);
                    await ToSignal(GetTree().CreateTimer(.02), SceneTreeTimer.SignalName.Timeout);
                }
                GD.Print($"[speaker] sent {network.Voice.SentPackets} packets");
                Require(network.Voice.SentPackets >= 120, $"too few packets sent: {network.Voice.SentPackets}");
                await ToSignal(GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            }
            else
            {
                await Until(() => network.Voice.ReceivedPackets >= 20, 120, $"no voice arrived (received {network.Voice.ReceivedPackets})");
                var speaker = network.Voice.KnownSpeakers;
                Require(speaker == 1, $"one speaker expected, got {speaker}");
                var speaking = false;
                for (var i = 0; i < 20 && !speaking; i++) { speaking = network.RemotePositions.Any() && network.Voice.ReceivedPackets > 0 && network.Voice.IsSpeaking(PeerOfOther(network)); await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout); }
                await ToSignal(GetTree().CreateTimer(3.5), SceneTreeTimer.SignalName.Timeout);
                GD.Print($"[listener] received {network.Voice.ReceivedPackets} packets, speaking-marker seen: {speaking}");
                Require(network.Voice.ReceivedPackets >= 100, $"expected most of the 150 packets, got {network.Voice.ReceivedPackets}");
            }
            GD.Print($"event=voice_e2e role={role} result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=voice_e2e role={role} result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static int PeerOfOther(NetworkManager network) => network.Voice.FirstSpeaker;

    private async Task Until(Func<bool> condition, int seconds, string failure)
    {
        var deadline = Time.GetTicksMsec() + (ulong)seconds * 1000UL;
        while (!condition()) { if (Time.GetTicksMsec() > deadline) throw new InvalidOperationException(failure); await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
