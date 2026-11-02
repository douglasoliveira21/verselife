using Godot;

namespace LifeVerse.Client.Scripts;

public partial class InteractableSeat : StaticBody3D
{
    private PlayerController? _occupant;
    public bool IsOccupied => IsInstanceValid(_occupant);
    public Transform3D SeatTransform => GetNode<Marker3D>("SeatAnchor").GlobalTransform;
    public Vector3 ExitPosition => GetNode<Marker3D>("ExitAnchor").GlobalPosition;
    public bool TryOccupy(PlayerController player) { if (IsOccupied && _occupant != player) return false; _occupant = player; return true; }
    public void Release(PlayerController player) { if (_occupant == player) _occupant = null; }
    public void Interact(PlayerController player) { if (!IsOccupied || _occupant == player) player.Sit(this); }
}
