using LifeVerse.IslandSupervisor;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class IslandSupervisorTests
{
    private sealed class FakeChild : IChildProcess
    {
        public bool HasExited { get; private set; }
        public int? ExitCode { get; private set; }
        public bool Stopped { get; private set; }
        public void Exit(int code) { HasExited = true; ExitCode = code; }
        public void Stop(TimeSpan grace) { Stopped = true; Exit(0); }
    }

    private sealed class FakeLauncher : IProcessLauncher
    {
        public List<(IslandLaunch Launch, FakeChild Child)> Started { get; } = [];
        public bool Fail { get; set; }
        public IChildProcess Start(IslandLaunch launch)
        {
            if (Fail) throw new InvalidOperationException("cannot start godot");
            var child = new FakeChild(); Started.Add((launch, child)); return child;
        }
    }

    private sealed class FakeSource : IPendingIslands
    {
        public List<PendingIsland> Pending { get; } = [];
        public bool Throw { get; set; }
        public Task<IReadOnlyList<PendingIsland>> GetAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Throw ? throw new HttpRequestException("api down") : Task.FromResult<IReadOnlyList<PendingIsland>>(Pending.ToList()); }
    }

    private static (IslandProcessSupervisor Supervisor, FakeSource Source, FakeLauncher Launcher, List<string> Log, Func<DateTimeOffset> Clock, Action<TimeSpan> Advance) Setup(SupervisorOptions? options = null)
    {
        var source = new FakeSource(); var launcher = new FakeLauncher(); var log = new List<string>();
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var supervisor = new IslandProcessSupervisor(options ?? new SupervisorOptions(BaseEnvironment: new Dictionary<string, string> { ["LIFEVERSE_API_URL"] = "http://api", ["LIFEVERSE_SERVICE_KEY"] = new string('k', 40) }), source, launcher, () => now, log.Add);
        return (supervisor, source, launcher, log, () => now, span => now += span);
    }

    [Fact]
    public async Task APendingIslandGetsAProcessWithItsOwnPortsAndSettings()
    {
        var (supervisor, source, launcher, _, _, _) = Setup();
        var island = new PendingIsland(Guid.NewGuid(), "Praia");
        source.Pending.Add(island);
        await supervisor.TickAsync(default);
        var (launch, _) = Assert.Single(launcher.Started);
        Assert.Equal(island.Id, launch.IslandId);
        Assert.Equal(7777, launch.GamePort); Assert.Equal(7070, launch.HealthPort);
        Assert.Equal(island.Id.ToString(), launch.Environment["LIFEVERSE_ISLAND_ID"]);
        Assert.Equal("7777", launch.Environment["LIFEVERSE_GAME_PORT"]); Assert.Equal("7070", launch.Environment["LIFEVERSE_HEALTH_PORT"]);
        Assert.Equal(launch.InstanceId.ToString(), launch.Environment["LIFEVERSE_INSTANCE_ID"]);
        Assert.Equal("http://api", launch.Environment["LIFEVERSE_API_URL"]);
    }

    [Fact]
    public async Task EachIslandGetsADifferentPortAndIsStartedOnlyOnce()
    {
        var (supervisor, source, launcher, _, _, _) = Setup();
        source.Pending.AddRange([new PendingIsland(Guid.NewGuid(), "A"), new PendingIsland(Guid.NewGuid(), "B"), new PendingIsland(Guid.NewGuid(), "C")]);
        await supervisor.TickAsync(default); await supervisor.TickAsync(default); await supervisor.TickAsync(default);
        Assert.Equal(3, launcher.Started.Count);
        Assert.Equal([7777, 7778, 7779], launcher.Started.Select(s => s.Launch.GamePort).ToArray());
        Assert.Equal([7070, 7071, 7072], launcher.Started.Select(s => s.Launch.HealthPort).ToArray());
        Assert.Equal(3, supervisor.RunningIslands.Count);
    }

    [Fact]
    public async Task WhenThePortsRunOutNoMoreIslandsStartUntilOneStops()
    {
        var (supervisor, source, launcher, log, _, _) = Setup(new SupervisorOptions(MaxIslands: 2));
        source.Pending.AddRange([new PendingIsland(Guid.NewGuid(), "A"), new PendingIsland(Guid.NewGuid(), "B"), new PendingIsland(Guid.NewGuid(), "C")]);
        await supervisor.TickAsync(default);
        Assert.Equal(2, launcher.Started.Count);
        Assert.Contains(log, line => line.Contains("no free port"));
        launcher.Started[0].Child.Exit(0);
        await supervisor.TickAsync(default);
        Assert.Equal(3, launcher.Started.Count);
        Assert.Equal(launcher.Started[0].Launch.GamePort, launcher.Started[2].Launch.GamePort); // the freed port is reused
    }

    [Fact]
    public async Task ACleanExitFreesThePortAndTheIslandStartsAgainOnlyWhenAskedAgain()
    {
        var (supervisor, source, launcher, _, _, _) = Setup();
        var island = new PendingIsland(Guid.NewGuid(), "Praia");
        source.Pending.Add(island);
        await supervisor.TickAsync(default);
        launcher.Started[0].Child.Exit(0);   // idle shutdown
        source.Pending.Clear();
        await supervisor.TickAsync(default);
        Assert.Empty(supervisor.RunningIslands); Assert.Single(launcher.Started);
        source.Pending.Add(island);          // someone tries to enter again
        await supervisor.TickAsync(default);
        Assert.Equal(2, launcher.Started.Count);
    }

    [Fact]
    public async Task ACrashingIslandIsRetriedThenLeftAloneForAWhile()
    {
        var (supervisor, source, launcher, log, _, advance) = Setup();
        source.Pending.Add(new PendingIsland(Guid.NewGuid(), "Quebrada"));
        for (var attempt = 0; attempt < 6; attempt++) { await supervisor.TickAsync(default); if (launcher.Started.Count > attempt) launcher.Started[attempt].Child.Exit(1); }
        Assert.Equal(3, launcher.Started.Count);
        Assert.Contains(log, line => line.Contains("failed 3 times"));
        advance(TimeSpan.FromMinutes(6));
        await supervisor.TickAsync(default);
        Assert.Equal(4, launcher.Started.Count);
    }

    [Fact]
    public async Task ABrokenInstallDoesNotCrashTheLoop()
    {
        var (supervisor, source, launcher, log, _, _) = Setup();
        launcher.Fail = true;
        source.Pending.Add(new PendingIsland(Guid.NewGuid(), "Praia"));
        await supervisor.TickAsync(default);
        Assert.Empty(supervisor.RunningIslands);
        Assert.Contains(log, line => line.Contains("could not start"));
    }

    [Fact]
    public async Task AnUnreachableApiIsLoggedAndRunningIslandsKeepRunning()
    {
        var (supervisor, source, launcher, log, _, _) = Setup();
        source.Pending.Add(new PendingIsland(Guid.NewGuid(), "Praia"));
        await supervisor.TickAsync(default);
        source.Throw = true;
        await supervisor.TickAsync(default);
        Assert.Single(supervisor.RunningIslands);
        Assert.False(launcher.Started[0].Child.HasExited);
        Assert.Contains(log, line => line.Contains("could not read pending islands"));
    }

    [Fact]
    public async Task StopAllAsksEveryIslandToShutDownCleanly()
    {
        var (supervisor, source, launcher, _, _, _) = Setup();
        source.Pending.AddRange([new PendingIsland(Guid.NewGuid(), "A"), new PendingIsland(Guid.NewGuid(), "B")]);
        await supervisor.TickAsync(default);
        supervisor.StopAll(TimeSpan.FromSeconds(15));
        Assert.All(launcher.Started, started => Assert.True(started.Child.Stopped));
        Assert.Empty(supervisor.RunningIslands);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        var (supervisor, source, _, _, _, _) = Setup();
        source.Throw = true;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        // A cancelled token must stop the loop (the exception propagates) instead of being logged as an API error.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => supervisor.TickAsync(cancelled.Token));
    }
}

