using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Settings;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 3 of the settings menu: sound buses and devices, voice, keys (remapping and conflicts), mouse and movement, and the camera: each setting reaches the game, and the window handles keys and the microphone test.</summary>
public partial class SettingsControlsSmoke : Node
{
    private const string Root = "user://smoke_settings_controls";
    private static void Wipe() { var absolute = ProjectSettings.GlobalizePath(Root); if (!DirAccess.DirExistsAbsolute(absolute)) return; foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static T? Find<T>(Node node, Func<T, bool>? test = null) where T : Node { if (node is T t && (test is null || test(t))) return t; foreach (var child in node.GetChildren()) if (Find(child, test) is { } hit) return hit; return null; }

    public override async void _Ready()
    {
        try
        {
            Wipe();
            // ---- the catalog of this stage
            foreach (var d in SettingsCatalog.All.Where(d => d.Kind == SettingKind.Key)) Require(d.Normalize(d.Default).Equals(d.Default) && KeyBindings.IsValid((string)d.Default), $"{d.Id}: a valid default key");
            Require(KeyBindings.Actions.Count == 20 && SettingsCatalog.All.Count(d => d.Kind == SettingKind.Key) == 40, "20 actions, two keys each");
            Require(SettingsCatalog.Find("controls.jump.1")!.Normalize("Escape").Equals("Space") && SettingsCatalog.Find("controls.jump.1")!.Normalize("Enter").Equals("Space") && SettingsCatalog.Find("controls.jump.1")!.Normalize("Teclafalsa").Equals("Space") && SettingsCatalog.Find("controls.jump.2")!.Normalize("").Equals(""), "reserved or unknown keys are refused; an empty alternative is fine");
            Require(KeyBindings.Parse("controls.move_forward.2") is { Action: "move_forward", Slot: 2 } && KeyBindings.Parse("controls.nao.1") is null && KeyBindings.Parse("graphics.msaa") is null, "key setting ids");
            Require(Mathf.IsEqualApprox(SettingsAppliers.ThresholdFor(100), .01f) && Mathf.IsEqualApprox(SettingsAppliers.ThresholdFor(0), .30f) && SettingsAppliers.ThresholdFor(83) is > .05f and < .07f, "the sensitivity maps to the open-mic threshold (higher is more sensitive)");
            Require(KeyBindings.Pretty("Up") == "Seta ↑" && KeyBindings.Pretty("") == "—" && KeyBindings.Pretty("W") == "W" && KeyBindings.NameOf(Key.Space) == "Space" && KeyBindings.KeyOf("Shift") == Key.Shift && KeyBindings.KeyOf("Escape") == Key.None, "key names");
            Require(SettingsCatalog.All.Where(d => d.Category is SettingCategory.Audio or SettingCategory.Voice or SettingCategory.Controls or SettingCategory.Camera).All(d => d.Description.Length > 15), "every setting has a help text");
            Require(SettingsCatalog.Find("audio.interface") is { IsAvailable: false } && SettingsCatalog.Find("voice.noise_suppression") is { IsAvailable: false } && SettingsCatalog.Find("voice.echo_cancel") is { IsAvailable: false }, "what the game cannot do is unavailable");

            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(20);
            var player = world.GetNode<PlayerController>("Player");
            var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets); SettingsAppliers.Register(store);

            // ---- sound
            foreach (var bus in AudioBuses.All) Require(AudioServer.GetBusIndex(bus) >= 0, $"the bus {bus} exists");
            Require((Find<AudioStreamPlayer>(world, p => p.Name == "Ambient")?.Bus ?? "") == AudioBuses.Ambient, "the world's ambient sound plays on its bus");
            store.Set("audio.effects", .5); store.Set("audio.music", 0); store.Set("audio.ambient", .25); store.Set("audio.media", .75); store.Set("voice.users_volume", .4); store.Apply();
            Require(Mathf.IsEqualApprox(AudioBuses.VolumeOf(AudioBuses.Effects), .5f, .02f) && AudioBuses.VolumeOf(AudioBuses.Music) == 0f && AudioServer.IsBusMute(AudioServer.GetBusIndex(AudioBuses.Music)) && Mathf.IsEqualApprox(AudioBuses.VolumeOf(AudioBuses.Voice), .4f, .02f), "each bus takes its volume; zero mutes it");
            Require(AudioBuses.OutputDevices()[0].Id == "auto" && AudioBuses.InputDevices()[0].Id == "auto", "the device lists start with the system default");
            AudioBuses.UseOutput("um dispositivo que não existe"); Require(AudioServer.OutputDevice == "Default", "a missing device falls back to the default");

            // ---- voice
            store.Set("voice.sensitivity", 100); store.Set("voice.hearing_distance", 10); store.Set("voice.spatial", false); store.Set("voice.indicators", false); store.Apply();
            Require(Mathf.IsEqualApprox(VoiceChat.OpenMicThreshold, .01f) && Mathf.IsEqualApprox(VoicePlayback.Distance, 10f) && !VoicePlayback.Spatial && !NamePlate.ShowVoiceIndicator, "voice settings reach the voice system");
            store.Set("voice.open_mic", true); store.Set("voice.enabled", false); store.Apply();
            var voice = GetNode<NetworkManager>("/root/NetworkManager").Voice; Require(voice.OpenMic && !voice.Enabled, "the voice switches are applied");
            voice.Enabled = true; voice.OpenMic = false;

            // ---- keys
            store.ApplyAll();
            Require(InputMap.HasAction("voice_talk") && KeyBindings.KeysOf("voice_talk").Contains("X"), "the talk key exists in the input map");
            Require(KeyBindings.KeysOf("move_forward").SequenceEqual(["W", "Up"]) && KeyBindings.KeysOf("jump").SequenceEqual(["Space"]), "by default the keys are the ones of the game");
            store.Set("controls.move_forward.1", "I"); store.Set("controls.jump.2", "J"); store.Apply();
            Require(KeyBindings.KeysOf("move_forward").SequenceEqual(["I", "Up"]) && KeyBindings.KeysOf("jump").SequenceEqual(["Space", "J"]), "a changed key replaces the old one in the input map");
            store.Set("controls.jump.1", ""); store.Apply(); Require(KeyBindings.KeysOf("jump").SequenceEqual(["J"]), "an empty slot leaves the action with the other key");
            store.ResetCategory(SettingCategory.Controls); store.Apply();
            Require(KeyBindings.KeysOf("move_forward").SequenceEqual(["W", "Up"]) && KeyBindings.KeysOf("jump").SequenceEqual(["Space"]), "restoring the category puts the keys back");

            // ---- mouse and movement
            store.Set("controls.mouse_sensitivity", 2); store.Set("controls.invert_y", true); store.Set("controls.always_run", true); store.Set("controls.double_tap_run", false); store.Set("controls.hold_to_fly", false); store.Apply();
            Require(Mathf.IsEqualApprox(PlayerController.SensitivityScale, 2f) && PlayerController.InvertYExtra && player.AlwaysRun && !PlayerController.DoubleTapRunEnabled && !PlayerController.HoldToFlyEnabled, "mouse and movement settings reach the player");

            // ---- camera
            store.Set("camera.fov", 95); store.Set("camera.default_distance", 8); store.Set("camera.height", .5); store.Set("camera.shoulder", .6); store.Set("camera.smoothing", 8); store.Set("camera.collision", false); store.Set("camera.zoom_step", 1); store.Apply(); await Frames(2);
            Require(Mathf.IsEqualApprox(player.FieldOfView, 95f) && Mathf.IsEqualApprox(player.CameraDistance, 8f) && Mathf.IsEqualApprox(player.PivotOffset, .5f) && Mathf.IsEqualApprox(player.ShoulderOffset, .6f) && Mathf.IsEqualApprox(player.CameraFollowRate, 8f) && !player.CameraCollides && Mathf.IsEqualApprox(PlayerController.ZoomStep, 1f), "the camera settings reach the player");
            store.Set("camera.default_mode", "first"); store.Apply(); await Frames(2); Require(player.CameraModeName == "FirstPerson", $"the camera can start in first person ({player.CameraModeName})");
            store.Set("camera.default_mode", "third"); store.Apply(); await Frames(2); Require(player.CameraModeName == "ThirdPerson", "or in third person");
            store.ResetAll(); store.Apply(); await Frames(2);
            Require(Mathf.IsEqualApprox(player.FieldOfView, 75f) && Mathf.IsEqualApprox(player.CameraDistance, 5.2f) && player.CameraCollides && !player.AlwaysRun, "defaults bring everything back");

            // ---- the window: keys, conflicts, microphone test
            var holder = new SubViewport { Size = new Vector2I(2400, 1350), Disable3D = true, TransparentBg = true }; AddChild(holder);
            var windowStore = new SettingsStore(Root + "_window", SettingsCatalog.All, SettingsCatalog.Presets);
            var window = new SettingsWindow(windowStore); holder.AddChild(window); window.Open(SettingCategory.Controls); await Frames(6);
            Require(window.Tabs.Count == 2 && window.Tabs.TabAt(0).Text == "Teclas" && window.Tabs.TabAt(1).Text == "Mouse e movimento", "Controles has its two tabs");
            Require(window.RowOf("controls.jump.1") is not null && window.RowOf("controls.jump.1") == window.RowOf("controls.jump.2") && window.RowOf("controls.voice_talk.1") is not null, "each action is one row with two keys");
            var buttons = Find<KeyCaptureButton>(window.RowOf("controls.jump.1")!)!; Require(buttons.Text == "Espaço", "the key is shown in Portuguese");
            Require(window.KeyConflicts().Values.All(users => users.Count == 1), "no conflicts by default");
            var jump = SettingsCatalog.Find("controls.jump.1")!; var interact = SettingsCatalog.Find("controls.interact.1")!;
            window.AssignKey(jump, "E"); await Frames(2);
            Require(window.Dialog.IsOpen && window.Dialog.MessageForTesting.Contains("Interagir"), "a key already in use asks to swap");
            window.Dialog.ButtonsForTesting[1].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require((string)windowStore.Effective("controls.jump.1") == "Space" && !windowStore.HasPending, "Cancelar changes nothing");
            window.AssignKey(jump, "E"); await Frames(2); window.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require((string)windowStore.Effective("controls.jump.1") == "E" && (string)windowStore.Effective("controls.interact.1") == "Space" && window.KeyConflicts().Values.All(u => u.Count == 1), "Trocar swaps the two keys");
            window.DiscardButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(1);
            window.AssignKey(jump, "Escape"); await Frames(2); Require(window.Dialog.IsOpen && window.Dialog.MessageForTesting.Contains("próprio jogo") && !windowStore.HasPending, "a reserved key is refused");
            window.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed);
            windowStore.Set("controls.jump.2", "E"); await Frames(1);
            Require(window.KeyConflicts()["E"].Count == 2, "the same key on two actions is reported as a conflict");
            window.DiscardButton.EmitSignal(BaseButton.SignalName.Pressed);
            // capturing a key from the keyboard
            var capture = Find<KeyCaptureButton>(window.RowOf("controls.fly_toggle.1")!)!; string? got = null; capture.Captured += k => got = k;
            capture.Begin(); Require(capture.Capturing && KeyCaptureButton.AnyCapturing && capture.Text.Contains("Pressione"), "the button waits for a key");
            window._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); Require(window.Visible, "Esc while waiting for a key does not close the window");
            capture._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }); Require(!capture.Capturing && got is null && !KeyCaptureButton.AnyCapturing, "Esc cancels the capture");
            capture.Begin(); capture._Input(new InputEventKey { PhysicalKeycode = Key.J, Pressed = true }); Require(got == "J" && !capture.Capturing, "the pressed key is reported by its name");
            await Frames(2);

            window.SelectCategory(SettingCategory.Voice, "Microfone"); await Frames(4);
            Require(window.RowOf("voice.input_device") is not null && window.RowOf("voice.sensitivity") is not null && window.MicMeter is not null && window.MicButton is not null, "Voz › Microfone has the devices, the sensitivity and the test");
            Require(Find<CheckButton>(window.RowOf("voice.noise_suppression")!) is { Disabled: true } && Find<CheckButton>(window.RowOf("voice.echo_cancel")!) is { Disabled: true }, "noise reduction and echo cancellation are shown unavailable");
            Require(Find<OptionButton>(window.RowOf("voice.input_device")!)!.GetItemText(0) == "Padrão do sistema", "the microphone list starts with the system default");
            window.ToggleMicTest(); await Frames(2);
            if (window.Microphone.Active)
            {
                window.Microphone.Feed(.2f, .4f); window._Process(.016);
                Require(window.MicMeter!.Value > window.MicMeter.Threshold && window.MicButton!.Text == "Parar teste", "the meter follows the microphone and passes the line when it is loud");
                window.SelectCategory(SettingCategory.Voice, "Audição"); await Frames(2);
                Require(!window.Microphone.Active, "leaving the tab turns the microphone off");
            }
            else Require(window.Microphone.Problem.Length > 0, "if the microphone cannot be opened, the window says why");
            window.SelectCategory(SettingCategory.Voice, "Audição"); await Frames(2);
            foreach (var id in new[] { "voice.users_volume", "voice.hearing_distance", "voice.spatial", "voice.indicators" }) Require(window.RowOf(id) is not null, $"Audição has {id}");
            window.SelectCategory(SettingCategory.Audio, null); await Frames(2);
            foreach (var id in new[] { "audio.master", "audio.ambient", "audio.effects", "audio.music", "audio.media", "audio.interface", "audio.output_device", "audio.mute_background" }) Require(window.RowOf(id) is not null, $"Áudio has {id}");
            Require(Find<HSlider>(window.RowOf("audio.interface")!) is { Editable: false }, "the interface volume is shown unavailable");
            window.SelectCategory(SettingCategory.Camera, null); await Frames(2);
            Require(window.Tabs.Count == 2 && window.RowOf("camera.fov") is not null && window.RowOf("camera.collision") is not null, "Câmera has its settings");
            window.SelectCategory(SettingCategory.Camera, "Foto"); await Frames(2);
            Require(window.RowOf("camera.photo_format") is not null && window.RowOf("camera.photo_include_ui") is not null, "and the photo options");

            Wipe(); var extra = ProjectSettings.GlobalizePath(Root + "_window"); if (DirAccess.DirExistsAbsolute(extra)) foreach (var file in DirAccess.GetFilesAt(extra)) DirAccess.RemoveAbsolute($"{extra}/{file}");
            GD.Print("event=settings_controls_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=settings_controls_smoke result=failed error={exception.Message} {exception.StackTrace}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
