using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The avatar's soles must rest on the rig origin in every animation state. Regression for the avatar standing
/// waist-deep in the ground (a Hips position track reset to zero by the AnimationTree, plus legs longer than laid out).
/// </summary>
public partial class AvatarGroundingSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(10);
            var (idleLow, idleHigh) = Extent(rig);
            Require(idleLow is > -.02f and < .03f, $"idle soles are not on the ground: lowest={idleLow:F3}");
            Require(idleHigh - idleLow is > 1.7f and < 1.95f, $"avatar height does not match the 1.8 m capsule: {idleHigh - idleLow:F3}");
            foreach (var state in Enum.GetValues<AvatarMotionState>().Where(x => x is not (AvatarMotionState.Sit or AvatarMotionState.SitFloor or AvatarMotionState.Swim or AvatarMotionState.FlyMove or AvatarMotionState.StandUp or AvatarMotionState.Dance)))
            {
                rig.SetMotionState(state, true);
                for (var sample = 0; sample < 10; sample++)
                {
                    await Frames(8);
                    var (low, high) = Extent(rig);
                    Require(low > -.12f, $"{state}: avatar sunk into the ground, lowest={low:F3}");
                    Require(Mathf.Abs(high - idleHigh) < .15f, $"{state}: body dropped, highest={high:F3} idle={idleHigh:F3}");
                }
            }
            GD.Print($"event=avatar_grounding result=passed idle_low={idleLow:F3} idle_high={idleHigh:F3}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar_grounding result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static (float Low, float High) Extent(Node node)
    {
        var low = float.MaxValue; var high = float.MinValue;
        void Visit(Node current)
        {
            if (current is VoiceIndicator) return; // the dot above the name is interface, not part of the body
            if (current is MeshInstance3D { Mesh: not null } mesh)
            {
                var box = mesh.Mesh.GetAabb();
                for (var corner = 0; corner < 8; corner++)
                {
                    var y = (mesh.GlobalTransform * (box.Position + new Vector3((corner & 1) * box.Size.X, ((corner >> 1) & 1) * box.Size.Y, ((corner >> 2) & 1) * box.Size.Z))).Y;
                    low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                }
            }
            foreach (var child in current.GetChildren()) Visit(child);
        }
        Visit(node);
        return (low, high);
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
