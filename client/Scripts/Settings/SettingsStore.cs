using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>A quality preset: the selector setting and, for each preset, the values it gives to the settings it controls.</summary>
public sealed record SettingsPresetGroup(string SelectorId, string CustomId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> Presets)
{
    /// <summary>Maps a choice to the preset whose values it uses ("Automático" uses the level picked for this machine).</summary>
    public Func<string, string>? Resolve { get; init; }
}

public sealed record ApplyResult(IReadOnlyList<string> Changed, IReadOnlyList<string> RestartRequired);

/// <summary>
/// The values of every setting, per profile. A change is <b>pending</b> until it is applied ("Aplicar alterações") or dropped ("Descartar"); applying validates it,
/// saves the profile and runs the applier of the system the setting belongs to. Profiles are files under <c>user://config/perfis</c>; the active one is remembered.
/// No credential or token is ever stored here: only values of the catalog, and only of the types the catalog allows.
/// </summary>
public sealed class SettingsStore
{
    public const int SchemaVersion = 2;
    public const string DefaultProfile = "Padrão";
    public const int MaxProfiles = 30, MaxImportBytes = 64 * 1024;

    /// <summary>Settings that were renamed in a newer schema: (version that introduced the new id, old id, new id).</summary>
    public static readonly (int Version, string Old, string New)[] Renames = [(2, "gfx.vsync", "graphics.vsync"), (2, "gfx.fps", "graphics.fps_limit")];

    private readonly Dictionary<string, SettingDefinition> _definitions;
    private readonly SettingsPresetGroup? _presets;
    private readonly Dictionary<string, Action<object>> _appliers = [];
    private Dictionary<string, object> _values = [];
    private readonly Dictionary<string, object> _pending = [];

    public string Root { get; }
    public string ActiveProfile { get; private set; } = DefaultProfile;
    public IReadOnlyCollection<SettingDefinition> Definitions => _definitions.Values;
    public event Action<string>? ValueApplied;
    public event Action? PendingChanged;

