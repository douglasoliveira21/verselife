namespace LifeVerse.IslandServer.Scripts;

/// <summary>O que o código de scripts da ilha (Scripting/) lê da configuração: a API, a instância e a ilha, vindas de <see cref="Core.IslandServerConfig"/>.</summary>
public sealed record ServerConfiguration(Guid InstanceId, Guid IslandId, string ApiBaseUrl)
{
    public static ServerConfiguration From(Core.IslandServerConfig config) => new(config.InstanceId, config.IslandId, config.ApiBaseUrl);
}
