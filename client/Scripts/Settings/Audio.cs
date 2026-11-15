using Godot;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>
/// The sound buses of the game. Every sound is sent to one of them and each has its own volume (Configurações › Áudio), all feeding Master:
/// Ambiente (the sound of the world), Efeitos (footsteps and sounds of objects), Música (login music and music of parcels), Mídia (audio of objects and lands) and Voz (other people's voices).
/// </summary>
public static class AudioBuses
{
    public const string Ambient = "Ambiente", Effects = "Efeitos", Music = "Musica", Media = "Midia", Voice = "Voz";
    public static readonly string[] All = [Ambient, Effects, Music, Media, Voice];

    /// <summary>Creates the buses that do not exist yet (safe to call any number of times).</summary>
    public static void Ensure()
    {
        foreach (var name in All)
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus(); var index = AudioServer.BusCount - 1; AudioServer.SetBusName(index, name); AudioServer.SetBusSend(index, "Master");
        }
    }

    /// <summary>The name of the bus (and the bus is there): what a sound node should use as its <c>Bus</c>.</summary>
    public static string Of(string name) { Ensure(); return name; }

    public static void SetVolume(string name, float linear)
    {
        Ensure(); var index = AudioServer.GetBusIndex(name); if (index < 0) return;
        linear = Mathf.Clamp(linear, 0f, 1f);
        AudioServer.SetBusVolumeDb(index, linear <= 0f ? -80f : Mathf.LinearToDb(linear)); AudioServer.SetBusMute(index, linear <= 0f);
    }

    public static float VolumeOf(string name) { var index = AudioServer.GetBusIndex(name); return index < 0 || AudioServer.IsBusMute(index) ? 0f : Mathf.DbToLinear(AudioServer.GetBusVolumeDb(index)); }

    public static IReadOnlyList<SettingChoice> OutputDevices() => Devices(AudioServer.GetOutputDeviceList());
    public static IReadOnlyList<SettingChoice> InputDevices() => Devices(AudioServer.GetInputDeviceList());

    private static IReadOnlyList<SettingChoice> Devices(string[] names)
    {
        var list = new List<SettingChoice> { new("auto", "Padrão do sistema") };
        foreach (var name in names) if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) && name.Length is > 0 and <= 32) list.Add(new(name, name));
        return list;
    }

    /// <summary>"auto" is the system default. A device that is no longer connected falls back to it.</summary>
    public static void UseOutput(string device) => AudioServer.OutputDevice = device == "auto" || !AudioServer.GetOutputDeviceList().Contains(device) ? "Default" : device;
    public static void UseInput(string device) => AudioServer.InputDevice = device == "auto" || !AudioServer.GetInputDeviceList().Contains(device) ? "Default" : device;
}

/// <summary>
/// "Testar microfone": reads the microphone through a bus of its own, only while the person asked for the test and the page is open, and reports how loud it is.
/// Nothing is recorded or sent anywhere. The microphone stops when the test stops.
/// </summary>
public partial class MicrophoneTest : Node
{
    private const string BusName = "VoiceTest";
    private AudioEffectCapture? _capture;
    private AudioStreamPlayer? _player;

    public bool Active { get; private set; }
    /// <summary>Smoothed loudness 0..1 (how loud what you say is, on the scale the open-microphone gate uses) and the loudest recent sample.</summary>
    public float Level { get; private set; }
    public float Peak { get; private set; }
    public string Problem { get; private set; } = "";

    public bool Start()
    {
        Problem = "";
        if (Active) return true;
        if (!ProjectSettings.GetSetting("audio/driver/enable_input", false).AsBool()) { Problem = "A entrada de áudio está desligada neste cliente."; return false; }
        var index = AudioServer.GetBusIndex(BusName);
        if (index < 0)
        {
            AudioServer.AddBus(); index = AudioServer.BusCount - 1; AudioServer.SetBusName(index, BusName); AudioServer.SetBusMute(index, true);      // muted: you never hear yourself, the capture still gets the sound
            AudioServer.AddBusEffect(index, new AudioEffectCapture { BufferLength = .2f });
        }
        _capture = AudioServer.GetBusEffect(index, 0) as AudioEffectCapture;
        if (_capture is null) { Problem = "Não foi possível abrir o microfone."; return false; }
        _player = new AudioStreamPlayer { Stream = new AudioStreamMicrophone(), Bus = BusName }; AddChild(_player); _player.Play();
        _capture.ClearBuffer(); Active = true; return true;
    }

    public void Stop()
    {
        if (!Active) return;
        _player?.Stop(); _player?.QueueFree(); _player = null; _capture?.ClearBuffer(); Active = false; Level = Peak = 0f;
    }

    public override void _Process(double delta)
    {
        Level = Mathf.MoveToward(Level, 0f, (float)delta * 2.5f); Peak = Mathf.MoveToward(Peak, 0f, (float)delta * 2f);
        if (!Active || _capture is null) return;
        var available = _capture.GetFramesAvailable(); if (available <= 0) return;
        var buffer = _capture.GetBuffer(available); double sum = 0; var peak = 0f;
        foreach (var frame in buffer) { var sample = (frame.X + frame.Y) * .5f; sum += sample * sample; peak = Mathf.Max(peak, Mathf.Abs(sample)); }
        Feed((float)Math.Sqrt(sum / Math.Max(1, buffer.Length)), peak);
    }

    /// <summary>One reading (root-mean-square and peak of a batch of samples). Tests feed readings directly.</summary>
    public void Feed(float rms, float peak) { Level = Mathf.Max(Level, Mathf.Clamp(rms, 0f, 1f)); Peak = Mathf.Max(Peak, Mathf.Clamp(peak, 0f, 1f)); }

    public override void _ExitTree() => Stop();
}
