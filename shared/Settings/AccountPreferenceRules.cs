using System.Globalization;
using System.Text.Json;

namespace LifeVerse.Shared.Settings;

/// <summary>The kind of value an account preference holds.</summary>
public enum PreferenceKind { Bool, Int, Float, Choice, Key }

/// <summary>What one synced setting accepts. <c>Min</c>/<c>Max</c> bound numbers; <c>Choices</c> lists the allowed texts of a choice.</summary>
public sealed record PreferenceRule(string Id, PreferenceKind Kind, double Min = 0, double Max = 1, IReadOnlyList<string>? Choices = null);

public sealed record PreferenceError(string Id, string Message);

/// <summary>The outcome of checking a set of values: the cleaned values (JSON-ready: bool, long, double, string) and what was wrong.</summary>
public sealed record PreferenceCheck(IReadOnlyDictionary<string, object?> Values, IReadOnlyList<PreferenceError> Errors) { public bool Ok => Errors.Count == 0; }

/// <summary>
/// Which settings follow the account to every device, and what each may hold. Settings of one computer (graphics, window, sound devices, performance limits, interface scale)
/// are not here on purpose. The server checks every value against these rules; the client uses the same list to know what to send. Nothing sensitive can be stored: only these keys, only these shapes.
/// </summary>
public static class AccountPreferenceRules
{
    public const int MaxKeys = 160, MaxJsonBytes = 16 * 1024, MaxKeyLength = 24;
    public static readonly string[] ControlActions = ["move_forward", "move_back", "move_left", "move_right", "strafe_left", "strafe_right", "jump", "run", "fly_toggle", "descend", "interact", "emote_wave", "emote_dance", "cursor_toggle", "first_person", "camera_mode", "camera_reset", "build_mode", "terrain_mode", "voice_talk"];

    private static PreferenceRule B(string id) => new(id, PreferenceKind.Bool);
    private static PreferenceRule I(string id, int min, int max) => new(id, PreferenceKind.Int, min, max);
    private static PreferenceRule F(string id, double min, double max) => new(id, PreferenceKind.Float, min, max);
    private static PreferenceRule C(string id, params string[] choices) => new(id, PreferenceKind.Choice, Choices: choices);

