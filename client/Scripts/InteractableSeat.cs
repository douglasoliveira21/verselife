using Godot;

namespace LifeVerse.Client.Scripts;

public partial class InteractableSeat : StaticBody3D
{
    public Transform3D SeatTransform => GetNode<Marker3D>("SeatAnchor").GlobalTransform;
    public Vector3 ExitPosition => GetNode<Marker3D>("ExitAnchor").GlobalPosition;
    public void Interact(PlayerController player) => player.Sit(this);
}
