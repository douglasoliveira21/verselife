using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// Not a pass/fail test: draws avatars in different motion states side by side, seen from the side (orthographic), and saves the
/// picture so a person can judge the poses. Needs a window and LIFEVERSE_CAPTURE_PATH. LIFEVERSE_POSE_STATES picks the states
/// (comma separated, default Idle,Walk,Run,Jump,Fall,Sit,Wave,Dance).
public partial class AvatarPoseSheet : Node3D
{
    public override async void _Ready()
    {
        try
        {
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.78f, .84f, .9f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .7f } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 60, 0), LightEnergy = 1f });
            var names = (OS.GetEnvironment("LIFEVERSE_POSE_STATES") is { Length: > 0 } text ? text : "Idle,Walk,Run,Jump,Fall,Sit,Wave,Dance").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var floor = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.02f, .02f, names.Length * 1.6f + 2f) }, Position = new Vector3(0, -.01f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.3f, .3f, .3f) } };
            AddChild(floor);
            var rigs = new List<AvatarRig>();
            for (var i = 0; i < names.Length; i++)
            {
                // Side view from +X: the avatars face -Z, so they all look to the left of the picture.
                var holder = new Node3D { Position = new Vector3(0, 0, (i - (names.Length - 1) / 2f) * 1.6f) }; AddChild(holder);
                var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); holder.AddChild(rig); rigs.Add(rig);
            }
            AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.5f, Position = new Vector3(8, .95f, 0), RotationDegrees = new Vector3(0, 90, 0), Current = true });
            await Frames(5);
            for (var i = 0; i < names.Length; i++) if (Enum.TryParse<AvatarMotionState>(names[i], out var state)) rigs[i].SetMotionState(state, true);
            await ToSignal(GetTree().CreateTimer(1.4), SceneTreeTimer.SignalName.Timeout);
            await Frames(2);
            GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-poses.png");
            GD.Print("event=pose_sheet result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=pose_sheet result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
