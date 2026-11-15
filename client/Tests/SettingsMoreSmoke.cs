using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Notifications;
using LifeVerse.Client.Scripts.Settings;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 4 of the settings menu: the remaining categories (account, avatar, chat, privacy, notifications, inventory, building, world, scripts, network, interface, accessibility, advanced): each setting reaches the game, the buttons work, and the interface settings stay in step with the viewer.</summary>
public partial class SettingsMoreSmoke : Node
{
    private const string Root = "user://smoke_settings_more";
    private static void WipeDir(string path) { var absolute = ProjectSettings.GlobalizePath(path); if (!DirAccess.DirExistsAbsolute(absolute)) return; foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static T? Find<T>(Node node, Func<T, bool>? test = null) where T : Node { if (node is T t && (test is null || test(t))) return t; foreach (var child in node.GetChildren()) if (Find(child, test) is { } hit) return hit; return null; }

    public override async void _Ready()
    {
        try
        {
            WipeDir(Root); WipeDir(Root + "_import"); WipeDir(Root + "_window"); WipeDir(Root + "_empty");
            // ---- the catalog as a whole
            Require(SettingsService.IsTestRun && SettingsService.Root.EndsWith("perfis_teste"), "a test run never touches the real profiles");
            foreach (var category in Enum.GetValues<SettingCategory>()) Require(SettingsCatalog.All.Any(d => d.Category == category), $"{SettingCategories.LabelOf(category)} has settings");
            SettingsAppliers.RegisterActions();
            foreach (var d in SettingsCatalog.All.Where(d => d.Kind == SettingKind.Action)) Require(SettingsActions.Has(d.Id) && d.ButtonText.Length > 0, $"{d.Id}: the button does something");
            foreach (var d in SettingsCatalog.All.Where(d => d.Kind == SettingKind.Info)) Require(d.InfoSource is not null, $"{d.Id}: reads a value");
            foreach (var d in SettingsCatalog.All.Where(d => !d.IsAvailable)) Require(d.Unavailable!.Length > 25, $"{d.Id}: unavailable says why");
            Require(SettingsCatalog.All.Count(d => d.Kind == SettingKind.Info || d.Kind == SettingKind.Action) >= 25 && SettingsCatalog.All.Count > 130, $"the catalog is large ({SettingsCatalog.All.Count})");
            Require(SettingsCatalog.All.All(d => d.Kind is SettingKind.Action or SettingKind.Info || d.Normalize(d.Default).Equals(d.Default)), "every default is a valid value");

            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(25);
            var ui = world.GetNode<ViewerUI>("ViewerUI");
            NotificationPrefs.Path = Root + "/notification_prefs.cfg";
            var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets); SettingsAppliers.Register(store);

            // ---- avatar, chat, notifications, accessibility, network, scripts, inventory, building
            store.Set("avatar.show_names", false); store.Set("avatar.show_own_name", false); store.Set("avatar.show_titles", false); store.Set("avatar.footsteps", false); store.Set("avatar.default_animations", false);
            store.Set("chat.font_size", 22); store.Set("chat.show_time", false); store.Set("chat.visible_seconds", 12);
            store.Set("notifications.dnd", true); store.Set("notifications.toast_seconds", 9); store.Set("notifications.max_toasts", 5);
            store.Set("notifications.toast_grupos", false); store.Set("notifications.toast_convites", false);
            store.Set("accessibility.reduce_motion", true); store.Set("network.auto_reconnect", false);
            store.Set("scripts.font_size", 20); store.Set("scripts.indent", 2); store.Set("scripts.autocomplete", false);
            store.Set("inventory.default_sort", "2"); store.Set("inventory.subfolders", false);
            store.Set("build.snap", true); store.Set("build.space", "local"); store.Set("build.move_step", 1); store.Set("build.rotate_step", 45); store.Set("build.scale_step", .25);
            store.Apply();
            Require(!NamePlate.ShowNames && !NamePlate.ShowOwnName && !NamePlate.ShowTitles && !AvatarRig.FootstepsEnabled && DefaultAvatarAnimations.Disabled, "avatar settings reach the name plates, the footsteps and the animations");
            Require(ViewerUI.ChatFontSize == 22 && !ViewerUI.ChatTimes && ViewerUI.ChatSeconds == 12, "chat settings reach the chat");
            Require(ViewerUI.DoNotDisturb && ViewerUI.ToastSeconds == 9 && ViewerUI.MaxVisibleToasts == 5 && !NotificationPrefs.ToastsOn("grupos") && !NotificationPrefs.ToastsOn("convites") && NotificationPrefs.ToastsOn("sistema"), "notification settings reach the toasts and the old preference file");
            Require(Accessibility.ReduceMotion && !NetworkManager.AutoReconnect, "reduced motion and automatic reconnection switch");
            Require(ScriptEditorWindow.EditorFontSize == 20 && ScriptEditorWindow.EditorIndent == 2 && !ScriptEditorWindow.EditorAutocomplete && InventoryPanel.DefaultSort == 2 && !InventoryPanel.DefaultSubfolders, "script editor and inventory defaults");
            Require(Build.BuildEditorWindow.Defaults == (true, true, 1f, 45f, .25f), "the build editor starts as chosen");
            // "Não perturbar": plain notices stay quiet, warnings and errors get through
            var before = world.GetNode("ViewerUI").FindChild("*Toast*", true, false); _ = before;
            ui.ApplyChatFont(); Require(Find<RichTextLabel>(ui, r => r.GetThemeFontSize("normal_font_size") == 22) is not null, "the chat text takes the size");
            store.ResetAll(); store.Apply();
            Require(NamePlate.ShowNames && AvatarRig.FootstepsEnabled && !DefaultAvatarAnimations.Disabled && ViewerUI.ChatFontSize == 17 && !ViewerUI.DoNotDisturb && NetworkManager.AutoReconnect && !Accessibility.ReduceMotion, "restoring puts everything back");

            // ---- the interface settings follow the viewer in both directions
            store.Set("interface.mode", "compact"); store.Set("interface.scale", "1.25"); store.Set("interface.window_opacity", 60); store.Set("interface.map_opacity", 50); store.Set("interface.compact_toolbar", true); store.Set("interface.side_bar", false); store.Set("chat.opacity", 55);
            ui.ApplyInterfaceFromStore(store.Apply() is { } ? store : store);
            var i = ui.InterfaceSettingsForTesting;
            Require(i.Mode == "compact" && !i.AutoScale && Mathf.IsEqualApprox(i.Scale, 1.25f) && Mathf.IsEqualApprox(i.WindowOpacity, .6f) && Mathf.IsEqualApprox(i.MiniMapOpacity, .5f) && i.CompactToolbar && !i.SideToolbarOpen && Mathf.IsEqualApprox(i.ChatOpacity, .55f), "the interface takes what the settings say");
            Require(!ui.BarVisibleForTesting("side") && !ui.BarVisibleForTesting("location") && ui.BarVisibleForTesting("top"), "and shows it (compact: no side or location bar)");
            ui.SetInterfaceModeForTesting("full"); ui.ToggleSideBarForTesting();
            var service = SettingsService.Store;
            Require((string)service.Value("interface.mode") == "full" && (bool)service.Value("interface.side_bar"), "what the viewer changes by itself is mirrored into the settings");
            Require(ViewerUI.ScaleIdOf(true, 1f) == "auto" && ViewerUI.ScaleIdOf(false, 1.25f) == "1.25" && ViewerUI.ScaleIdOf(false, .75f) == "0.75", "scale ids");
            ui.ResetInterfaceForTesting(); await Frames(2);
            Require((string)service.Value("interface.mode") == "full" && (string)service.Value("interface.scale") == "auto" && (int)service.Value("chat.opacity") == 80, "restoring the interface restores the settings too");

            // ---- importing what was in other files, once
            var imported = new SettingsStore(Root + "_import", SettingsCatalog.All, SettingsCatalog.Presets);
            Require(imported.ImportOnce("teste", new Dictionary<string, object> { ["chat.font_size"] = 24, ["advanced.version"] = "x", ["nao.existe"] = 1, ["chat.show_time"] = "false" }), "an old source is imported");
            Require((int)imported.Value("chat.font_size") == 24 && !(bool)imported.Value("chat.show_time"), "its values arrive, checked");
            imported.SetAndApply("chat.font_size", 14);
            Require(!imported.ImportOnce("teste", new Dictionary<string, object> { ["chat.font_size"] = 30 }) && (int)imported.Value("chat.font_size") == 14, "and it runs once, never over what was set later");
            var again = new SettingsStore(Root + "_import", SettingsCatalog.All, SettingsCatalog.Presets);
            Require(!again.ImportOnce("teste", new Dictionary<string, object> { ["chat.font_size"] = 30 }) && (int)again.Value("chat.font_size") == 14, "the flag survives a restart and later saves");
            var profile = File.ReadAllText(ProjectSettings.GlobalizePath(Root + "_import/padr%C3%A3o.cfg".Replace("%C3%A3", "ã")));
            Require(!profile.Contains("advanced.version") && !profile.Contains("account.") && profile.Contains("chat.font_size"), "buttons and read-only values are never saved in a profile");

            // ---- the buttons
            var tmp = Root + "/cache"; DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(tmp + "/sub"));
            foreach (var name in new[] { "/a.bin", "/sub/b.bin" }) { using var f = Godot.FileAccess.Open(tmp + name, Godot.FileAccess.ModeFlags.Write); f.StoreString(new string('x', 1000)); }
            Require(SettingsAppliers.DeleteFolderContents(tmp) == 2000 && !DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(tmp + "/sub")), "clearing a cache frees what was in it, subfolders too");
            var diagnostics = SettingsAppliers.DiagnosticsText(store);
            Require(diagnostics.Contains("Versão:") && diagnostics.Contains("graphics.preset") && diagnostics.Contains(AppManager.Version) && !diagnostics.Contains("Bearer") && !diagnostics.Contains("password", StringComparison.OrdinalIgnoreCase) && !diagnostics.Contains("token", StringComparison.OrdinalIgnoreCase), "the diagnostic lists versions and settings and no credential");

