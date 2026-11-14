using Godot;

namespace LifeVerse.Client.Scripts.Interface;

/// <summary>
/// The interface preferences, kept <b>per account</b> in <c>user://interface_{account}.cfg</c> (two people on one computer do not share a layout).
/// The first time an account opens the game, the scale and tutorial choices of the old per-machine <c>viewer.cfg</c> are copied in, so nobody loses them.
/// </summary>
public sealed class InterfaceSettings
{
    public const string LegacyPath = "user://viewer.cfg";
    public const float DefaultScale = .8f;

    private readonly ConfigFile _cfg = new();
    public string Path { get; }

    public bool AutoScale { get; set; } = true;
    public float Scale { get; set; } = DefaultScale;
    /// <summary>"full" | "compact" | "immersive" (the modes arrive in a later stage; the choice is stored already).</summary>
    public string Mode { get; set; } = "full";
    /// <summary>Opacity of the windows, 0.3–1.</summary>
    public float WindowOpacity { get; set; } = .92f;

    /// <summary>Bottom toolbar: icons only, the buttons the person hid, and their order (ids of <see cref="ToolbarLayout"/>).</summary>
    public bool CompactToolbar { get; set; }
    public List<string> ToolbarHidden { get; } = [];
    public List<string> ToolbarOrder { get; } = [];
    /// <summary>The shortcut bar on the left edge: open or collapsed.</summary>
    public bool SideToolbarOpen { get; set; } = true;
    /// <summary>Opacity of the chat panel and of the mini-map window (0.3-1) and the people met recently.</summary>
    public float ChatOpacity { get; set; } = .8f;
    public float MiniMapOpacity { get; set; } = .92f;
    public List<string> RecentPeople { get; } = [];
    /// <summary>Favorite places (islands) of the account.</summary>
    public List<(Guid Id, string Name)> Favorites { get; } = [];

    /// <summary>The position and size the person gave each window (by window id), in interface units.</summary>
    public Dictionary<string, Rect2> Windows { get; } = [];
    public const int MaxSavedWindows = 200;

    private InterfaceSettings(string path) => Path = path;

    public static string PathFor(string? account)
    {
        var safe = new string((account ?? "").Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return string.IsNullOrEmpty(safe) ? "user://interface_guest.cfg" : $"user://interface_{safe}.cfg";
    }

    /// <summary>Loads the account's file; if it does not exist yet, starts from the legacy one (when <paramref name="legacy"/> is given or <c>viewer.cfg</c> exists).</summary>
    public static InterfaceSettings Load(string? account, string? pathOverride = null, string? legacyPath = LegacyPath)
    {
        var settings = new InterfaceSettings(pathOverride ?? PathFor(account));
        if (settings._cfg.Load(settings.Path) == Error.Ok) { settings.Read(); return settings; }
        var old = new ConfigFile();
        if (legacyPath is not null && old.Load(legacyPath) == Error.Ok)
        {
            // the old file had one fixed scale: keep it as the manual choice
            settings.Scale = (float)(double)old.GetValue("ui", "scale", (double)DefaultScale); settings.AutoScale = false;
            settings.Scale = Math.Clamp(settings.Scale, InterfaceScale.Min, InterfaceScale.Max);
        }
        settings.Save();
        return settings;
    }

    private void Read()
    {
        AutoScale = (bool)_cfg.GetValue("scale", "auto", true);
        Scale = Math.Clamp((float)(double)_cfg.GetValue("scale", "manual", (double)DefaultScale), InterfaceScale.Min, InterfaceScale.Max);
        Mode = (string)_cfg.GetValue("layout", "mode", "full"); if (Mode is not ("full" or "compact" or "immersive")) Mode = "full";
        WindowOpacity = Math.Clamp((float)(double)_cfg.GetValue("theme", "window_opacity", .92), .3f, 1f);
        CompactToolbar = (bool)_cfg.GetValue("toolbar", "compact", false); SideToolbarOpen = (bool)_cfg.GetValue("toolbar", "side_open", true);
        ToolbarHidden.Clear(); ToolbarHidden.AddRange(Split((string)_cfg.GetValue("toolbar", "hidden", "")));
        ToolbarOrder.Clear(); ToolbarOrder.AddRange(Split((string)_cfg.GetValue("toolbar", "order", "")));
        ChatOpacity = Math.Clamp((float)(double)_cfg.GetValue("chat", "opacity", .8), .3f, 1f); MiniMapOpacity = Math.Clamp((float)(double)_cfg.GetValue("minimap", "opacity", .92), .3f, 1f);
        RecentPeople.Clear(); RecentPeople.AddRange(((string)_cfg.GetValue("people", "recent", "")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(20));
        Favorites.Clear();
        foreach (var line in ((string)_cfg.GetValue("places", "favorites", "")).Split('\n', StringSplitOptions.RemoveEmptyEntries)) { var cut = line.IndexOf('|'); if (cut > 0 && Guid.TryParse(line[..cut], out var id) && Favorites.Count < 50) Favorites.Add((id, line[(cut + 1)..])); }
        Windows.Clear();
        if (_cfg.HasSection("windows")) foreach (var key in _cfg.GetSectionKeys("windows")) if (_cfg.GetValue("windows", key).VariantType == Variant.Type.Rect2 && Windows.Count < MaxSavedWindows) Windows[key] = (Rect2)_cfg.GetValue("windows", key);
    }

    private static string[] Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public Error Save()
    {
        _cfg.SetValue("scale", "auto", AutoScale); _cfg.SetValue("scale", "manual", (double)Scale);
        _cfg.SetValue("layout", "mode", Mode); _cfg.SetValue("theme", "window_opacity", (double)WindowOpacity);
        _cfg.SetValue("chat", "opacity", (double)ChatOpacity); _cfg.SetValue("minimap", "opacity", (double)MiniMapOpacity); _cfg.SetValue("people", "recent", string.Join('\n', RecentPeople.Take(20)));
        _cfg.SetValue("toolbar", "compact", CompactToolbar); _cfg.SetValue("toolbar", "side_open", SideToolbarOpen);
        _cfg.SetValue("toolbar", "hidden", string.Join(',', ToolbarHidden)); _cfg.SetValue("toolbar", "order", string.Join(',', ToolbarOrder));
        _cfg.SetValue("places", "favorites", string.Join('\n', Favorites.Select(f => $"{f.Id}|{f.Name.Replace('\n', ' ')}")));
        if (_cfg.HasSection("windows")) _cfg.EraseSection("windows");
        foreach (var (id, rect) in Windows) _cfg.SetValue("windows", id, rect);
        return _cfg.Save(Path);
    }
}
