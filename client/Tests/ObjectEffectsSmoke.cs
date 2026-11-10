using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Sounds and floating texts that scripts ask for: decoding, 3D playback, stopping, texts, limits and bad messages.</summary>
public partial class ObjectEffectsSmoke : Node3D
{
    private static byte[] Wav(int samples, int rate = 22050, int bits = 16, int channels = 1)
    {
        var data = new byte[samples * channels * bits / 8]; for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 7 * 10);
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray()); w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data); return stream.ToArray();
    }

    public override async void _Ready()
    {
        try
        {
            var fx = new ObjectEffects { Name = "ObjectEffects" }; AddChild(fx);
            var bell = Guid.NewGuid(); var broken = Guid.NewGuid(); var door = Guid.NewGuid();
            var requested = new List<Guid>();
            fx.FetchOverride = (asset, done) => { requested.Add(asset); done(asset == bell ? Wav(22050 * 3) : asset == broken ? [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13] : null); };

            Require(WavDecoder.TryParse(Wav(100)) is { Stereo: false, MixRate: 22050 }, "a 16-bit mono WAV is read");
            Require(WavDecoder.TryParse(Wav(100, 44100, 16, 2)) is { Stereo: true, MixRate: 44100 }, "a stereo WAV is read");
            Require(WavDecoder.TryParse(Wav(100, 8000, 8)) is { Format: AudioStreamWav.FormatEnum.Format8Bits }, "an 8-bit WAV is read");
            Require(WavDecoder.TryParse([1, 2, 3]) is null && ObjectEffects.Decode(new byte[40]) is null, "bytes that are not a sound are refused");

            fx.OnOperation("not json"); fx.OnOperation("{\"operation\":\"physics\",\"bodies\":[]}"); fx.OnOperation("{\"operation\":\"sound\",\"objectId\":\"x\"}");
            Require(fx.PlayingForTesting == 0 && fx.TextsForTesting == 0, "other operations and broken ones are ignored");

            var sound = "{\"operation\":\"sound\",\"objectId\":\"" + door + "\",\"assetId\":\"" + bell + "\",\"x\":3,\"y\":1,\"z\":-2,\"volume\":0.5,\"loop\":false}";
            fx.OnOperation(sound); await Frames(2);
            var player = fx.PlayerForTesting(door);
            Require(player is not null && player.Playing && player.GlobalPosition.DistanceTo(new Vector3(3, 1, -2)) < .01f, "the sound plays at the object");
            Require(Mathf.Abs(player!.VolumeDb - Mathf.LinearToDb(.5f)) < .01f, "the volume asked for is used");
            fx.OnOperation(sound); await Frames(2);
            Require(requested.Count(a => a == bell) == 1, "a sound is downloaded once");
            Require(fx.PlayingForTesting == 1, "the same object plays one sound at a time");

            fx.OnOperation("{\"operation\":\"sound_stop\",\"objectId\":\"" + door + "\"}"); await Frames(2);
            Require(fx.PlayingForTesting == 0, "stopping ends the sound");

            fx.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + door + "\",\"assetId\":\"" + bell + "\",\"x\":0,\"y\":0,\"z\":0,\"volume\":1,\"loop\":true}"); await Frames(2);
            Require(fx.PlayerForTesting(door)?.Stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward }, "a looping sound loops");
            fx.Clear();

            fx.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + door + "\",\"assetId\":\"" + broken + "\",\"x\":0,\"y\":0,\"z\":0,\"volume\":1,\"loop\":false}");
            fx.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + door + "\",\"assetId\":\"" + Guid.NewGuid() + "\",\"x\":0,\"y\":0,\"z\":0,\"volume\":1,\"loop\":false}"); await Frames(2);
            Require(fx.PlayingForTesting == 0, "a file that cannot be decoded, or is not found, plays nothing");

            for (var i = 0; i < 30; i++) fx.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + bell + "\",\"x\":0,\"y\":0,\"z\":0,\"volume\":1,\"loop\":true}");
            await Frames(2);
            Require(fx.PlayingForTesting == ObjectEffects.MaxSounds, $"no more than {ObjectEffects.MaxSounds} sounds at once (got {fx.PlayingForTesting})");
            fx.Clear();

            fx.OnOperation("{\"operation\":\"text\",\"objectId\":\"" + door + "\",\"text\":\"Toque em mim\",\"color\":\"#12F2E0\",\"x\":1,\"y\":2,\"z\":3}");
            Require(fx.TextsForTesting == 1 && fx.TextOfForTesting(door) == "Toque em mim", "a floating text appears");
            fx.OnOperation("{\"operation\":\"text\",\"objectId\":\"" + door + "\",\"text\":\"Olá!\",\"color\":\"nope\",\"x\":1,\"y\":2,\"z\":3}");
            Require(fx.TextsForTesting == 1 && fx.TextOfForTesting(door) == "Olá!", "a new text replaces the old one and a bad colour does not break it");
            fx.OnOperation("{\"operation\":\"text\",\"objectId\":\"" + door + "\",\"text\":\"\",\"color\":\"#FFFFFF\"}");
            Require(fx.TextsForTesting == 0, "an empty text takes it away");
            GD.Print("event=object_effects_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=object_effects_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