    public static readonly IReadOnlyList<PreferenceRule> All =
    [
        C("interface.mode", "full", "compact", "immersive"), I("interface.window_opacity", 30, 100), I("interface.map_opacity", 30, 100), B("interface.compact_toolbar"), B("interface.side_bar"),
        I("chat.opacity", 30, 100), I("chat.font_size", 12, 28), B("chat.show_time"), I("chat.visible_seconds", 3, 30),
        B("notifications.toast_grupos"), B("notifications.toast_convites"), B("notifications.toast_sistema"), B("notifications.dnd"), I("notifications.toast_seconds", 2, 15), I("notifications.max_toasts", 1, 6),
        B("avatar.show_names"), B("avatar.show_own_name"), B("avatar.show_titles"), B("avatar.footsteps"), B("avatar.default_animations"),
        F("controls.mouse_sensitivity", .25, 3), B("controls.invert_y"), B("controls.always_run"), B("controls.double_tap_run"), B("controls.hold_to_fly"),
        C("camera.default_mode", "third", "first"), I("camera.fov", 50, 110), F("camera.default_distance", 1.5, 12), F("camera.zoom_step", .25, 2), F("camera.height", -.5, 1), F("camera.shoulder", -1, 1), F("camera.smoothing", .5, 10),
        B("camera.collision"), C("camera.photo_format", "png", "jpg"), B("camera.photo_include_ui"),
        B("build.snap"), C("build.space", "world", "local"), F("build.move_step", .1, 5), F("build.rotate_step", 5, 90), F("build.scale_step", .05, 1), C("build.import_collision", "none", "box", "convex", "triangle"),
        I("scripts.font_size", 10, 32), I("scripts.indent", 2, 8), B("scripts.autocomplete"),
        C("inventory.default_sort", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9"), B("inventory.subfolders"),
        B("accessibility.reduce_motion"), B("privacy.external_media"), B("world.minimap_open"), B("world.show_coords"),
        B("voice.indicators"), B("voice.spatial"), I("voice.hearing_distance", 5, 25),
        .. ControlActions.SelectMany(action => new[] { new PreferenceRule($"controls.{action}.1", PreferenceKind.Key), new PreferenceRule($"controls.{action}.2", PreferenceKind.Key) })
    ];

    private static readonly Dictionary<string, PreferenceRule> ById = All.ToDictionary(r => r.Id);

    public static PreferenceRule? Find(string id) => ById.GetValueOrDefault(id);
    public static bool IsSynced(string id) => ById.ContainsKey(id);

    /// <summary>A key name: engine names such as W, Up, Space, Shift, Ctrl, Alt, F5, Kp 4. Letters, digits, spaces and a few signs; empty is allowed (no key). Reserved keys are refused.</summary>
    public static bool ValidKeyName(string text)
    {
        if (text.Length > MaxKeyLength) return false;
        foreach (var c in text) if (!(char.IsAsciiLetterOrDigit(c) || c is ' ' or '+' or '-' or '_' or '/' or '.' or ',' or ';' or '[' or ']' or '\'' or '=' or '`' or '\\')) return false;
        return !new[] { "Escape", "Enter", "Kp Enter", "F10", "Delete" }.Contains(text, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Checks one value from JSON. Returns the cleaned value (numbers inside the range are kept, outside are an error) or an error text.</summary>
    public static (object? Value, string? Error) CheckValue(PreferenceRule rule, JsonElement element)
    {
        switch (rule.Kind)
        {
            case PreferenceKind.Bool:
                return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? (element.GetBoolean(), null) : (null, "deve ser verdadeiro ou falso");
            case PreferenceKind.Int:
            case PreferenceKind.Float:
            {
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var number) || !double.IsFinite(number)) return (null, "deve ser um número");
                if (number < rule.Min - 1e-9 || number > rule.Max + 1e-9) return (null, string.Create(CultureInfo.InvariantCulture, $"deve estar entre {rule.Min} e {rule.Max}"));
                if (rule.Kind == PreferenceKind.Int) return number != Math.Floor(number) ? (null, "deve ser um número inteiro") : ((long)number, null);
                return (Math.Round(Math.Clamp(number, rule.Min, rule.Max), 4), null);
            }
            case PreferenceKind.Choice:
                return element.ValueKind == JsonValueKind.String && element.GetString() is { } choice && rule.Choices!.Contains(choice) ? (choice, null) : (null, $"deve ser um de: {string.Join(", ", rule.Choices!)}");
            default:
                return element.ValueKind == JsonValueKind.String && element.GetString() is { } key && ValidKeyName(key.Trim()) ? (key.Trim(), null) : (null, "não é uma tecla válida ou é reservada");
        }
    }

    /// <summary>Checks a set of values (a patch): unknown keys, wrong types and values out of range are errors; a JSON null means "forget this one" and stays as null.</summary>
    public static PreferenceCheck Check(IReadOnlyDictionary<string, JsonElement> values)
    {
        var clean = new Dictionary<string, object?>(); var errors = new List<PreferenceError>();
        if (values.Count > MaxKeys) errors.Add(new("", $"São no máximo {MaxKeys} preferências por vez."));
        foreach (var (id, element) in values.Take(MaxKeys))
        {
            if (!ById.TryGetValue(id, out var rule)) { errors.Add(new(id.Length > 40 ? id[..40] : id, "esta preferência não existe ou não sincroniza com a conta")); continue; }
            if (element.ValueKind == JsonValueKind.Null) { clean[id] = null; continue; }
            var (value, error) = CheckValue(rule, element);
            if (error is not null) errors.Add(new(id, error)); else clean[id] = value;
        }
        return new PreferenceCheck(clean, errors);
    }
}