public sealed class SystemProcessLauncherTests
{
    [Fact]
    public void RealProcesses_AreStartedTaggedAndTheirExitIsSeen()
    {
        // `dotnet --headless --path x` is not a valid command, so it prints an error and exits with a failure code:
        // enough to prove the launcher starts a real process, forwards its output with the island tag and reports the exit.
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var launcher = new SystemProcessLauncher(Environment.ProcessPath ?? "dotnet", "x", lines.Enqueue);
        var island = Guid.NewGuid();
        var child = launcher.Start(new IslandLaunch(island, "Teste", 7777, 7070, Guid.NewGuid(), new Dictionary<string, string> { ["LIFEVERSE_ISLAND_ID"] = island.ToString() }));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!child.HasExited && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Assert.True(child.HasExited, "the process never exited");
        Assert.NotEqual(0, child.ExitCode);
        Thread.Sleep(200);
        Assert.All(lines, line => Assert.StartsWith($"[{island.ToString("N")[..8]}] ", line));
    }

    [Fact]
    public void AMissingExecutableIsReportedNotThrownAsAnOsError()
    {
        var launcher = new SystemProcessLauncher("/definitely/not/godot", "x", _ => { });
        Assert.Throws<InvalidOperationException>(() => launcher.Start(new IslandLaunch(Guid.NewGuid(), "x", 7777, 7070, Guid.NewGuid(), new Dictionary<string, string>())));
    }
}