            // ---- the window
            var holder = new SubViewport { Size = new Vector2I(2400, 1350), Disable3D = true, TransparentBg = true }; AddChild(holder);
            var windowStore = new SettingsStore(Root + "_window", SettingsCatalog.All, SettingsCatalog.Presets);
            var window = new SettingsWindow(windowStore); holder.AddChild(window); window.Open(SettingCategory.Advanced); await Frames(6);
            Require(Find<Label>(window.RowOf("advanced.version")!, l => l.Text == AppManager.Version) is not null, "a read-only value shows what the game reports");
            Require(Find<Button>(window.RowOf("advanced.clear_cache")!) is { Text: "Limpar cache" } clear, "an action row has its button");
            window.RunAction(SettingsCatalog.Find("advanced.clear_cache")!); await Frames(2);
            Require(window.Dialog.IsOpen && window.Dialog.MessageForTesting.Contains("Apagar"), "a risky action asks first");
            window.Dialog.ButtonsForTesting[1].EmitSignal(BaseButton.SignalName.Pressed); await Frames(1);
            windowStore.Set("chat.font_size", 25); window.RunAction(SettingsCatalog.Find("advanced.restore_all")!); await Frames(2);
            Require(window.Dialog.IsOpen && windowStore.HasPending, "restore all asks and nothing changes until confirmed");
            window.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require((int)windowStore.Value("chat.font_size") == 17 && !windowStore.HasPending, "confirmed: every setting is back to its default and applied");
            window.SelectCategory(SettingCategory.General, "Conta"); await Frames(3);
            Require(window.RowOf("account.user") is not null && window.RowOf("account.leave_island") is not null && Find<Button>(window.RowOf("account.quit")!) is { Text: "Sair do jogo" }, "the account tab shows the account and the session buttons");
            window.SelectCategory(SettingCategory.Interface, null); await Frames(3);
            foreach (var id in new[] { "interface.scale", "interface.mode", "interface.window_opacity", "interface.map_opacity", "interface.compact_toolbar", "interface.side_bar", "interface.restore_layout" }) Require(window.RowOf(id) is not null, $"Interface e HUD has {id}");
            Require(Find<OptionButton>(window.RowOf("interface.scale")!)!.GetItemText(0) == "Automático" && Find<OptionButton>(window.RowOf("interface.scale")!)!.ItemCount == 8, "the scale choices are the ones of the menu");
            foreach (var (category, ids) in new (SettingCategory, string[])[] {
                (SettingCategory.Avatar, ["avatar.show_names", "avatar.footsteps", "avatar.open_mirror"]), (SettingCategory.Chat, ["chat.opacity", "chat.font_size", "chat.translate"]), (SettingCategory.Privacy, ["privacy.external_media", "privacy.online_visibility"]),
                (SettingCategory.Notifications, ["notifications.dnd", "notifications.toast_seconds", "notifications.sound"]), (SettingCategory.Inventory, ["inventory.default_sort"]), (SettingCategory.Building, ["build.snap", "build.import_collision"]),
                (SettingCategory.World, ["world.minimap_open", "world.show_coords"]), (SettingCategory.Scripts, ["scripts.font_size", "scripts.revoke"]), (SettingCategory.Network, ["network.auto_reconnect", "network.state", "network.ping"]),
                (SettingCategory.Accessibility, ["accessibility.reduce_motion", "accessibility.captions"]) })
            { window.SelectCategory(category, null); await Frames(2); foreach (var id in ids) Require(window.RowOf(id) is not null, $"{SettingCategories.LabelOf(category)} has {id}"); }
            window.SelectCategory(SettingCategory.Privacy, null); await Frames(2);
            Require(Find<OptionButton>(window.RowOf("privacy.online_visibility")!) is { Disabled: true } && window.RowOf("privacy.online_visibility")!.TooltipText.Contains("servidor"), "what depends on the server is shown unavailable with the reason");

            WipeDir(Root); WipeDir(Root + "_import"); WipeDir(Root + "_window");
            GD.Print("event=settings_more_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=settings_more_smoke result=failed error={exception.Message} {exception.StackTrace}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
