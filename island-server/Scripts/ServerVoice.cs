using LifeVerse.Shared.Voice;

namespace LifeVerse.IslandServer.Scripts;

/// Proximity voice: a speaker's Opus packets go to the players inside hearing range and nowhere else. The server never
/// decodes the audio; it only checks size and rate, finds who is close enough and forwards the bytes.
public partial class ServerMain
{
    private readonly VoiceRelay _voice = new();

    public void SubmitVoice(int peerId, long sequence, byte[] data)
    {
        if (sequence < 0 || !_players.TryGetValue(peerId, out var speaker) || !speaker.Authenticated) return;
        if (!_voice.Allow(peerId, System.Environment.TickCount64, data.Length)) return;
        var positions = _players.Values.Where(x => x.Authenticated).Select(x => new VoicePeer(x.PeerId, x.State.X, x.State.Y, x.State.Z)).ToList();
        foreach (var hearer in VoiceRelay.Hearers(peerId, positions))
            _bridge.RpcId(hearer, NetworkBridge.MethodName.ReceiveVoice, peerId, sequence, data);
    }
}
