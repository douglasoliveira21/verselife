using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Settings;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 1 of the settings menu: the window (categories, tabs, search, footer, profiles, dialogs, keyboard, responsive layout) over a store of its own, and the runtime rules behind the Geral tab.</summary>
public partial class SettingsWindowSmoke : Node
{
    private const string Root = "user://smoke_settings_window";

    private static void Wipe() { var absolute = ProjectSettings.GlobalizePath(Root); if (!DirAccess.DirExistsAbsolute(absolute)) return; foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}"); }
    private static T? Find<T>(Node node, Func<T, bool>? test = null) where T : Node { if (node is T t && (test is null || test(t))) return t; foreach (var child in node.GetChildren()) if (Find(child, test) is { } hit) return hit; return null; }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    public override async void _Ready()
    {
        try
        {
            Wipe();
            // ---- the rules behind the Geral tab
            Require(Mathf.IsEqualApprox(RenderScale.Next(1f, 40, 60, .6f, 1f), .95f) && Mathf.IsEqualApprox(RenderScale.Next(.8f, 60, 60, .6f, 1f), .85f) && RenderScale.Next(.6f, 10, 60, .6f, 1f) == .6f && RenderScale.Next(1f, 200, 60, .6f, 1f) == 1f && RenderScale.Next(.8f, 55, 60, .6f, 1f) == .8f, "dynamic resolution steps down under the goal, up at it, and stays within its limits");
            var crowd = SettingsAppliers.BudgetFor(40, "high", true); var tight = SettingsAppliers.BudgetFor(3, "low", false);
            Require(crowd.MaxFull == 40 && crowd.FullRange > new DetailBudget().FullRange && tight.MaxFull == 3 && tight.FullRange < new DetailBudget().FullRange, "the avatar LOD and limit move the budget");
            Require(tight.MaxReduced == 0 && tight.ReducedRange == tight.FullRange && crowd.MaxReduced > 0, "without simplification there is no reduced step");
            Require(SettingsAppliers.BudgetFor(999, "medium", true).MaxFull == 40 && SettingsAppliers.BudgetFor(-4, "medium", true).MaxFull == 1, "the limit of full avatars is clamped");
            Require(DisplayOptions.AspectOf(1920, 1080) == "16:9" && DisplayOptions.AspectOf(2560, 1080) == "21:9" && DisplayOptions.AspectOf(1920, 1061) == "" && DisplayOptions.AspectOf(1920, 1200) == "16:10", "aspect labels");
            Require(DisplayOptions.Monitors().Count >= 1 && DisplayOptions.Resolutions(0).Count >= 1 && DisplayOptions.Modes.Count == 3, "the device offers monitors, resolutions and modes");
            Require(SettingsCatalog.Find("perf.object_lod") is { IsAvailable: false } && SettingsCatalog.Find("metrics.cpu_gpu") is { Unavailable: { Length: > 20 } }, "what the project cannot do is marked unavailable with a reason");

            // ---- the window, in a viewport of a given size
            var holder = new SubViewport { Size = new Vector2I(2400, 1350), Disable3D = true, TransparentBg = true, GuiDisableInput = false }; AddChild(holder);
            var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets);
            var shadowApplied = new List<object>(); store.RegisterApplier("graphics.shadow_distance", v => shadowApplied.Add(v));
            var window = new SettingsWindow(store); holder.AddChild(window); window.Open(SettingCategory.Graphics); await Frames(8);
            Require(window.Visible && window.Category == SettingCategory.Graphics && window.Tab == "Geral", "opens on Gráficos e desempenho › Geral");
            Require(window.CategoryButtons.Count == 18 && window.CategoryButtons.Values.Count(b => b.ButtonPressed) == 1 && window.CategoryButtons[SettingCategory.Graphics].ButtonPressed, "18 categories, only the open one selected");
            var labels = window.CategoryButtons.OrderBy(p => (int)p.Key).Select(p => p.Value.Text).ToArray();
            Require(labels.SequenceEqual(SettingCategories.Labels) && labels[0] == "Geral e conta" && labels[17] == "Avançado e diagnóstico", "the categories keep the order and names of the design");
            Require(window.Tabs.Count == 5 && window.Tabs.TabAt(0).Text == "Geral" && window.Tabs.TabAt(4).Text == "Estatísticas", "the five tabs of Gráficos");
            var rect = window.FrameRect; var view = holder.GetVisibleRect().Size;
            Require(rect.Position.X >= 0 && rect.Position.Y >= 0 && rect.End.X <= view.X + .5f && rect.End.Y <= view.Y + .5f && rect.Size.X > view.X * .85f, $"the frame fits the screen and is most of it ({rect} in {view})");
            foreach (var id in new[] { "graphics.preset", "display.monitor", "display.resolution", "display.mode", "graphics.vsync", "graphics.render_scale", "graphics.dynamic_resolution", "graphics.fps_limit", "perf.render_distance", "graphics.shadow_distance", "avatars.max_full", "perf.object_lod", "avatars.lod", "avatars.simplify_distant", "metrics.fps", "metrics.cpu_gpu", "metrics.memory", "metrics.frame_time" })
                Require(window.RowOf(id) is not null, $"the Geral tab has {id}");
            Require(window.TwoColumns, "wide: two columns");
            Require(Find<CheckButton>(window.RowOf("metrics.cpu_gpu")!) is { Disabled: true } && window.RowOf("metrics.cpu_gpu")!.TooltipText.Contains("Indisponível") && Find<OptionButton>(window.RowOf("perf.object_lod")!) is { Disabled: true }, "unavailable settings are shown disabled with the reason, not faked");
            Require(window.ApplyButton.Disabled && window.DiscardButton.Disabled && !window.FooterNote.Text.Contains("não aplicada"), "nothing pending: Aplicar and Descartar are off");

            // ---- changing things through the controls
            var fps = Find<HSlider>(window.RowOf("graphics.fps_limit")!)!; fps.Value = 200;
            Require((int)store.Effective("graphics.fps_limit") == 200 && store.IsPending("graphics.fps_limit") && !window.ApplyButton.Disabled && !window.DiscardButton.Disabled && window.FooterNote.Text.Contains("1 alteração"), "moving a slider makes a pending change and turns the buttons on");
            Require(Find<Label>(window.RowOf("graphics.fps_limit")!, l => l.Text == "200 FPS") is not null, "the number beside the slider follows");
            window.DiscardButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(!store.HasPending && (int)Find<HSlider>(window.RowOf("graphics.fps_limit")!)!.Value == 120 && window.ApplyButton.Disabled, "Descartar puts the controls back");
            var preset = Find<OptionButton>(window.RowOf("graphics.preset")!)!; var lowIndex = Array.IndexOf(GraphicsQuality.Keys, "low");
            preset.Select(lowIndex); preset.EmitSignal(OptionButton.SignalName.ItemSelected, lowIndex); await Frames(2);
            Require((string)store.Effective("graphics.preset") == "low" && (int)store.Effective("graphics.shadow_distance") == 55, "choosing a preset proposes its values");
            Find<HSlider>(window.RowOf("graphics.shadow_distance")!)!.Value = 300; await Frames(2);
            Require((string)store.Effective("graphics.preset") == "custom" && preset.GetItemText(preset.Selected) == "Personalizada", "changing a controlled value makes it Personalizada");
            window.DiscardButton.EmitSignal(BaseButton.SignalName.Pressed);
            var vsync = Find<CheckButton>(window.RowOf("graphics.vsync")!)!; vsync.ButtonPressed = false; Require(!(bool)store.Effective("graphics.vsync"), "a toggle changes its setting");
            window.ApplyButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(!(bool)store.Value("graphics.vsync") && !window.Dialog.IsOpen && !store.HasPending, "Aplicar saves it, and a change that is not on the screen asks nothing");

            // ---- restore the category
            Find<HSlider>(window.RowOf("graphics.fps_limit")!)!.Value = 60;
            window.RestoreButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(window.Dialog.IsOpen && window.Dialog.MessageForTesting.Contains("Gráficos e desempenho"), "Restaurar padrão asks first");
            window.Dialog.ButtonsForTesting[1].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(!window.Dialog.IsOpen && (int)store.Effective("graphics.fps_limit") == 60, "Cancelar leaves things as they are");
            window.RestoreButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2); window.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require((int)store.Effective("graphics.fps_limit") == 120 && (bool)store.Effective("graphics.vsync") && store.IsPending("graphics.vsync"), "Restaurar proposes the defaults of the category (not applied until Aplicar)");
            window.DiscardButton.EmitSignal(BaseButton.SignalName.Pressed);

