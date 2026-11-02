using Godot;

namespace LifeVerse.Client.Scripts;

/// Root script of the world scene. The interface itself lives in ViewerUI.
public partial class WorldNetworkUI : Node3D
{
    public override void _Ready() => AddChild(new ViewerUI { Name = "ViewerUI" });
}
