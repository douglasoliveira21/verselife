namespace LifeVerse.IslandServer.Scripts;

public sealed record ServerConfiguration(
    Guid InstanceId,
    Guid IslandId,
    string ApiBaseUrl,
    string ServiceKey,
    string Hostname,
    int GamePort,
    int HealthPort)
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
        if (gamePort is < 1 or > 65535 || healthPort is < 1 or > 65535) throw new InvalidOperationException("Server ports must be between 1 and 65535.");
        var serviceKey = Required("LIFEVERSE_SERVICE_KEY");
        if (serviceKey.Length < 32) throw new InvalidOperationException("LIFEVERSE_SERVICE_KEY must contain at least 32 characters.");
        return new ServerConfiguration(instanceId, islandId, Required("LIFEVERSE_API_URL").TrimEnd('/'), serviceKey,
            Environment.GetEnvironmentVariable("LIFEVERSE_HOSTNAME") ?? Environment.MachineName, gamePort, healthPort);
    }
}
