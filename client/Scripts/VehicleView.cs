using Godot;

namespace LifeVerse.Client.Scripts;

/// What a car looks like in the world: a simple body with four wheels and a name tag. The server decides where
/// it is; this node only follows the latest snapshot smoothly and spins the wheels with the speed.
public partial class VehicleView : Node3D
{
    private const float WheelRadius = .36f;
    private readonly List<MeshInstance3D> _wheels = [];
    public int VehicleId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public int DriverPeer { get; set; }
    public float Speed { get; set; }
    public Vector3 TargetPosition { get; set; }
    public float TargetYaw { get; set; }
    public float TargetPitch { get; set; }

    /// Where the driver sits, in world space (the node's own orientation, a little above and behind the centre).
    public Transform3D SeatTransform => new(GlobalBasis, GlobalTransform * new Vector3(-.35f, .35f, .25f));

    public void Setup(int id, string name, string type)
    {
        VehicleId = id; DisplayName = name; Name = $"Vehicle_{id}";
        var hue = Math.Abs((name + type).GetHashCode()) % 360 / 360f;
        var paint = new StandardMaterial3D { AlbedoColor = Color.FromHsv(hue, .65f, .8f), Roughness = .45f, Metallic = .35f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(.08f, .08f, .1f), Roughness = .9f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(.35f, .55f, .7f, .65f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = .1f };
        AddChild(new MeshInstance3D { Name = "Body", Mesh = new BoxMesh { Size = new Vector3(1.9f, .6f, 4.2f) }, MaterialOverride = paint, Position = new Vector3(0, .65f, 0) });
        AddChild(new MeshInstance3D { Name = "Cabin", Mesh = new BoxMesh { Size = new Vector3(1.65f, .55f, 2.0f) }, MaterialOverride = glass, Position = new Vector3(0, 1.2f, .25f) });
        foreach (var (part, x, z) in new[] { ("WheelFL", -.98f, -1.35f), ("WheelFR", .98f, -1.35f), ("WheelRL", -.98f, 1.35f), ("WheelRR", .98f, 1.35f) })
        {
            var wheel = new MeshInstance3D { Name = part, Mesh = new CylinderMesh { TopRadius = WheelRadius, BottomRadius = WheelRadius, Height = .3f }, MaterialOverride = dark, Position = new Vector3(x, WheelRadius, z), RotationDegrees = new Vector3(0, 0, 90) };
            AddChild(wheel); _wheels.Add(wheel);
        }
        AddChild(new Label3D { Name = "Tag", Text = name, Position = new Vector3(0, 2.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = .006f, NoDepthTest = true });
    }

    public void SnapTo(Vector3 position, float yaw) { TargetPosition = position; TargetYaw = yaw; Position = position; Rotation = new Vector3(0, yaw, 0); }

    public override void _Process(double delta)
    {
        var weight = 1f - MathF.Exp(-14f * (float)delta);
        Position = Position.Lerp(TargetPosition, weight);
        Rotation = new Vector3(Mathf.LerpAngle(Rotation.X, TargetPitch, weight), Mathf.LerpAngle(Rotation.Y, TargetYaw, weight), 0);
        var spin = Speed * (float)delta / WheelRadius;
        foreach (var wheel in _wheels) wheel.RotateObjectLocal(Vector3.Up, spin);
    }
}
