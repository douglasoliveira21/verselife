using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>Where the server says to go when signing in: the island the person was on last, or the welcome island.</summary>
public sealed record StartInfo(Guid IslandId, string Name, string Reason, bool IsWelcome);

/// <summary>
/// The first island of a session. The server decides the island (last one, or the welcome island that every new avatar starts on) and, on that
/// island, the spot; this keeps on this computer only which way the avatar was facing, which the server does not track.
/// </summary>
public static class StartPlace
{
    /// <summary>Tests change this to a file of their own.</summary>
    public static string FacingPath { get; set; } = "user://last_place.json";

    public static StartInfo? Parse(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); var r = d.RootElement;
            if (!Guid.TryParse(r.GetProperty("islandId").GetString(), out var id) || id == Guid.Empty) return null;
            return new StartInfo(id, r.TryGetProperty("name", out var n) ? n.GetString() ?? "ilha" : "ilha", r.TryGetProperty("reason", out var why) ? why.GetString() ?? "welcome" : "welcome", r.TryGetProperty("isWelcome", out var w) && w.ValueKind == JsonValueKind.True);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>What to tell the person while they are taken there.</summary>
    public static string Message(StartInfo info) => info.Reason == "last" ? $"Voltando para {info.Name}, onde você estava." : $"Bem-vindo! Vamos para {info.Name}.";

    /// <summary>Keeps the direction the avatar is facing on this island.</summary>
    public static void Remember(Guid user, Guid island, float yaw)
    {
        if (user == Guid.Empty || island == Guid.Empty || !float.IsFinite(yaw)) return;
        try { using var file = Godot.FileAccess.Open(FacingPath, Godot.FileAccess.ModeFlags.Write); file?.StoreString(JsonSerializer.Serialize(new { userId = user, islandId = island, yaw })); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The direction the avatar was facing when it left this island, if it was this person on this island.</summary>
    public static float? FacingOn(Guid user, Guid island)
    {
        try
        {
            if (!Godot.FileAccess.FileExists(FacingPath)) return null;
            using var d = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(FacingPath)); var r = d.RootElement;
            return Guid.TryParse(r.GetProperty("userId").GetString(), out var u) && u == user && Guid.TryParse(r.GetProperty("islandId").GetString(), out var i) && i == island && r.GetProperty("yaw").TryGetSingle(out var yaw) && float.IsFinite(yaw) ? yaw : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}
