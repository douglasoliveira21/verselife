namespace LifeVerse.IslandServer.Scripts;

/// <summary>O que o código de scripts da ilha (compilado de island-server/) lê da configuração: a API, a instância e a ilha. O servidor Godot usa a própria ServerConfiguration, que depende de variáveis de ambiente; aqui ela vem de <see cref="Core.IslandServerConfig"/>.</summary>
public sealed record ServerConfiguration(Guid InstanceId, Guid IslandId, string ApiBaseUrl)
{
    public static ServerConfiguration From(Core.IslandServerConfig config) => new(config.InstanceId, config.IslandId, config.ApiBaseUrl);
}