            // ---- the screen change is confirmed or goes back
            Find<OptionButton>(window.RowOf("display.mode")!)!.EmitSignal(OptionButton.SignalName.ItemSelected, 1); await Frames(2);
            Require(store.IsPending("display.mode"), "the mode is a pending change");
            window.ApplyChanges(); await Frames(2);
            Require(window.Dialog.IsOpen && window.Dialog.ButtonsForTesting.Count == 2 && window.Dialog.ButtonsForTesting[0].Text == "Manter", "a change of the screen asks for confirmation");
            window.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(!window.Dialog.IsOpen && (string)store.Value("display.mode") == "fullscreen", "Manter keeps it");
            var reverted = false; window.Dialog.Ask("Teste", "Contagem", [("Ok", UiStyle.ButtonKind.Primary, null)], seconds: .5, onTimeout: () => reverted = true);
            window.Dialog._Process(.3); Require(window.Dialog.IsOpen && !reverted, "the countdown runs"); window.Dialog._Process(.4);
            Require(!window.Dialog.IsOpen && reverted, "and at zero the dialog closes and goes back by itself");

            // ---- search
            window.SearchField.Text = "sombras"; window.SearchField.EmitSignal(LineEdit.SignalName.TextChanged, "sombras"); await Frames(3);
            var results = window.PageHost.GetChildren().OfType<PanelContainer>().ToList();
            Require(window.SearchActive && results.Count >= 1 && results.Any(r => (string)r.GetMeta("setting") == "graphics.shadow_distance"), "the search lists the real settings that match");
            Require(Find<RichTextLabel>(results[0]) is { } rich && rich.Text.Contains("[b][color=#12F2E0]"), "the words found are highlighted");
            window.SearchField.Text = "xyzxyz"; window.SearchField.EmitSignal(LineEdit.SignalName.TextChanged, "xyzxyz"); await Frames(2);
            Require(window.SearchActive && !window.PageHost.GetChildren().OfType<PanelContainer>().Any(), "nonsense finds nothing");
            window.SearchField.Text = "volume"; window.SearchField.EmitSignal(LineEdit.SignalName.TextChanged, "volume"); await Frames(2);
            window.GoTo(SettingsCatalog.Find("audio.master")!); await Frames(3);
            Require(!window.SearchActive && window.Category == SettingCategory.Audio && window.RowOf("audio.master") is not null && window.SearchField.Text.Length == 0, "clicking a result opens its page");
            Require(window.CategoryButtons.Values.Count(b => b.ButtonPressed) == 1 && window.RestoreButton.Disabled == false, "navigation keeps one category selected");

