namespace LifeVerse.Api.Modules.Worlds;

public sealed class WorldManagerHostedService(IServiceScopeFactory scopes, ILogger<WorldManagerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorldManager>().SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "World Manager sweep failed"); }
        }
    }
}
