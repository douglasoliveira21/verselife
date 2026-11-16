using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Scripts.Camera;

/// <summary>What the person chose for the camera, as one value every part of the client reads (the player, the editors). Always within the limits of <see cref="CameraOptions.Clamped"/>.</summary>
public static class CameraSettings
{
    private static CameraOptions _current = new CameraOptions().Clamped();
    public static CameraOptions Current { get => _current; set { _current = value.Clamped(); Changed?.Invoke(); } }
    public static event Action? Changed;
    public static void Reset() => Current = new CameraOptions();
}
