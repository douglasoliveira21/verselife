using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.Notifications;

/// <summary>
/// Which kinds of notification pop up as a toast. It only changes the toasts on this computer: everything still arrives in the bell, which keeps the full history.
/// Kept in <c>user://notification_prefs.cfg</c>; a missing or broken file means "show everything".
/// </summary>
public static class NotificationPrefs
{
    public static readonly (string Id, string Text)[] Categories = [("grupos", "Grupos (cargos, avisos, pedidos)"), ("convites", "Convites"), ("sistema", "Sistema, amigos e mensagens")];
    public static string Path { get; set; } = "user://notification_prefs.cfg";

    /// <summary>The category of a live event, from its <c>type</c>.</summary>
    public static string CategoryOfType(string type) => type == "group_invite" ? "convites" : type.StartsWith("group_", StringComparison.Ordinal) ? "grupos" : "sistema";

    public static bool ToastsOn(string category)
    {
        try { var file = new ConfigFile(); return file.Load(Path) != Error.Ok || (bool)file.GetValue("toasts", category, true); }
        catch (Exception) { return true; }
    }

    public static void SetToasts(string category, bool on)
    {
        try { var file = new ConfigFile(); file.Load(Path); file.SetValue("toasts", category, on); file.Save(Path); } catch (Exception) { /* a failed save only means the choice is not remembered */ }
    }

    /// <summary>Whether the live event (raw JSON from the socket) may be shown as a toast.</summary>
    public static bool AllowsToast(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || ToastsOn(CategoryOfType(type.GetString() ?? ""));
        }
        catch (JsonException) { return true; }
    }
}
