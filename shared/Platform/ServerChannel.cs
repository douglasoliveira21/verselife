using System.Text.Json;

namespace LifeVerse.Shared.Platform;

/// <summary>One person connected to an island server, as the server reports them.</summary>
public sealed record PlayerReport(Guid UserId, string Name, long ConnectedAtUnixMs, double X, double Y, double Z);

/// <summary>What an island server tells the API with every heartbeat: who is connected and how the server is doing.</summary>
public sealed record ServerReport(PlayerReport[] Players, double Fps = 0, double ProcessMs = 0, double MemoryMb = 0, long UptimeSeconds = 0, double TickRate = 0, int PhysicsBodies = 0, int PhysicsFixed = 0, int PhysicsMaxBodies = 0, double PhysicsMs = 0);

/// <summary>Something the API asks an island server to do. Kinds: <see cref="ServerCommandKinds.Kick"/>, <see cref="ServerCommandKinds.Notice"/> and <see cref="ServerCommandKinds.Shutdown"/> (save and stop cleanly; the island is started again by the API).</summary>
public sealed record ServerCommand(Guid Id, string Kind, Guid? UserId, string Text);

public static class ServerCommandKinds
{
    public const string Kick = "kick", Notice = "notice", Shutdown = "shutdown";
    public static bool Known(string kind) => kind is Kick or Notice or Shutdown;
}

/// <summary>The JSON of the channel, written in one place so both ends agree.</summary>
public static class ServerChannelJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The commands in a heartbeat answer ({"commands":[...]}); an empty or unreadable answer gives none, and unknown kinds are left out so an older server ignores newer commands.</summary>
    public static IReadOnlyList<ServerCommand> ParseCommands(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("commands", out var array) || array.ValueKind != JsonValueKind.Array) return [];
            var list = new List<ServerCommand>();
            foreach (var item in array.EnumerateArray())
            {
                var command = item.Deserialize<ServerCommand>(Options);
                if (command is not null && command.Id != Guid.Empty && ServerCommandKinds.Known(command.Kind)) list.Add(command);
            }
            return list;
        }
        catch (JsonException) { return []; }
    }
}
