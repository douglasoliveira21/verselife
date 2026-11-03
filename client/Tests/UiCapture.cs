using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// Not a pass/fail test: opens the world scene with sample windows and saves a picture of the interface, to compare it with the
/// design references in client/Assets/ui. Needs a window and LIFEVERSE_CAPTURE_PATH.
public partial class UiCapture : Node
{
    public override async void _Ready()
    {
        try
        {
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "login")
            {
                AddChild(GD.Load<PackedScene>("res://Scenes/Login.tscn").Instantiate<Control>());
                for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-ui.png");
                GetTree().Quit();
                return;
            }
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            for (var i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ui = world.GetNode<ViewerUI>("ViewerUI");
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "import")
            {
                var sample = new Node3D(); sample.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .2f, BottomRadius = .5f, Height = 1.6f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#C98B5A") } });
                var document = new GltfDocument(); var state = new GltfState(); document.AppendFromScene(sample, state);
                world.GetNode<InventoryPanel>("InventoryPanel").ImportDialogForTesting.ShowForTesting("vaso.glb", document.GenerateBuffer(state));
            }
            if (OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") is "radial" or "radial2")
            {
                RadialItem Item(string label, string icon, bool accent = false, List<RadialItem>? children = null) => new() { Label = label, Icon = icon, Accent = accent, Children = children };
                var radial = ui.Radial!;
                radial.Open(new Vector2(790, 430), "Avatar\nLucas Ferreira", "user", [Item("Ver Perfil", "user"), Item("Mensagem", "chat"), Item("Adicionar Amigo", "user-add"), Item("Interagir", "hand", children: [Item("Cumprimentar", "hand"), Item("Acenar", "hand"), Item("Abraçar", "people"), Item("Dançar", "volume"), Item("Seguir", "run")]), Item("Teleporte", "pin"), Item("Enviar Item", "cube"), Item("Mais Opções", "dots"), Item("Fechar", "close", true)]);
                if (OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "radial2") radial.PickForTesting(3);
                radial.HoverForTesting(new Vector2(OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "radial2" ? 0 : 0, OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") == "radial2" ? 0 : -130));
            }
            ui.PreviewForTesting(OS.GetEnvironment("LIFEVERSE_CAPTURE_VIEW") is { Length: > 0 } view ? view : "people");
            for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng($"{OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH")}-ui.png");
            GD.Print("event=ui_capture result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=ui_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }
}
