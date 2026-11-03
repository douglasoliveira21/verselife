using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Tests;

/// The Second Life style voice controls without a microphone: hold-to-talk button, padlock, the dot and rings over a
/// name (green when clean, red when too loud), and the other player's rings following the voice they receive.
/// Set LIFEVERSE_CAPTURE_PATH on a windowed run to save a picture of the indicator states.
public partial class VoiceUiSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var voice = new VoiceChat(); AddChild(voice);
            voice.Sink = (_, _) => { };
            var bar = new VoiceBar(); AddChild(bar); bar.Bind(voice);
            await Frames(2);

            // The talk button works while held and lets go on release.
            Require(!voice.PushToTalk && !voice.OpenMic, "nothing is on at the start");
            bar.PressTalk(true);
            Require(voice.PushToTalk, "holding the button must open the microphone");
            bar.PressTalk(false);
            Require(!voice.PushToTalk, "letting go must close it");
            bar.PressTalk(true);
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!voice.PushToTalk, "a button held while the mouse is not down must not stay on (lost focus)");

            // The padlock keeps the microphone open without holding anything, and follows changes made elsewhere.
            bar.ClickLock();
            Require(voice.OpenMic && bar.LockButton.ButtonPressed, "the padlock should lock the microphone open");
            voice.OpenMic = false;
            Require(!bar.LockButton.ButtonPressed, "turning it off elsewhere must unlock the padlock");
            bar.ClickLock(); bar.ClickLock();
            Require(!voice.OpenMic, "clicking twice must end up unlocked");

            // With voice switched off nothing can be turned on.
            voice.Enabled = false;
            Require(bar.TalkButton.Disabled && bar.LockButton.Disabled, "buttons are greyed out when voice is off");
            bar.PressTalk(true);
            Require(!voice.PushToTalk, "the button must do nothing while voice is off");
            voice.Enabled = true;
            Require(!bar.TalkButton.Disabled, "buttons come back with voice");

            // Your own meter: a clean voice is green, a shout is red, and both settle when you stop.
            var rate = (int)AudioServer.GetMixRate(); voice.ExternalInput = true;
            voice.SendSamples(Enumerable.Range(0, rate / 5).Select(i => MathF.Sin(2f * MathF.PI * 300f * i / rate) * .25f).ToArray());
            Require(voice.LocalLevel > .1f && !VoiceMeter.IsClipping(voice.LocalPeak), $"a clean voice is not clipping: level {voice.LocalLevel:F2} peak {voice.LocalPeak:F2}");
            voice.ExternalInput = false;
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            Require(voice.LocalLevel < .05f && voice.LocalPeak < .05f, "the meter settles after you stop");
            voice.ExternalInput = true;
            voice.SendSamples(Enumerable.Range(0, rate / 5).Select(i => MathF.Sin(2f * MathF.PI * 300f * i / rate) * 1.4f).ToArray());
            Require(VoiceMeter.IsClipping(voice.LocalPeak), "a shout must read as too loud");

            // The dot and rings over a name.
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); AddChild(rig);
            await Frames(3);
            var indicator = rig.Voice ?? throw new InvalidOperationException("the avatar has no voice indicator");
            var name = rig.FindChild("DisplayName", true, false) as Node3D ?? throw new InvalidOperationException("no name tag");
            Require(indicator.Position.Y > name.Position.Y + .2f, $"the dot belongs above the name: dot {indicator.Position.Y:F2}, name {name.Position.Y:F2}");
            Require(!indicator.Active, "nobody heard from yet means no dot");
            rig.SetVoice(true, 0f, 0f);
            Require(indicator.Active && indicator.Speaking < .01f, "voice on and nobody speaking: a quiet dot without rings");
            rig.SetVoice(false, 0f, 0f);
            Require(!rig.Voice!.Active, "voice off hides the dot");
            for (var i = 0; i < 20; i++) { rig.SetVoice(true, .45f, .5f); indicator.Advance(.05f); }
            Require(indicator.Speaking > .9f && !indicator.Red, $"clean speech shows green rings: speaking {indicator.Speaking:F2} red {indicator.Red}");
            for (var i = 0; i < 10; i++) { rig.SetVoice(true, .9f, 1f); indicator.Advance(.05f); }
            Require(indicator.Red, "a peak at the top of the range turns the rings red");
            for (var i = 0; i < 60; i++) indicator.Advance(.05f);
            Require(indicator.Speaking < .05f && indicator.Clip < .05f, "the rings fade when the speaking stops");

            // The other player's rings follow the voice that reaches you.
            var other = new Node3D { Name = "Other" }; AddChild(other);
            var otherRig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); other.AddChild(otherRig);
            voice.SpeakerNodeProvider = peer => peer == 3 ? other : null; voice.Reset();
            await Frames(3);
            var encoder = new VoiceEncoder(); var framer = new VoiceFramer(16000);
            var tone = Enumerable.Range(0, 16000).Select(i => MathF.Sin(2f * MathF.PI * 440f * i / 16000f) * .3f).ToArray();
            var sequence = 0L;
            foreach (var frame in framer.Push(tone).Take(25)) { voice.Receive(3, ++sequence, encoder.Encode(frame)); await Frames(1); }
            Require(otherRig.Voice is { Active: true } && otherRig.Voice.Level > .03f, $"hearing someone turns their dot on and moves the rings: {otherRig.Voice?.Level:F2}");

            if (DisplayServer.GetName() != "headless" && !string.IsNullOrWhiteSpace(OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"))) await Capture();
            GD.Print("event=voice_ui_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=voice_ui_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    /// Three avatars side by side with their names: voice on but quiet, speaking cleanly, shouting. Saved as a picture for a human to look at.
    private async Task Capture()
    {
        foreach (var child in GetChildren().OfType<Node3D>().ToArray()) child.Visible = false;
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.35f, .45f, .6f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .8f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 30, 0) });
        var camera = new Camera3D { Position = new Vector3(0, 1.5f, 4.6f), Current = true }; AddChild(camera);
        var states = new (float X, float Level, float Peak)[] { (-1.6f, 0f, 0f), (0f, .5f, .5f), (1.6f, .9f, 1f) };
        var rigs = new List<(AvatarRig Rig, float Level, float Peak)>();
        foreach (var (x, level, peak) in states)
        {
            var holder = new Node3D { Position = new Vector3(x, 0, 0), Rotation = new Vector3(0, Mathf.Pi, 0) }; AddChild(holder);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); holder.AddChild(rig); rigs.Add((rig, level, peak));
        }
        for (var frame = 0; frame < 60; frame++) { foreach (var (rig, level, peak) in rigs) rig.SetVoice(true, level, peak); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-voice-indicator.png");
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
