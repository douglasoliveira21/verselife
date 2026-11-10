using LifeVerse.Shared.Loading;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class LoadingManagerTests
{
    private static LoadingManager Started(out List<string> log)
    {
        var events = new List<string>(); var manager = new LoadingManager(() => new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        manager.LoadingStarted += () => events.Add("started"); manager.LoadingStepChanged += s => events.Add($"step:{s.Id}:{s.State}"); manager.LoadingStepCompleted += s => events.Add($"done:{s.Id}");
        manager.LoadingFailed += (s, e) => events.Add($"failed:{s.Id}:{e}"); manager.LoadingCompleted += () => events.Add("completed"); manager.LoadingCancelled += () => events.Add("cancelled");
        manager.Start(LoadingMode.Entry, "Entrando no LifeVerse...", "Ilha", [new("a", "Sessão", 1), new("b", "Servidor", 2, "a"), new("c", "Ambiente", 2, "b"), new("d", "Pronto", 1, "c")]);
        log = events; return manager;
    }

    [Fact]
    public void ProgressComesFromRealStepsAndNeverReachesOneBeforeTheLastCompletes()
    {
        var manager = Started(out var log);
        Assert.Equal(0, manager.Progress); Assert.True(manager.Begin("a")); Assert.True(manager.Complete("a"));
        Assert.Equal(1d / 6, manager.Progress, 6); // weight 1 of 6
        manager.Begin("b"); Assert.True(manager.Indeterminate); Assert.Equal(1d / 6, manager.Progress, 6); // a running step of unknown size adds nothing invented
        manager.Report("b", 1, 4); Assert.False(manager.Indeterminate); Assert.Equal((1 + 2 * .25) / 6, manager.Progress, 6);
        manager.Complete("b"); manager.Complete("c"); Assert.True(manager.Progress < 1); Assert.False(manager.Finished);
        manager.Complete("d"); Assert.Equal(1, manager.Progress); Assert.True(manager.Succeeded); Assert.Equal("completed", log[^1]);
    }

    [Fact]
    public void AStepCannotStartBeforeItsDependenciesFinish()
    {
        var manager = Started(out _);
        Assert.False(manager.Begin("c")); Assert.False(manager.Complete("c")); manager.Report("c", 5, 10); Assert.Equal(StepState.Pending, manager.Step("c")!.State);
        manager.Complete("a"); Assert.False(manager.Begin("c")); Assert.True(manager.Begin("b")); Assert.True(manager.Begin("b")); // starting a running step is fine
    }

    [Fact]
    public void AFailureStopsTheRestAndRetryRunsAgainFromWhereItStopped()
    {
        var manager = Started(out var log);
        manager.Complete("a"); manager.Begin("b"); manager.Fail("b", "O servidor não respondeu.");
        Assert.True(manager.Failed); Assert.Equal("b", manager.FailedStep!.Id); Assert.Equal(StepState.Cancelled, manager.Step("c")!.State); Assert.Contains("failed:b:O servidor não respondeu.", log);
        Assert.False(manager.Complete("c")); // nothing moves after a failure
        Assert.True(manager.Retry()); Assert.Equal(StepState.Completed, manager.Step("a")!.State); Assert.Equal(StepState.Pending, manager.Step("b")!.State); Assert.True(manager.Active);
        Assert.True(manager.Begin("b")); manager.Complete("b"); manager.Complete("c"); manager.Complete("d"); Assert.True(manager.Succeeded);
        Assert.False(manager.Retry()); // nothing to retry after success
    }

    [Fact]
    public void CancelMarksWhatDidNotFinish()
    {
        var manager = Started(out var log); manager.Complete("a"); manager.Begin("b"); manager.Cancel();
        Assert.True(manager.Cancelled); Assert.Equal(StepState.Completed, manager.Step("a")!.State); Assert.Equal(StepState.Cancelled, manager.Step("b")!.State); Assert.Contains("cancelled", log); Assert.False(manager.Complete("b"));
    }

    [Fact]
    public void StepsRecordWhenTheyRanAndFinishingTwiceIsHarmless()
    {
        var manager = Started(out var log); manager.Begin("a"); manager.Complete("a"); manager.Complete("a");
        var step = manager.Step("a")!; Assert.NotNull(step.StartedAt); Assert.NotNull(step.CompletedAt); Assert.Single(log, "done:a");
        manager.Begin("b"); manager.Report("b", 3, 3); Assert.Equal(1d, manager.Step("b")!.Fraction);
    }

    [Fact]
    public void BadDefinitionsAreRefused()
    {
        var manager = new LoadingManager();
        Assert.Throws<ArgumentException>(() => manager.Start(LoadingMode.Entry, "t", "d", []));
        Assert.Throws<ArgumentException>(() => manager.Start(LoadingMode.Entry, "t", "d", [new("a", "A"), new("a", "B")]));
        Assert.Throws<ArgumentException>(() => manager.Start(LoadingMode.Entry, "t", "d", [new("a", "A", 1, "b"), new("b", "B")])); // a dependency must come first
        Assert.Throws<ArgumentException>(() => manager.Start(LoadingMode.Teleport, "t", "d", [new("a", "A", 1, "a")]));
    }

    [Fact]
    public void WorksFromSeveralThreads()
    {
        var manager = new LoadingManager(); manager.Start(LoadingMode.Entry, "t", "d", [new("a", "A", 1), new("b", "B", 1)]);
        Parallel.For(0, 200, i => { manager.Report("a", i, 200); _ = manager.Progress; _ = manager.Indeterminate; });
        Assert.True(manager.Progress < 1); manager.Complete("a"); manager.Complete("b"); Assert.Equal(1, manager.Progress);
    }
}

public sealed class TravellerReleaseTests
{
    [Fact]
    public void OnlySomeoneWhoJoinedBeforeTheArrivalIsLetGo()
    {
        var arrival = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.True(TravellerRelease.ShouldDrop(DateTimeOffset.FromUnixTimeMilliseconds(arrival - 5000), arrival)); // was here when the trip arrived elsewhere
        Assert.True(TravellerRelease.ShouldDrop(DateTimeOffset.FromUnixTimeMilliseconds(arrival), arrival));
        Assert.False(TravellerRelease.ShouldDrop(DateTimeOffset.FromUnixTimeMilliseconds(arrival + 1), arrival)); // came back afterwards: a new visit
    }
}