            // ---- categories without options are honest about it
            var emptyStore = new SettingsStore(Root + "_empty", SettingsCatalog.All.Where(d => d.Category != SettingCategory.Scripts), SettingsCatalog.Presets);
            var emptyWindow = new SettingsWindow(emptyStore); holder.AddChild(emptyWindow); emptyWindow.Open(SettingCategory.Scripts); await Frames(4);
            Require(emptyWindow.RowIds.Count == 0 && emptyWindow.RestoreButton.Disabled && Find<Label>(emptyWindow.PageHost, l => l.Text.Contains("Nada aqui é simulado")) is not null, "a category with nothing real says so and offers no restore");
            emptyWindow.QueueFree();
            window.SelectCategory(SettingCategory.Graphics, "Qualidade"); await Frames(2);
            foreach (var id in new[] { "graphics.texture_quality", "graphics.anisotropic", "graphics.msaa", "graphics.shadows_enabled", "graphics.shadow_quality", "graphics.shadow_cascades", "graphics.ssao", "graphics.ssao_quality", "graphics.ssil", "graphics.glow", "graphics.color_adjust", "graphics.ssr", "graphics.volumetric", "graphics.fog", "graphics.particles", "graphics.water_waves", "graphics.vegetation" })
                Require(window.Tab == "Qualidade" && window.RowOf(id) is not null, $"Qualidade has {id}");
            Require(Find<OptionButton>(window.RowOf("graphics.vegetation")!) is { Disabled: true } && window.RowOf("graphics.vegetation")!.TooltipText.Contains("Indisponível"), "vegetation is shown unavailable with the reason");
            Find<CheckButton>(window.RowOf("graphics.ssao")!)!.ButtonPressed = false; await Frames(2);
            Require((string)store.Effective("graphics.preset") == "custom", "changing an effect by hand makes the preset Personalizada");
            window.DiscardButton.EmitSignal(BaseButton.SignalName.Pressed);
            window.SelectCategory(SettingCategory.Graphics, "Mundo e avatares"); await Frames(2);
            foreach (var id in new[] { "world.object_distance", "world.max_lights", "world.max_light_shadows", "avatars.visibility_distance", "avatars.max_reduced", "avatars.triangle_budget", "avatars.animation_quality", "avatars.texture_quality" })
                Require(window.RowOf(id) is not null, $"Mundo e avatares has {id}");
            window.SelectCategory(SettingCategory.Graphics, "Otimização"); await Frames(2);
            foreach (var id in new[] { "perf.auto_quality", "perf.background_fps", "perf.shader_cache", "perf.resource_cache", "perf.preload_assets" }) Require(window.RowOf(id) is not null, $"Otimização has {id}");
            Require(Find<CheckButton>(window.RowOf("perf.shader_cache")!) is { Disabled: true }, "what the engine does not allow is shown unavailable");
            window.SelectCategory(SettingCategory.Graphics, "Estatísticas"); await Frames(3); window.RefreshStats();
            foreach (var key in new[] { "fps", "average", "frame", "ram", "vram", "objects", "triangles", "draw_calls", "nodes", "resources", "avatars", "avatars_total", "adapter", "renderer" })
                Require(window.StatLabels.TryGetValue(key, out var stat) && stat.Text.Length > 0 && stat.Text != "—" || key is "adapter" or "renderer", $"Estatísticas shows {key}");
            Require(window.StatLabels["draw_calls"].Text.All(c => char.IsDigit(c) || c == '.') && window.StatLabels["ram"].Text.EndsWith(" MB"), "the numbers are formatted");
            window.SearchField.Text = "fps"; window.SearchField.EmitSignal(LineEdit.SignalName.TextChanged, "fps"); await Frames(2); window.ClearSearch();
            window.SelectCategory(SettingCategory.Graphics, "Geral"); await Frames(2);

