using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>What the settings of Áudio, Voz, Controles e Câmera do in the game.</summary>
public static partial class SettingsAppliers
{
    private static PlayerController? Player => Tree?.GetFirstNodeInGroup("lifeverse_player") as PlayerController;
    private static VoiceChat? Voice => Tree?.Root.GetNodeOrNull<NetworkManager>("NetworkManager")?.Voice;

    /// <summary>The loudness an open microphone needs to start sending, from the sensitivity the person sees (higher sensitivity, lower threshold).</summary>
    public static float ThresholdFor(int sensitivity) => Mathf.Lerp(.30f, .01f, Mathf.Clamp(sensitivity, 0, 100) / 100f);

    private static void RegisterStage3(SettingsStore store)
    {
        // sound
        AudioBuses.Ensure();
        store.RegisterApplier("audio.ambient", v => AudioBuses.SetVolume(AudioBuses.Ambient, (float)v));
        store.RegisterApplier("audio.effects", v => AudioBuses.SetVolume(AudioBuses.Effects, (float)v));
        store.RegisterApplier("audio.music", v => AudioBuses.SetVolume(AudioBuses.Music, (float)v));
        store.RegisterApplier("audio.media", v => AudioBuses.SetVolume(AudioBuses.Media, (float)v));
        store.RegisterApplier("audio.output_device", v => AudioBuses.UseOutput((string)v));

        // voice
        store.RegisterApplier("voice.enabled", v => { if (Voice is { } voice) voice.Enabled = (bool)v; });
        store.RegisterApplier("voice.open_mic", v => { if (Voice is { } voice) voice.OpenMic = (bool)v; });
        store.RegisterApplier("voice.input_device", v => AudioBuses.UseInput((string)v));
        store.RegisterApplier("voice.sensitivity", v => VoiceChat.OpenMicThreshold = ThresholdFor((int)v));
        store.RegisterApplier("voice.echo_cancel", v => VoiceChat.EchoGuard = (bool)v);
        store.RegisterApplier("voice.users_volume", v => AudioBuses.SetVolume(AudioBuses.Voice, (float)v));
        store.RegisterApplier("voice.hearing_distance", v => VoicePlayback.Distance = Mathf.Clamp((int)v, 5, 25));
        store.RegisterApplier("voice.spatial", v => VoicePlayback.Spatial = (bool)v);
        store.RegisterApplier("voice.indicators", v => NamePlate.ShowVoiceIndicator = (bool)v);

        // keys
        foreach (var action in KeyBindings.Actions)
            foreach (var slot in new[] { 1, 2 })
            {
                var name = action.Action;
                store.RegisterApplier(KeyBindings.IdOf(name, slot), _ => KeyBindings.Apply(name, (string)store.Value(KeyBindings.IdOf(name, 1)), (string)store.Value(KeyBindings.IdOf(name, 2))));
            }

        // mouse and movement
        store.RegisterApplier("controls.mouse_sensitivity", v => PlayerController.SensitivityScale = (float)v);
        store.RegisterApplier("controls.invert_y", v => PlayerController.InvertYExtra = (bool)v);
        store.RegisterApplier("controls.double_tap_run", v => PlayerController.DoubleTapRunEnabled = (bool)v);
        store.RegisterApplier("controls.hold_to_fly", v => PlayerController.HoldToFlyEnabled = (bool)v);
        store.RegisterApplier("controls.always_run", v => { if (Player is { } player) player.AlwaysRun = (bool)v; });

        // camera
        foreach (var id in new[] { "camera.fov", "camera.default_distance", "camera.height", "camera.shoulder", "camera.smoothing", "camera.collision" }) store.RegisterApplier(id, _ => ApplyCamera(store));
        foreach (var id in LifeVerse.Client.Scripts.Camera.FirstPersonSettings.Ids) store.RegisterApplier(id, _ => LifeVerse.Client.Scripts.Camera.FirstPersonSettings.Current = FirstPersonOptionsFrom(store));
        store.RegisterApplier("camera.wheel_first_person", v => PlayerController.WheelEntersFirstPerson = (bool)v);
        store.RegisterApplier("camera.fp_fov", v => Player?.SetFirstPersonFov((int)v));
        store.RegisterApplier("camera.zoom_step", v => PlayerController.ZoomStep = (float)v);
        store.RegisterApplier("camera.default_mode", v => Player?.SetCameraMode((string)v == "first" ? "FirstPerson" : "ThirdPerson"));
    }

    private static LifeVerse.Shared.Camera.FirstPersonOptions FirstPersonOptionsFrom(SettingsStore store) => new()
    {
        HeadBob = store.Get<bool>("camera.fp_bob"), BobStrength = store.Get<float>("camera.fp_bob_strength"), Sway = store.Get<bool>("camera.fp_sway"), SwayStrength = store.Get<float>("camera.fp_sway_strength"),
        Stabilization = store.Get<float>("camera.fp_stabilization"), LookSmoothing = store.Get<float>("camera.fp_look_smoothing"), Collision = store.Get<bool>("camera.fp_collision")
    };

    public static void ApplyCamera(SettingsStore store)
    {
        Player?.ApplyCameraSettings(store.Get<int>("camera.fov"), store.Get<float>("camera.default_distance"), store.Get<float>("camera.height"), store.Get<float>("camera.shoulder"), store.Get<float>("camera.smoothing"), store.Get<bool>("camera.collision"));
    }
}
