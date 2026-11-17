using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Scripts.Camera;

/// <summary>What the person chose for the first person camera (head bob, lean, steadiness, how soft the turn is), as one value the player reads. Always within the limits of <see cref="FirstPersonOptions.Clamped"/>.</summary>
public static class FirstPersonSettings
{
    private static FirstPersonOptions _current = new FirstPersonOptions().Clamped();
    public static FirstPersonOptions Current { get => _current; set { _current = value.Clamped(); Changed?.Invoke(); } }
    public static event Action? Changed;
    public static void Reset() => Current = new FirstPersonOptions();

    /// <summary>The ids of the settings, in the order of the catalog.</summary>
    public static readonly string[] Ids = ["camera.fp_bob", "camera.fp_bob_strength", "camera.fp_sway", "camera.fp_sway_strength", "camera.fp_stabilization", "camera.fp_look_smoothing", "camera.fp_collision"];
}
