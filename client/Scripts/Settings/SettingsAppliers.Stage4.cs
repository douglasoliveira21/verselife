using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>What the settings of the remaining categories do, and what their buttons do.</summary>
public static partial class SettingsAppliers
{
    private static ViewerUI? Viewer => Tree?.GetFirstNodeInGroup("lifeverse_viewer") as ViewerUI;
    private static readonly string[] InterfaceIds = ["interface.scale", "interface.mode", "interface.window_opacity", "interface.map_opacity", "interface.compact_toolbar", "interface.side_bar", "chat.opacity"];

    private static void RegisterStage4(SettingsStore store)
    {
        store.RegisterApplier("general.keep_on_top", v => DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, (bool)v));

        store.RegisterApplier("avatar.show_names", v => NamePlate.ShowNames = (bool)v);
        store.RegisterApplier("avatar.show_own_name", v => NamePlate.ShowOwnName = (bool)v);
        store.RegisterApplier("avatar.show_titles", v => NamePlate.ShowTitles = (bool)v);
        store.RegisterApplier("avatar.footsteps", v => AvatarRig.FootstepsEnabled = (bool)v);
        store.RegisterApplier("avatar.default_animations", v => DefaultAvatarAnimations.Disabled = !(bool)v);

        store.RegisterApplier("chat.font_size", v => { ViewerUI.ChatFontSize = (int)v; Viewer?.ApplyChatFont(); });
        store.RegisterApplier("chat.show_time", v => ViewerUI.ChatTimes = (bool)v);
        store.RegisterApplier("chat.visible_seconds", v => ViewerUI.ChatSeconds = (int)v);

        store.RegisterApplier("privacy.external_media", v => { if (BuildObjectView.MediaSettings.Enabled != (bool)v) BuildObjectView.MediaSettings.Enabled = (bool)v; });

        foreach (var (key, category) in new[] { ("notifications.toast_grupos", "grupos"), ("notifications.toast_convites", "convites"), ("notifications.toast_sistema", "sistema") })
        {
            var id = category;
            store.RegisterApplier(key, v => { if (Notifications.NotificationPrefs.ToastsOn(id) != (bool)v) Notifications.NotificationPrefs.SetToasts(id, (bool)v); });
        }
        store.RegisterApplier("notifications.dnd", v => ViewerUI.DoNotDisturb = (bool)v);
        store.RegisterApplier("notifications.toast_seconds", v => ViewerUI.ToastSeconds = (int)v);
        store.RegisterApplier("notifications.max_toasts", v => ViewerUI.MaxVisibleToasts = (int)v);

        store.RegisterApplier("inventory.default_sort", v => { InventoryPanel.DefaultSort = int.TryParse((string)v, out var index) ? index : 0; Tree?.CallGroup("lifeverse_inventory", "ApplyDefaults"); });
        store.RegisterApplier("inventory.subfolders", v => { InventoryPanel.DefaultSubfolders = (bool)v; Tree?.CallGroup("lifeverse_inventory", "ApplyDefaults"); });

        foreach (var id in new[] { "build.snap", "build.space", "build.move_step", "build.rotate_step", "build.scale_step" })
            store.RegisterApplier(id, _ => { Build.BuildEditorWindow.Defaults = ((string)store.Value("build.space") == "local", store.Get<bool>("build.snap"), store.Get<float>("build.move_step"), store.Get<float>("build.rotate_step"), store.Get<float>("build.scale_step")); Tree?.CallGroup("lifeverse_build_window", "ApplyDefaults"); });
        store.RegisterApplier("build.import_collision", v => { if (ModelImportDialog.ImportedCollisionShape != (string)v) ModelImportDialog.SaveImportedCollisionShape((string)v); });

        store.RegisterApplier("world.minimap_open", v => Viewer?.SetMiniMapOpen((bool)v));
        store.RegisterApplier("world.show_coords", v => Viewer?.SetCoordinatesVisible((bool)v));

        foreach (var id in new[] { "scripts.font_size", "scripts.indent", "scripts.autocomplete" })
            store.RegisterApplier(id, _ => { ScriptEditorWindow.EditorFontSize = store.Get<int>("scripts.font_size"); ScriptEditorWindow.EditorIndent = store.Get<int>("scripts.indent"); ScriptEditorWindow.EditorAutocomplete = store.Get<bool>("scripts.autocomplete"); Tree?.CallGroup("lifeverse_script_editor", "ApplyEditorSettings"); });

