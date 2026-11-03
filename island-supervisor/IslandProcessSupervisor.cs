namespace LifeVerse.IslandSupervisor;

public sealed record PendingIsland(Guid Id, string Name);

/// <summary>Everything needed to start one island server process.</summary>
public sealed record IslandLaunch(Guid IslandId, string Name, int GamePort, int HealthPort, Guid InstanceId, IReadOnlyDictionary<string, string> Environment);

public interface IPendingIslands { Task<IReadOnlyList<PendingIsland>> GetAsync(CancellationToken ct); }

public interface IChildProcess
{
    bool HasExited { get; }
    int? ExitCode { get; }
    /// <summary>Asks the process to shut down cleanly (it saves its island), then forces it after the grace period.</summary>
    void Stop(TimeSpan grace);
}

public interface IProcessLauncher { IChildProcess Start(IslandLaunch launch); }

public sealed record SupervisorOptions(
    int PortStart = 7777, int MaxIslands = 8, int HealthPortStart = 7070,
    int MaxFailures = 3, TimeSpan? FailureBlock = null, IReadOnlyDictionary<string, string>? BaseEnvironment = null)
{
    public TimeSpan Block => FailureBlock ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Keeps one island server process running for every island players asked to enter. The API marks an island
/// "starting" when someone tries to join it without a server; the supervisor sees it, gives it a free port and
/// starts a process, which registers itself with the API like any island server. Processes that exit cleanly
/// (an idle island saving itself) free their port; processes that crash are retried a few times, then left alone
/// for a while so a broken island cannot loop forever.
/// </summary>
public sealed class IslandProcessSupervisor(SupervisorOptions options, IPendingIslands source, IProcessLauncher launcher, Func<DateTimeOffset> clock, Action<string> log)
{
    private sealed record Running(IslandLaunch Launch, IChildProcess Process);
    private sealed class Failures { public int Count; public DateTimeOffset BlockedUntil; }

    private readonly Dictionary<Guid, Running> _running = [];
    private readonly Dictionary<Guid, Failures> _failures = [];

    public IReadOnlyCollection<Guid> RunningIslands => _running.Keys;
    public IReadOnlyDictionary<Guid, int> Ports => _running.ToDictionary(pair => pair.Key, pair => pair.Value.Launch.GamePort);

    public async Task TickAsync(CancellationToken ct)
    {
        Reap();
        IReadOnlyList<PendingIsland> pending;
        try { pending = await source.GetAsync(ct); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException && !ct.IsCancellationRequested)
        { log($"could not read pending islands: {exception.Message}"); return; }

        foreach (var island in pending)
        {
            if (_running.ContainsKey(island.Id)) continue;
            if (_failures.TryGetValue(island.Id, out var failed) && failed.Count >= options.MaxFailures && clock() < failed.BlockedUntil) continue;
            var port = FreePort();
            if (port is null) { log($"no free port for island {island.Name} ({island.Id}); {options.MaxIslands} islands already running"); break; }
            Start(island, port.Value);
        }
    }

    public void StopAll(TimeSpan grace)
    {
        foreach (var running in _running.Values) running.Process.Stop(grace);
        _running.Clear();
    }

    private void Start(PendingIsland island, int gamePort)
    {
        var instanceId = Guid.NewGuid();
        var environment = new Dictionary<string, string>(options.BaseEnvironment ?? new Dictionary<string, string>())
        {
            ["LIFEVERSE_ISLAND_ID"] = island.Id.ToString(), ["LIFEVERSE_INSTANCE_ID"] = instanceId.ToString(),
            ["LIFEVERSE_GAME_PORT"] = gamePort.ToString(), ["LIFEVERSE_HEALTH_PORT"] = (options.HealthPortStart + gamePort - options.PortStart).ToString()
        };
        var launch = new IslandLaunch(island.Id, island.Name, gamePort, options.HealthPortStart + gamePort - options.PortStart, instanceId, environment);
        try
        {
            _running[island.Id] = new Running(launch, launcher.Start(launch));
            log($"started island {island.Name} ({island.Id}) on UDP {gamePort}");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log($"could not start island {island.Name}: {exception.Message}");
            RecordFailure(island.Id);
        }
    }

    private void Reap()
    {
        foreach (var (id, running) in _running.Where(pair => pair.Value.Process.HasExited).ToList())
        {
            _running.Remove(id);
            var code = running.Process.ExitCode ?? -1;
            if (code == 0) { _failures.Remove(id); log($"island {running.Launch.Name} stopped cleanly; UDP {running.Launch.GamePort} is free"); }
            else { log($"island {running.Launch.Name} exited with code {code}"); RecordFailure(id); }
        }
    }

    private void RecordFailure(Guid id)
    {
        if (!_failures.TryGetValue(id, out var failed)) _failures[id] = failed = new Failures();
        failed.Count++;
        if (failed.Count >= options.MaxFailures) { failed.BlockedUntil = clock() + options.Block; log($"island {id} failed {failed.Count} times; waiting {options.Block.TotalMinutes:0} min before trying again"); }
    }

    private int? FreePort()
    {
        var used = _running.Values.Select(r => r.Launch.GamePort).ToHashSet();
        for (var offset = 0; offset < options.MaxIslands; offset++) if (!used.Contains(options.PortStart + offset)) return options.PortStart + offset;
        return null;
    }
}
