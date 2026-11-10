namespace LifeVerse.Shared.Loading;

/// <summary>What the loading screen is for: entering the world after signing in, or travelling to another island.</summary>
public enum LoadingMode { Entry, Teleport }

public enum StepState { Pending, Running, Completed, Failed, Cancelled }

/// <summary>What a step is called and how much of the whole job it stands for. Weights are an estimate of the share of the work; they only decide how much a finished step moves the bar.</summary>
public sealed record StepDefinition(string Id, string Name, double Weight = 1, params string[] DependsOn);

/// <summary>One real operation of a loading: its state, how far it is when that can be measured, when it ran and what went wrong.</summary>
public sealed class LoadingStep
{
    public LoadingStep(StepDefinition definition) { Id = definition.Id; Name = definition.Name; Weight = Math.Max(0.0001, definition.Weight); DependsOn = definition.DependsOn; }

    public string Id { get; }
    public string Name { get; internal set; }
    public double Weight { get; }
    public IReadOnlyList<string> DependsOn { get; }
    public StepState State { get; internal set; } = StepState.Pending;
    /// <summary>Units finished (bytes, assets, objects, parts) and the units expected; Total is null while the amount of work is not known.</summary>
    public long Done { get; internal set; }
    public long? Total { get; internal set; }
    public DateTimeOffset? StartedAt { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public string? Error { get; internal set; }

    /// <summary>0 to 1 when the progress can be measured (a finished step is 1), null while it runs without a known size.</summary>
    public double? Fraction => State switch
    {
        StepState.Completed => 1d,
        StepState.Running => Total is > 0 ? Math.Clamp((double)Done / Total.Value, 0d, 1d) : null,
        _ => 0d
    };

    public bool Indeterminate => State == StepState.Running && Total is not > 0;
}