            // ---- keyboard
            window.SearchField.ReleaseFocus();
            window._Input(new InputEventKey { Keycode = Key.F, CtrlPressed = true, Pressed = true }); await Frames(1);
            Require(window.SearchField.HasFocus(), "Ctrl+F goes to the search");
            window.SearchField.Text = "fps"; window.SearchField.EmitSignal(LineEdit.SignalName.TextChanged, "fps"); window._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frames(2);
            Require(!window.SearchActive && window.Visible, "Esc clears the search first");
            store.Set("graphics.fps_limit", 90); window._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frames(2);
            Require(window.Visible && window.Dialog.IsOpen && window.Dialog.ButtonsForTesting.Count == 3, "closing with pending changes asks what to do with them");
            window._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frames(1);
            Require(window.Visible && !window.Dialog.IsOpen && store.HasPending, "Esc on the question keeps editing");
            window.RequestClose(); window.Dialog.ButtonsForTesting[1].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(!window.Visible && !store.HasPending, "Descartar e fechar closes and drops them");
            window.Open(SettingCategory.Graphics); await Frames(2);
            Require(window.Visible && !window.Dialog.IsOpen, "it opens again");

            // ---- profiles
            Require(window.ProfileChoice.ItemCount == 1 && window.ProfileChoice.GetItemText(0) == "Padrão", "the default profile is listed");
            store.Create("Jogo leve"); window.RefreshProfiles(); Require(window.ProfileChoice.ItemCount == 2 && window.ProfileChoice.GetItemText(window.ProfileChoice.Selected) == "Padrão", "profiles are listed with the active one selected");
            window.ProfileChoice.EmitSignal(OptionButton.SignalName.ItemSelected, 1); await Frames(2);
            Require(store.ActiveProfile == "Jogo leve" && window.ProfileChoice.GetItemText(window.ProfileChoice.Selected) == "Jogo leve", "choosing a profile switches to it");
            store.Set("graphics.fps_limit", 70); window.ProfileChoice.EmitSignal(OptionButton.SignalName.ItemSelected, 0); await Frames(2);
            Require(window.Dialog.IsOpen && store.ActiveProfile == "Jogo leve", "switching with pending changes asks first");
            window.Dialog.ButtonsForTesting[1].EmitSignal(BaseButton.SignalName.Pressed); store.Discard(); await Frames(1);