        store.RegisterApplier("network.auto_reconnect", v => NetworkManager.AutoReconnect = (bool)v);
        foreach (var id in InterfaceIds) store.RegisterApplier(id, _ => Viewer?.ApplyInterfaceFromStore(store));
        store.RegisterApplier("accessibility.reduce_motion", v => Accessibility.ReduceMotion = (bool)v);
    }

    // ---- the buttons ----------------------------------------------------------------------------------------------------------

    private static readonly string[] CacheFolders = ["user://asset_cache", "user://assets", "user://thumbs", "user://thumbs128", "user://avatar_models"];

    /// <summary>Deletes the files of a folder (and its subfolders); returns how many bytes that freed.</summary>
    public static long DeleteFolderContents(string path)
    {
        var absolute = ProjectSettings.GlobalizePath(path); if (!DirAccess.DirExistsAbsolute(absolute)) return 0;
        long freed = 0;
        foreach (var file in DirAccess.GetFilesAt(absolute)) { var full = $"{absolute}/{file}"; using (var f = Godot.FileAccess.Open(full, Godot.FileAccess.ModeFlags.Read)) freed += (long)(f?.GetLength() ?? 0); DirAccess.RemoveAbsolute(full); }
        foreach (var directory in DirAccess.GetDirectoriesAt(absolute)) { freed += DeleteFolderContents($"{path}/{directory}"); DirAccess.RemoveAbsolute($"{absolute}/{directory}"); }
        return freed;
    }

    /// <summary>The text of "Exportar diagnóstico": versions, system, graphics, performance and every setting. No credential, token or account data goes in it.</summary>
    public static string DiagnosticsText(SettingsStore store)
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR"); var text = new System.Text.StringBuilder();
        text.AppendLine("LifeVerse — diagnóstico"); text.AppendLine($"Gerado em: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine().AppendLine("[Cliente]"); text.AppendLine($"Versão: {AppManager.Version}"); text.AppendLine($"Godot: {Engine.GetVersionInfo()["string"].AsString()}"); text.AppendLine($"Protocolo: {LifeVerse.Shared.Networking.NetworkProtocol.Version}");
        text.AppendLine().AppendLine("[Sistema]"); text.AppendLine($"Sistema: {OS.GetName()} {OS.GetVersion()}"); text.AppendLine($"Processador: {OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)"); text.AppendLine($"Idioma do sistema: {OS.GetLocale()}");
        text.AppendLine().AppendLine("[Vídeo]"); text.AppendLine($"Placa: {RenderingServer.GetVideoAdapterName()}"); text.AppendLine($"Renderizador: {RenderingServer.GetCurrentRenderingMethod()} · {RenderingServer.GetCurrentRenderingDriverName()}");
        for (var i = 0; i < DisplayServer.GetScreenCount(); i++) { var size = DisplayServer.ScreenGetSize(i); text.AppendLine($"Monitor {i + 1}: {size.X}×{size.Y}, {DisplayServer.ScreenGetRefreshRate(i):0} Hz"); }
        var window = DisplayServer.WindowGetSize(); text.AppendLine($"Janela: {window.X}×{window.Y} ({DisplayOptions.CurrentMode()})");
        text.AppendLine().AppendLine("[Desempenho agora]");
        if (Tree?.Root is { } root) { var s = RenderStats.Read(root, Network); text.AppendLine(string.Format(pt, "FPS: {0:0} (média {1:0})", s.Fps, s.AverageFps)); text.AppendLine(string.Format(pt, "RAM: {0:N0} MB · VRAM: {1:N0} MB", s.RamMb, s.VramMb)); text.AppendLine(string.Format(pt, "Objetos: {0:N0} · Triângulos: {1:N0} · Draw calls: {2:N0}", s.Objects, s.Triangles, s.DrawCalls)); text.AppendLine($"Avatares: {s.AvatarsDrawn} desenhados de {s.AvatarsTotal}"); }
        text.AppendLine().AppendLine($"[Configurações — perfil {store.ActiveProfile}]");
        foreach (var definition in store.Definitions.Where(d => d.IsValueSetting && d.IsAvailable).OrderBy(d => d.Id, StringComparer.Ordinal))
            text.AppendLine($"{definition.Id} = {definition.Display(store.Value(definition.Id))}");
        return text.ToString();
    }

    private static NetworkManager? Network => Tree?.Root.GetNodeOrNull<NetworkManager>("NetworkManager");

    public static void RegisterActions()
    {
        SettingsActions.Register("advanced.open_data", w => OS.ShellOpen(ProjectSettings.GlobalizePath("user://")));
        SettingsActions.Register("advanced.open_logs", w => { var logs = ProjectSettings.GlobalizePath("user://logs"); OS.ShellOpen(DirAccess.DirExistsAbsolute(logs) ? logs : ProjectSettings.GlobalizePath("user://")); });
        SettingsActions.Register("advanced.clear_cache", w =>
        {
            var freed = CacheFolders.Sum(DeleteFolderContents);
            w.Notify("Cache limpo", freed > 0 ? string.Format(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), "{0:N1} MB liberados. Os recursos serão baixados de novo quando o jogo precisar.", freed / 1048576.0) : "Não havia nada para apagar.");
        });
        SettingsActions.Register("advanced.export_diagnostics", w =>
        {
            var path = $"user://diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write)) { if (file is null) throw new IOException("Não foi possível gravar o arquivo."); file.StoreString(DiagnosticsText(w.Store)); }
            w.Notify("Diagnóstico exportado", $"{ProjectSettings.GlobalizePath(path)}\n\nEle não tem senhas nem dados da conta.");
        });
        SettingsActions.Register("advanced.restore_all", w => { w.Store.ResetAll(); w.ApplyChanges(); });
        SettingsActions.Register("general.download_update", w => Tree?.Root.GetNodeOrNull<AppManager>("AppManager")?.Updates.OpenDownload());
        SettingsActions.Register("account.quit", w => Tree?.Quit());
        foreach (var id in new[] { "account.leave_island", "avatar.open_mirror", "avatar.open_editor", "chat.open_people", "interface.restore_layout", "interface.restore_toolbar", "interface.restore_all" })
        {
            var action = id;
            SettingsActions.Register(id, w => { if (Viewer is { } viewer) viewer.RunSettingsAction(action); else throw new InvalidOperationException("O mundo ainda não está aberto."); });
        }
    }
}
