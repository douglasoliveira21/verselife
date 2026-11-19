using System.Globalization;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Core;

/// <summary>
/// A configuração do servidor, lida das mesmas variáveis de ambiente do servidor Godot (o Supervisor não muda).
/// <see cref="TicketProtocolVersion"/> é a versão que a API grava nos bilhetes de entrada: enquanto a API emitir bilhetes do protocolo 11,
/// o servidor Unity (protocolo de rede 12) os aceita pela variável LIFEVERSE_TICKET_PROTOCOL, sem mudar a API.
/// </summary>
public sealed record IslandServerConfig(
    Guid InstanceId, Guid IslandId, string ApiBaseUrl, string ServiceKey, string Hostname,
    int GamePort, int HealthPort, int MaxPlayers, int SnapshotRate, float InterestRadius,
    int TicketProtocolVersion, int IdleShutdownMinutes, int TickRate, bool LoopbackOnly)
{
    public const int DefaultTickRate = 60;

    public static IslandServerConfig FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static IslandServerConfig From(Func<string, string?> get)
    {
        string Required(string name) => get(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"{name} is required.");
        int Int(string name, int fallback) => int.TryParse(get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var instanceId = Guid.TryParse(get("LIFEVERSE_INSTANCE_ID"), out var parsedInstance) ? parsedInstance : Guid.NewGuid();
        if (!Guid.TryParse(Required("LIFEVERSE_ISLAND_ID"), out var islandId)) throw new InvalidOperationException("LIFEVERSE_ISLAND_ID must be a UUID.");
        var gamePort = Int("LIFEVERSE_GAME_PORT", 7777);
        var healthPort = Int("LIFEVERSE_HEALTH_PORT", 7070);
        if (gamePort is < 0 or > 65535 || healthPort is < 0 or > 65535) throw new InvalidOperationException("Server ports must be between 0 and 65535.");
        var radius = float.TryParse(get("LIFEVERSE_INTEREST_RADIUS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius) ? parsedRadius : 100f;
        var serviceKey = Required("LIFEVERSE_SERVICE_KEY");
        if (serviceKey.Length < 32) throw new InvalidOperationException("LIFEVERSE_SERVICE_KEY must contain at least 32 characters.");
        return new IslandServerConfig(instanceId, islandId, Required("LIFEVERSE_API_URL").TrimEnd('/'), serviceKey,
            get("LIFEVERSE_HOSTNAME") ?? Environment.MachineName, gamePort, healthPort,
            Math.Clamp(Int("LIFEVERSE_MAX_PLAYERS", 64), 2, 256), Math.Clamp(Int("LIFEVERSE_SNAPSHOT_RATE", 20), 5, 30), Math.Clamp(radius, 10f, 500f),
            Int("LIFEVERSE_TICKET_PROTOCOL", NetworkProtocol.Version), Math.Clamp(Int("LIFEVERSE_IDLE_SHUTDOWN_MINUTES", 0), 0, 24 * 60),
            Math.Clamp(Int("LIFEVERSE_TICK_RATE", DefaultTickRate), 10, 120), get("LIFEVERSE_LOOPBACK_ONLY") == "1");
    }
}
