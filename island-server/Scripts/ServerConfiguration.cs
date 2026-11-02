namespace LifeVerse.IslandServer.Scripts;

public sealed record ServerConfiguration(
    Guid InstanceId,
    Guid IslandId,
    string ApiBaseUrl,
    string ServiceKey,
    string Hostname,
    int GamePort,
    int HealthPort,
    int MaxPlayers,
    int SnapshotRate,
    float InterestRadius,
    int ProtocolVersion)
{
    public static ServerConfiguration FromEnvironment()
    {
        static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"{name} is required.");
        var instanceText = Environment.GetEnvironmentVariable("LIFEVERSE_INSTANCE_ID");
        var instanceId = Guid.TryParse(instanceText, out var parsedInstance) ? parsedInstance : Guid.NewGuid();
        if (!Guid.TryParse(Required("LIFEVERSE_ISLAND_ID"), out var islandId)) throw new InvalidOperationException("LIFEVERSE_ISLAND_ID must be a UUID.");
        var gamePort = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_GAME_PORT"), out var parsedPort) ? parsedPort : 7777;
        var healthPort = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_HEALTH_PORT"), out var parsedHealth) ? parsedHealth : 7070;
        var maxPlayers = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_MAX_PLAYERS"), out var parsedMax) ? parsedMax : 64;
        var snapshotRate = int.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_SNAPSHOT_RATE"), out var parsedRate) ? parsedRate : 20;
        var interestRadius = float.TryParse(Environment.GetEnvironmentVariable("LIFEVERSE_INTEREST_RADIUS"), System.Globalization.CultureInfo.InvariantCulture, out var parsedRadius) ? parsedRadius : 100f;
        if (gamePort is < 1 or > 65535 || healthPort is < 1 or > 65535) throw new InvalidOperationException("Server ports must be between 1 and 65535.");
        var serviceKey = Required("LIFEVERSE_SERVICE_KEY");
        if (serviceKey.Length < 32) throw new InvalidOperationException("LIFEVERSE_SERVICE_KEY must contain at least 32 characters.");
        return new ServerConfiguration(instanceId, islandId, Required("LIFEVERSE_API_URL").TrimEnd('/'), serviceKey,
            Environment.GetEnvironmentVariable("LIFEVERSE_HOSTNAME") ?? Environment.MachineName, gamePort, healthPort,
            Math.Clamp(maxPlayers, 2, 256), Math.Clamp(snapshotRate, 5, 30), Math.Clamp(interestRadius, 10, 500),
            LifeVerse.Shared.Networking.NetworkProtocol.Version);
    }
}