            // ---- window states
            var normal = window.FrameRect;
            Find<Button>(window, b => b.TooltipText == "Maximizar")!.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(window.Maximized && window.FrameRect.Size.X > normal.Size.X && window.FrameRect.End.X <= view.X + .5f, "maximize fills the screen");
            Find<Button>(window, b => b.TooltipText == "Restaurar")!.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(!window.Maximized && Mathf.IsEqualApprox(window.FrameRect.Size.X, normal.Size.X, 1f), "restore goes back");
            Find<Button>(window, b => b.TooltipText == "Minimizar")!.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(window.Minimized && window.FrameRect.Size.Y < 100, "minimize leaves only the header");
            Find<Button>(window, b => b.TooltipText == "Minimizar")!.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);

            // ---- responsive: every common size, with the page in one or two columns
            foreach (var size in new Vector2I[] { new(1778, 1000), new(1896, 1067), new(2222, 1250), new(2400, 1350), new(3225, 1350), new(1100, 650), new(2400, 1350) })
            {
                holder.Size = size; await Frames(4); window.LayoutFrame(); await Frames(3);
                var r = window.FrameRect;
                Require(r.Position.X >= -.5f && r.Position.Y >= -.5f && r.End.X <= size.X + .5f && r.End.Y <= size.Y + .5f, $"{size}: the window fits ({r})");
                Require(window.CategoryButtons.Values.All(b => b.GetGlobalRect().Size.X > 100), $"{size}: the side bar is usable");
                Require(window.ApplyButton.GetGlobalRect().End.X <= size.X + .5f && window.ApplyButton.GetGlobalRect().End.Y <= size.Y + .5f, $"{size}: the footer is on the screen");
                Require(window.TwoColumns == (window.PageHost.Size.X >= SettingsWindow.TwoColumnWidth), $"{size}: columns follow the width ({window.PageHost.Size.X})");
            }

            Wipe();
            GD.Print("event=settings_window_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=settings_window_smoke result=failed error={exception.Message} {exception.StackTrace}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
