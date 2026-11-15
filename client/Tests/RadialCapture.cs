using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the radial menu over the world and saves screenshots (LIFEVERSE_CAPTURE_PATH): the main circle with its side panel, a compact circle and a submenu.</summary>
public partial class RadialCapture : Node
{
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static RadialItem Leaf(string label, string icon, bool enabled = true, bool danger = false) => new() { Label = label, Icon = icon, Enabled = enabled, Destructive = danger, Run = () => { } };

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(40);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var ui = world.GetNode<ViewerUI>("ViewerUI"); var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"); var menu = ui.Radial!;
            var screen = GetViewport().GetVisibleRect().Size; var at = screen / 2f;
            List<RadialItem> items =
            [
                Leaf("Tocar", "hand"), Leaf("Sentar", "user"),
                new() { Label = "Interagir", Icon = "hand", SidePanel = true, Children = [Leaf("Cumprimentar", "hand"), Leaf("Abraçar", "people"), Leaf("Apertar mão", "hand"), Leaf("Dançar junto", "volume"), Leaf("Sentar junto", "user"), Leaf("Oferecer animação…", "play")] },
                Leaf("Inventário", "bag"), Leaf("Pagar", "coin"), Leaf("Teleporte", "map"), Leaf("Grupos", "people"), Leaf("Moderação", "alert", true, true), Leaf("Mais opções", "dots"), Leaf("Ver perfil", "user")
            ];
            menu.Open(at, "Marina Costa", "user", items); await Frames(30);
            menu.HoverForTesting(new Vector2(40, -95)); await Frames(20); Save(path, "avatar");
            menu.PickForTesting(2); await Frames(20); menu.HoverForTesting(new Vector2(0, 0)); Save(path, "painel");
            menu.Close();
            menu.Open(new Vector2(screen.X - 40, 120), "Mesa", "cube", [.. items.Take(4), Leaf("Pegar", "bag", false), Leaf("Editar", "hammer"), Leaf("Propriedades", "settings"), Leaf("Excluir", "trash", true, true)]); await Frames(25); menu.HoverForTesting(new Vector2(-60, 30)); await Frames(15); Save(path, "canto");
            menu.Close();
            var real = InteractionRegistry.Build(new AvatarTarget(2, "Rafa Lima", Vector3.Zero), new ContextServices { Actor = () => new LifeVerse.Shared.Interaction.ContextActor(Guid.NewGuid()), Relation = _ => "friend" });
            menu.Open(at, "Rafa Lima", "user", real); await Frames(25); menu.PickForTesting(7); await Frames(10); menu.HoverForTesting(new Vector2(30, 0)); await Frames(10); Save(path, "moderacao");
            menu.Close();
            GD.Print("event=radial_capture result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=radial_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private void Save(string path, string name) { if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print($"event=radial_capture name={name}"); }
}
