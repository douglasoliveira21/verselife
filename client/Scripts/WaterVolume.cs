using Godot;

namespace LifeVerse.Client.Scripts;

public partial class WaterVolume : Area3D
{
    public override void _Ready()
    {
        BodyEntered += body => { if (body is PlayerController player) player.SetSwimming(true); };
        BodyExited += body => { if (body is PlayerController player) player.SetSwimming(false); };
    }
}
