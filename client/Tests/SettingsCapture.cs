using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Settings;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the settings window over the world and saves screenshots (LIFEVERSE_CAPTURE_PATH): Gráficos, a search, another category, and the narrow layout.</summary>
public partial class SettingsCapture : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(40);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var ui = world.GetNode<ViewerUI>("ViewerUI"); var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            ui.OpenSettings(SettingCategory.Graphics); await Frames(20); Save(path, "geral");
            var window = ui.SettingsWindowForTesting!;
            window.SearchField.Text = "sombr"; window.SearchField.EmitSignal(LineEdit.SignalName.TextChanged, "sombr"); await Frames(10); Save(path, "busca");
            window.ClearSearch(); window.SelectCategory(SettingCategory.Audio, null); await Frames(10); Save(path, "audio");
            window.SelectCategory(SettingCategory.Camera, null); await Frames(10); Save(path, "vazia");
            window.SelectCategory(SettingCategory.Graphics, "Geral"); window.Store.Set("graphics.preset", "ultra"); window.Store.Set("graphics.fps_limit", 200); await Frames(10); Save(path, "pendente");
            window.Store.Discard();
            foreach (var (category, tab) in new[] { (SettingCategory.General, "Conta"), (SettingCategory.Privacy, "Geral"), (SettingCategory.Notifications, "Avisos"), (SettingCategory.Interface, "Geral"), (SettingCategory.Advanced, "Diagnóstico"), (SettingCategory.Accessibility, "Geral") }) { window.SelectCategory(category, tab); await Frames(30); Save(path, $"p3-{category}-{tab.Replace(" ", "")}"); }
            foreach (var tab in new[] { "Qualidade", "Mundo e avatares", "Otimização", "Estatísticas" }) { window.SelectCategory(SettingCategory.Graphics, tab); await Frames(40); Save(path, "tab-" + tab.Replace(" ", "")); if (tab == "Estatísticas") { await Frames(120); var s = RenderStats.Read(GetTree().Root, null); GD.Print($"event=dbg cpu={s.CpuRenderMs} gpu={s.GpuRenderMs} fps={s.Fps}"); } }
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=settings_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private void Save(string path, string name) { if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print($"event=settings_capture name={name}"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