    public SettingsStore(string root, IEnumerable<SettingDefinition> catalog, SettingsPresetGroup? presets = null)
    {
        Root = root.TrimEnd('/'); _definitions = catalog.ToDictionary(d => d.Id); _presets = presets;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Root));
        EnsureDefaultProfile();
        var remembered = ReadActiveName();
        Load(remembered is not null && ProfileExists(remembered) ? remembered : DefaultProfile);
    }

    public SettingDefinition? Definition(string id) => _definitions.GetValueOrDefault(id);

    public void RegisterApplier(string id, Action<object> applier) => _appliers[id] = applier;

    // ---- values -------------------------------------------------------------------------------------------------------------

    /// <summary>The value in use (what was applied last).</summary>
    public object Value(string id) => _values.TryGetValue(id, out var v) ? v : Require(id).Default;

    /// <summary>What the window shows: the pending change when there is one, else the value in use.</summary>
    public object Effective(string id) => _pending.TryGetValue(id, out var v) ? v : Value(id);

    public T Get<T>(string id) => (T)Convert.ChangeType(Value(id), typeof(T), CultureInfo.InvariantCulture);
    public T GetEffective<T>(string id) => (T)Convert.ChangeType(Effective(id), typeof(T), CultureInfo.InvariantCulture);

    public bool HasPending => _pending.Count > 0;
    public bool IsPending(string id) => _pending.ContainsKey(id);
    public IReadOnlyCollection<string> PendingIds => _pending.Keys;
    public bool IsDefault(string id) => Equals(Effective(id), Require(id).Default);

    private SettingDefinition Require(string id) => _definitions.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Configuração desconhecida: {id}");

    /// <summary>Proposes a new value. It is validated and limited; nothing happens in the game until <see cref="Apply"/>. Returns the value that was kept.</summary>
    public object Set(string id, object? raw)
    {
        var definition = Require(id);
        if (!definition.IsAvailable || !definition.IsValueSetting) return Effective(id);
        var value = definition.Normalize(raw);
        if (_presets is not null && id == _presets.SelectorId && (string)value != _presets.CustomId && _presets.Presets.TryGetValue(_presets.Resolve?.Invoke((string)value) ?? (string)value, out var preset))
        {
            Propose(id, value);
            foreach (var (controlled, controlledValue) in preset) if (_definitions.TryGetValue(controlled, out var c)) Propose(controlled, c.Normalize(controlledValue));
        }
        else
        {
            if (_presets is not null && definition.PresetControlled && !Equals(Effective(id), value) && (string)Effective(_presets.SelectorId) != _presets.CustomId) Propose(_presets.SelectorId, _presets.CustomId);
            Propose(id, value);
        }
        PendingChanged?.Invoke();
        return value;
    }

    private void Propose(string id, object value)
    {
        if (Equals(Value(id), value)) _pending.Remove(id); else _pending[id] = value;
    }

    /// <summary>Drops every pending change.</summary>
    public void Discard() { if (_pending.Count == 0) return; _pending.Clear(); PendingChanged?.Invoke(); }

    /// <summary>Proposes the default of every setting of a category (the window asks for confirmation first).</summary>
    public void ResetCategory(SettingCategory category)
    {
        foreach (var definition in _definitions.Values.Where(d => d.Category == category && d.IsAvailable && d.IsValueSetting)) Set(definition.Id, definition.Default);
    }

    public void ResetAll() { foreach (var definition in _definitions.Values.Where(d => d.IsAvailable && d.IsValueSetting)) Set(definition.Id, definition.Default); }

    /// <summary>Applies and saves what is pending: each value goes to the system it belongs to. Returns what changed and what needs a restart to take effect.</summary>
    public ApplyResult Apply()
    {
        var changed = new List<string>(); var restart = new List<string>();
        foreach (var (id, value) in _pending.ToList())
        {
            _values[id] = value; changed.Add(id);
            if (Require(id).RestartRequired) restart.Add(id);
        }
        _pending.Clear();
        Save();
        foreach (var id in changed) RunApplier(id);
        PendingChanged?.Invoke();
        return new ApplyResult(changed, restart);
    }

    /// <summary>Runs the applier of every setting with its current value (at start-up and when the profile changes).</summary>
    public void ApplyAll() { foreach (var id in _definitions.Keys) RunApplier(id); }

    private void RunApplier(string id)
    {
        if (_appliers.TryGetValue(id, out var applier)) { try { applier(Value(id)); } catch (Exception exception) { GD.PushWarning($"Configuração {id} não pôde ser aplicada: {exception.Message}"); } }
        ValueApplied?.Invoke(id);
    }

    /// <summary>Sets and applies at once (a system that changed a value on its own, such as the old preferences window, keeps the store in step).</summary>
    public void SetAndApply(string id, object? raw)
    {
        var definition = Require(id); if (!definition.IsAvailable) return;
        var value = definition.Normalize(raw); _pending.Remove(id);
        if (Equals(Value(id), value)) return;
        _values[id] = value; if (_presets is not null && id == _presets.SelectorId) AlignToPreset(); Save();
        ValueApplied?.Invoke(id);
    }

    // ---- profiles ------------------------------------------------------------------------------------------------------------

    public static string SafeFileName(string name)
    {
        var safe = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return safe.Length == 0 ? "perfil" : safe;
    }

    private string PathOf(string name) => $"{Root}/{SafeFileName(name)}.cfg";
    private string ActivePointer => $"{Root}/ativo.cfg";

    public static bool ValidName(string? name) => name is not null && name.Trim().Length is >= 1 and <= 40 && !name.Any(char.IsControl);

    public IReadOnlyList<string> Profiles()
    {
        var names = new List<string>();
        foreach (var file in DirAccess.GetFilesAt(Root))
        {
            if (!file.EndsWith(".cfg", StringComparison.Ordinal) || file == "ativo.cfg") continue;
            var cfg = new ConfigFile(); if (cfg.Load($"{Root}/{file}") != Error.Ok) continue;
            if (cfg.GetValue("meta", "name", "").AsString() is { Length: > 0 } name) names.Add(name);
        }
        return [.. names.OrderBy(n => n == DefaultProfile ? 0 : 1).ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase)];
    }

    public bool ProfileExists(string name) => Profiles().Any(n => string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase)) || Godot.FileAccess.FileExists(PathOf(name));

    private void EnsureDefaultProfile() { if (!Godot.FileAccess.FileExists(PathOf(DefaultProfile))) WriteProfile(DefaultProfile, Defaults(), legacyImported: false); }

    private Dictionary<string, object> Defaults() => _definitions.ToDictionary(p => p.Key, p => p.Value.Default);

    private string? ReadActiveName()
    {
        var cfg = new ConfigFile(); return cfg.Load(ActivePointer) == Error.Ok ? cfg.GetValue("state", "profile", "").AsString() is { Length: > 0 } name ? name : null : null;
    }

    private void WriteProfile(string name, IReadOnlyDictionary<string, object> values, bool legacyImported)
    {
        var cfg = new ConfigFile();
        cfg.SetValue("meta", "name", name); cfg.SetValue("meta", "schema_version", SchemaVersion); cfg.SetValue("meta", "legacy_imported", legacyImported);
        foreach (var (id, value) in values) if (_definitions.TryGetValue(id, out var written) && written.IsValueSetting && written.IsAvailable) cfg.SetValue("values", id, ToVariant(value));
        cfg.Save(PathOf(name));
    }

    private static Variant ToVariant(object value) => value switch { bool b => b, int i => i, float f => f, double d => d, string s => s, _ => value.ToString() ?? "" };

    private void Save()
    {
        var legacy = false; var keep = new Dictionary<string, Variant>(); var existing = new ConfigFile();
        if (existing.Load(PathOf(ActiveProfile)) == Error.Ok)
        {
            legacy = existing.GetValue("meta", "legacy_imported", false).AsBool();
            foreach (var key in existing.GetSectionKeys("meta")) if (key.StartsWith("imported_", StringComparison.Ordinal)) keep[key] = existing.GetValue("meta", key);       // which old sources were already brought in
        }
        WriteProfile(ActiveProfile, _values, legacy);
        if (keep.Count == 0) return;
        var written = new ConfigFile(); if (written.Load(PathOf(ActiveProfile)) != Error.Ok) return;
        foreach (var (key, value) in keep) written.SetValue("meta", key, value);
        written.Save(PathOf(ActiveProfile));
    }

    /// <summary>Loads a profile: values are validated, old ids renamed and unknown ones ignored; a file from an older schema is saved again in the new one.</summary>
    private void Load(string name)
    {
        var cfg = new ConfigFile(); var loaded = cfg.Load(PathOf(name)) == Error.Ok;
        var version = loaded ? cfg.GetValue("meta", "schema_version", 1).AsInt32() : SchemaVersion;
        var raw = new Dictionary<string, object>();
        if (loaded && cfg.HasSection("values")) foreach (var key in cfg.GetSectionKeys("values")) raw[key] = FromVariant(cfg.GetValue("values", key));
        foreach (var (introduced, old, renamed) in Renames) if (version < introduced && raw.Remove(old, out var moved)) raw.TryAdd(renamed, moved);
        _values = _definitions.ToDictionary(p => p.Key, p => raw.TryGetValue(p.Key, out var value) ? p.Value.Normalize(value) : p.Value.Default);
        ActiveProfile = loaded ? cfg.GetValue("meta", "name", name).AsString() : name;
        AlignToPreset();
        _pending.Clear();
        if (loaded && version < SchemaVersion) Save();
        var pointer = new ConfigFile(); pointer.SetValue("state", "profile", ActiveProfile); pointer.Save(ActivePointer);
    }

    /// <summary>A preset that is not Personalizada decides the values it controls: they are made to match it (a profile imported from an old file, or one saved with another build, stays consistent).</summary>
    private void AlignToPreset()
    {
        if (_presets is null || !_values.TryGetValue(_presets.SelectorId, out var selector)) return;
        var chosen = (string)selector; if (chosen == _presets.CustomId) return;
        if (_presets.Presets.TryGetValue(_presets.Resolve?.Invoke(chosen) ?? chosen, out var preset))
            foreach (var (id, value) in preset) if (_definitions.TryGetValue(id, out var definition)) _values[id] = definition.Normalize(value);
    }

    private static object FromVariant(Variant v) => v.VariantType switch
    {
        Variant.Type.Bool => v.AsBool(), Variant.Type.Int => v.AsInt64(), Variant.Type.Float => v.AsDouble(), Variant.Type.String => v.AsString(), _ => v.ToString()
    };

    /// <summary>Changes to another profile (pending changes are dropped) and applies all its values.</summary>
    public void Switch(string name)
    {
        if (!ProfileExists(name)) throw new ArgumentException("Esse perfil não existe.");
        Load(Profiles().FirstOrDefault(n => string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase)) ?? name);
        ApplyAll(); PendingChanged?.Invoke();
    }

    /// <summary>A new profile with the default values (or a copy of the current ones).</summary>
    public string Create(string name, bool copyCurrent = false)
    {
        name = CheckNewName(name);
        WriteProfile(name, copyCurrent ? _values : Defaults(), legacyImported: true);
        return name;
    }

    public string Duplicate(string source, string name)
    {
        name = CheckNewName(name);
        var cfg = new ConfigFile(); if (cfg.Load(PathOf(source)) != Error.Ok) throw new ArgumentException("Esse perfil não existe.");
        var values = new Dictionary<string, object>(); if (cfg.HasSection("values")) foreach (var key in cfg.GetSectionKeys("values")) if (_definitions.TryGetValue(key, out var d)) values[key] = d.Normalize(FromVariant(cfg.GetValue("values", key)));
        WriteProfile(name, values, legacyImported: true);
        return name;
    }

    public void Rename(string name, string newName)
    {
        if (string.Equals(name, DefaultProfile, StringComparison.CurrentCultureIgnoreCase)) throw new InvalidOperationException("O perfil Padrão não pode ser renomeado.");
        newName = CheckNewName(newName, ignore: name);
        var cfg = new ConfigFile(); if (cfg.Load(PathOf(name)) != Error.Ok) throw new ArgumentException("Esse perfil não existe.");
        cfg.SetValue("meta", "name", newName); cfg.Save(PathOf(newName));
        if (SafeFileName(newName) != SafeFileName(name)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(name)));
        if (string.Equals(ActiveProfile, name, StringComparison.CurrentCultureIgnoreCase)) { ActiveProfile = newName; var pointer = new ConfigFile(); pointer.SetValue("state", "profile", newName); pointer.Save(ActivePointer); }
    }

    public void Delete(string name)
    {
        if (string.Equals(name, DefaultProfile, StringComparison.CurrentCultureIgnoreCase)) throw new InvalidOperationException("O perfil Padrão não pode ser excluído.");
        if (!ProfileExists(name)) throw new ArgumentException("Esse perfil não existe.");
        var wasActive = string.Equals(ActiveProfile, name, StringComparison.CurrentCultureIgnoreCase);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(name)));
        if (wasActive) Switch(DefaultProfile);
    }

    private string CheckNewName(string name, string? ignore = null)
    {
        if (!ValidName(name)) throw new ArgumentException("O nome do perfil deve ter de 1 a 40 caracteres, sem caracteres de controle.");
        name = name.Trim();
        var clash = Profiles().Any(n => !string.Equals(n, ignore, StringComparison.CurrentCultureIgnoreCase) && (string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase) || SafeFileName(n) == SafeFileName(name)));
        if (clash) throw new InvalidOperationException("Já existe um perfil com esse nome.");
        if (ignore is null && Profiles().Count >= MaxProfiles) throw new InvalidOperationException($"O limite é de {MaxProfiles} perfis.");
        return name;
    }

    // ---- export / import -----------------------------------------------------------------------------------------------------

    /// <summary>Writes a profile as a JSON file the person can share. Only catalog values are written; there is nothing sensitive in it.</summary>
    public void Export(string name, string path)
    {
        var cfg = new ConfigFile(); if (cfg.Load(PathOf(name)) != Error.Ok) throw new ArgumentException("Esse perfil não existe.");
        var values = new SortedDictionary<string, object>();
        if (cfg.HasSection("values")) foreach (var key in cfg.GetSectionKeys("values")) if (_definitions.TryGetValue(key, out var d)) values[key] = d.Normalize(FromVariant(cfg.GetValue("values", key)));
        var json = JsonSerializer.Serialize(new { formato = "lifeverse-configuracoes", versao = SchemaVersion, nome = name, valores = values }, new JsonSerializerOptions { WriteIndented = true });
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write) ?? throw new IOException("Não foi possível gravar o arquivo.");
        file.StoreString(json);
    }

    /// <summary>Reads a profile file as a new profile (every value is checked; unknown ones are ignored). Returns the name given to it.</summary>
    public string Import(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read) ?? throw new IOException("Não foi possível abrir o arquivo.");
        if (file.GetLength() > MaxImportBytes) throw new InvalidDataException("O arquivo é grande demais para ser um perfil.");
        JsonElement root;
        try { using var document = JsonDocument.Parse(file.GetAsText()); root = document.RootElement.Clone(); } catch (JsonException) { throw new InvalidDataException("O arquivo não é um perfil de configurações válido."); }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("formato", out var format) || format.GetString() != "lifeverse-configuracoes" || !root.TryGetProperty("valores", out var valores) || valores.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("O arquivo não é um perfil de configurações do LifeVerse.");
        var version = root.TryGetProperty("versao", out var v) && v.TryGetInt32(out var parsed) ? parsed : 1;
        var raw = new Dictionary<string, object>();
        foreach (var property in valores.EnumerateObject())
            raw[property.Name] = property.Value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number => property.Value.GetDouble(), JsonValueKind.String => property.Value.GetString() ?? "", _ => "" };
        foreach (var (introduced, old, renamed) in Renames) if (version < introduced && raw.Remove(old, out var moved)) raw.TryAdd(renamed, moved);
        var values = _definitions.ToDictionary(p => p.Key, p => raw.TryGetValue(p.Key, out var value) ? p.Value.Normalize(value) : p.Value.Default);
        var wanted = root.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String && ValidName(n.GetString()) ? n.GetString()!.Trim() : "Importado";
        var name = wanted; for (var i = 2; Profiles().Any(p => string.Equals(p, name, StringComparison.CurrentCultureIgnoreCase) || SafeFileName(p) == SafeFileName(name)); i++) name = $"{wanted} ({i})";
        if (!ValidName(name)) name = "Importado";
        if (Profiles().Count >= MaxProfiles) throw new InvalidOperationException($"O limite é de {MaxProfiles} perfis.");
        WriteProfile(name, values, legacyImported: true);
        return name;
    }

    /// <summary>
    /// Brings the values of one more old source into the active profile, once (the settings that came after the first import: notifications, media, the interface of an account...).
    /// The source is named so it runs a single time per profile; what the person already set later is never overwritten.
    /// </summary>
    public bool ImportOnce(string source, IReadOnlyDictionary<string, object> values)
    {
        var file = new ConfigFile(); file.Load(PathOf(ActiveProfile));
        var flag = $"imported_{SafeFileName(source)}";
        if (file.GetValue("meta", flag, false).AsBool()) return false;
        foreach (var (id, raw) in values) if (_definitions.TryGetValue(id, out var d) && d.IsValueSetting && d.IsAvailable) _values[id] = d.Normalize(raw);
        AlignToPreset(); Save();
        var again = new ConfigFile(); again.Load(PathOf(ActiveProfile)); again.SetValue("meta", flag, true); again.Save(PathOf(ActiveProfile));
        return true;
    }

    // ---- legacy files --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The first time, brings the values of the old scattered files into the default profile (<c>settings.cfg</c>: frame rate limit, V-Sync, quality, volume;
    /// <c>viewer.cfg</c>: volume). It runs once; afterwards the profile is the one source.
    /// </summary>
    public bool ImportLegacy(string settingsPath, string viewerPath)
    {
        var profile = new ConfigFile(); profile.Load(PathOf(DefaultProfile));
        if (profile.GetValue("meta", "legacy_imported", false).AsBool()) return false;
        var settings = new ConfigFile(); var hasSettings = settings.Load(settingsPath) == Error.Ok;
        var viewer = new ConfigFile(); var hasViewer = viewer.Load(viewerPath) == Error.Ok;
        var imported = new Dictionary<string, object>();
        if (hasSettings)
        {
            if (settings.HasSectionKey("graphics", "fps")) imported["graphics.fps_limit"] = settings.GetValue("graphics", "fps").AsInt32();
            if (settings.HasSectionKey("graphics", "vsync")) imported["graphics.vsync"] = settings.GetValue("graphics", "vsync").AsBool();
            if (settings.HasSectionKey("graphics", "quality")) imported["graphics.preset"] = settings.GetValue("graphics", "quality").AsString();
            if (settings.HasSectionKey("audio", "master")) imported["audio.master"] = settings.GetValue("audio", "master").AsDouble();
        }
        if (hasViewer && viewer.HasSectionKey("audio", "volume")) imported["audio.master"] = viewer.GetValue("audio", "volume").AsDouble();     // the volume menu of the viewer is what the person last used
        var current = ActiveProfile == DefaultProfile ? _values : null;
        var values = current is null ? Defaults() : new Dictionary<string, object>(current);
        foreach (var (id, raw) in imported) if (_definitions.TryGetValue(id, out var d)) values[id] = d.Normalize(raw);
        WriteProfile(DefaultProfile, values, legacyImported: true);
        if (ActiveProfile == DefaultProfile) Load(DefaultProfile);
        return true;
    }
}
