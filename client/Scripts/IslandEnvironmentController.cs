using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

public partial class IslandEnvironmentController : Node
{
    [Export] public bool DayNightCycle { get; set; } = true;
    [Export] public float TimeScale { get; set; } = .02f;
    [Export] public float Hour { get; set; } = 12f;
    private DirectionalLight3D _sun = null!;
    private WorldEnvironment _environment = null!;

    public override void _Ready()
    {
        _sun = GetParent().GetNodeOrNull<DirectionalLight3D>("Sun")!; _environment = GetParent().GetNodeOrNull<WorldEnvironment>("WorldEnvironment")!;
        if (float.TryParse(OS.GetEnvironment("LIFEVERSE_ISLAND_HOUR"), out var hour)) Hour = Mathf.PosMod(hour, 24);
    }
    public override void _Process(double delta)
    {
        if (!DayNightCycle || _sun is null) return; Hour = Mathf.PosMod(Hour + (float)delta * TimeScale, 24); var angle = (Hour - 6f) / 24f * 360f; _sun.RotationDegrees = new Vector3(angle - 90f, -28f, 0); var night = Mathf.Abs(Hour - 12f) / 12f; if (_environment?.Environment is { } env) env.AmbientLightEnergy = Mathf.Lerp(0.18f, .7f, 1f - night * .65f);
    }
}
