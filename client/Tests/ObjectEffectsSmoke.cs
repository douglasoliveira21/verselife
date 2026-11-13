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
            // particles and lights from scripts
            string Fx(string key, bool on, string kind, string preset = "fire", double rate = 30, double lifetime = 1.5, string color = "#FF8000", double radius = 8, bool flicker = false, double x = 1) => System.Text.Json.JsonSerializer.Serialize(new { operation = "effect", key, objectId = door, on, kind, preset, rate, lifetime, size = .5, speed = 2, color, radius, energy = 2, flicker, x, y = 2, z = 3 });
            fx.OnOperation(Fx("s1:1", true, "particles")); await Frames(2);
            Require(fx.EmittersForTesting == 1 && fx.EffectForTesting("s1:1") is CpuParticles3D { Emitting: true, Amount: 45 } fire && fire.GlobalPosition.IsEqualApprox(new Vector3(1, 2, 3)) && Mathf.IsEqualApprox(fire.Lifetime, 1.5f), "an emitter is created where the object is, with its numbers");
            fx.OnOperation(Fx("s1:1", true, "particles", "fire", 60, 6, "#00FF00", x: 9)); Require(fx.EmittersForTesting == 1 && ((CpuParticles3D)fx.EffectForTesting("s1:1")!).Amount == 240 && ((CpuParticles3D)fx.EffectForTesting("s1:1")!).GlobalPosition.X == 9, "the same emitter is updated in place (and follows the object); the amount is capped");
            fx.OnOperation(Fx("s1:2", true, "particles", "explosion")); Require(((CpuParticles3D)fx.EffectForTesting("s1:2")!).OneShot, "an explosion fires once");
            fx.OnOperation(Fx("s1:3", true, "light", color: "#FFC46B", radius: 10, flicker: true)); Require(fx.LightsForTesting == 1 && fx.EffectForTesting("s1:3") is OmniLight3D { OmniRange: 10 } lamp && lamp.LightColor.ToHtml(false) == "ffc46b", "a light with its colour and range");
            fx.OnOperation(Fx("s1:3", true, "light", color: "#FFC46B", radius: 4)); Require(((OmniLight3D)fx.EffectForTesting("s1:3")!).OmniRange == 4 && fx.LightsForTesting == 1, "the light changes in place");
            fx.OnOperation("{\"operation\":\"effect\",\"key\":\"x\"}"); fx.OnOperation("{\"operation\":\"effect\",\"on\":true}"); fx.OnOperation(Fx(new string('k', 200), true, "particles")); fx.OnOperation(Fx("bad", true, "laser")); Require(fx.EmittersForTesting == 2 && fx.LightsForTesting == 1, "bad messages create nothing");
            for (var i = 0; i < 80; i++) fx.OnOperation(Fx($"m:{i}", true, "particles", "smoke")); Require(fx.EmittersForTesting == ObjectEffects.MaxEmitters, "the number of emitters is capped");
            for (var i = 0; i < 30; i++) fx.OnOperation(Fx($"l:{i}", true, "light")); Require(fx.LightsForTesting == ObjectEffects.MaxLights, "and so is the number of lights");
            fx.OnOperation(Fx("s1:1", false, "particles")); await Frames(2); Require(fx.EffectForTesting("s1:1") is null && fx.EmittersForTesting == ObjectEffects.MaxEmitters - 1, "an effect is taken away when the script says so");
            fx.ClearEffects(); await Frames(2); Require(fx.EmittersForTesting == 0 && fx.LightsForTesting == 0, "leaving the island clears every effect");
            GD.Print("event=object_effects_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=object_effects_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
