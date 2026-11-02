using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

public partial class AnimationStateSmoke : Node
{
    public override async void _Ready()
    {
        var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>();
        AddChild(rig);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (var state in Enum.GetValues<AvatarMotionState>())
        {
            rig.SetMotionState(state, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print($"event=animation_state_smoke states={Enum.GetValues<AvatarMotionState>().Length} result=passed");
        GetTree().Quit();
    }
}
