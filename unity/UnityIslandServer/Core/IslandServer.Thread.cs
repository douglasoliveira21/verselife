namespace LifeVerse.IslandServer.Core;

/// <summary>
/// Everything that touches the island runs inside a step. The script glue (IslandScripting, the same source as the Godot server) is written for an engine that brings
/// the rest of an <c>await</c> back to the main thread; here the step does that: while it runs, this context sends what comes back to the inbox, and the next step runs it.
/// Without it a re-read of the objects changed the walls on another thread while the simulation was walking them.
/// </summary>
public sealed partial class IslandServer
{
    private sealed class StepContext(IslandServer server) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => server._inbox.Enqueue(() => callback(state));
        public override SynchronizationContext CreateCopy() => this;
    }

    private StepContext? _stepContext;

    /// <summary>Runs <paramref name="step"/> as the island's own thread: what it starts and awaits comes back inside a later step.</summary>
    private void AsStep(Action step)
    {
        var before = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(_stepContext ??= new StepContext(this));
        try { step(); }
        finally { SynchronizationContext.SetSynchronizationContext(before); }
    }

    /// <summary>Starts <paramref name="work"/> inside the next step and finishes when it does; gives up when the server is stopping (there may be no next step).</summary>
    private async Task OnStepAsync(Func<Task> work)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inbox.Enqueue(async () =>
        {
            try { await work(); done.TrySetResult(true); }
            catch (Exception exception) { done.TrySetException(exception); }
        });
        using var stop = _cts.Token.Register(() => done.TrySetCanceled());
        await done.Task.ConfigureAwait(false);
    }
}
