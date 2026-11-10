namespace LifeVerse.Shared.Loading;

/// <summary>
/// Runs one loading (entering the world, travelling to an island) as a list of real steps. Whoever does the work reports it here; the screen only listens.
/// The overall progress is the weighted share of the work finished, built from steps that really completed and from measured progress (bytes, objects…).
/// It is never 1 until every step has completed, and a step whose size is not known shows as indeterminate instead of a made-up percentage.
/// </summary>
public sealed class LoadingManager
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<LoadingStep> _steps = [];

    public LoadingManager(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public event Action? LoadingStarted;
    public event Action<LoadingStep>? LoadingStepChanged;
    public event Action<double>? LoadingProgressChanged;
    public event Action<LoadingStep>? LoadingStepCompleted;
    public event Action<LoadingStep, string>? LoadingFailed;
    public event Action? LoadingCompleted;
    public event Action? LoadingCancelled;

    public LoadingMode Mode { get; private set; } = LoadingMode.Entry;
    public string Title { get; private set; } = string.Empty;
    public string Destination { get; private set; } = string.Empty;
    public bool Active { get; private set; }
    public bool Succeeded { get; private set; }
    public LoadingStep? FailedStep { get; private set; }
    public string? FailureMessage { get; private set; }
    public IReadOnlyList<LoadingStep> Steps { get { lock (_gate) return [.. _steps]; } }

    public bool Failed => FailedStep is not null;
    public bool Cancelled { get; private set; }
    public bool Finished => Succeeded || Failed || Cancelled;

    public LoadingStep? Step(string id) { lock (_gate) return _steps.FirstOrDefault(s => s.Id == id); }

    /// <summary>The step being worked on, or the next one waiting.</summary>
    public LoadingStep? Current { get { lock (_gate) return _steps.FirstOrDefault(s => s.State == StepState.Running) ?? _steps.FirstOrDefault(s => s.State == StepState.Pending); } }

    /// <summary>0 to 1. Reaches 1 only when every step has completed.</summary>
    public double Progress
    {
        get
        {
            lock (_gate)
            {
                if (_steps.Count == 0) return 0;
                if (_steps.All(s => s.State == StepState.Completed)) return 1;
                var total = _steps.Sum(s => s.Weight);
                var done = _steps.Sum(s => s.Weight * (s.Fraction ?? 0));
                return Math.Min(.99, done / total);
            }
        }
    }

    /// <summary>True when something is running and none of what runs can say how far it is.</summary>
    public bool Indeterminate { get { lock (_gate) { var running = _steps.Where(s => s.State == StepState.Running).ToList(); return running.Count > 0 && running.All(s => s.Indeterminate); } } }

    public void Start(LoadingMode mode, string title, string destination, IEnumerable<StepDefinition> steps)
    {
        var list = steps.ToList();
        if (list.Count == 0) throw new ArgumentException("A loading needs at least one step.", nameof(steps));
        if (list.Select(s => s.Id).Distinct().Count() != list.Count) throw new ArgumentException("Step ids must be unique.", nameof(steps));
        for (var i = 0; i < list.Count; i++)
            foreach (var dependency in list[i].DependsOn)
                if (list.FindIndex(s => s.Id == dependency) is not (>= 0 and var at) || at >= i) throw new ArgumentException($"Step '{list[i].Id}' depends on '{dependency}', which does not come before it.", nameof(steps));
        lock (_gate)
        {
            _steps.Clear(); _steps.AddRange(list.Select(d => new LoadingStep(d)));
            Mode = mode; Title = title; Destination = destination; Active = true; Succeeded = false; Cancelled = false; FailedStep = null; FailureMessage = null;
        }
        LoadingStarted?.Invoke(); LoadingProgressChanged?.Invoke(Progress);
    }

    public void SetDestination(string destination) { lock (_gate) Destination = destination; }

    /// <summary>Starts a step. False when its dependencies are not complete yet or the loading is over; starting a running step is fine.</summary>
    public bool Begin(string id)
    {
        LoadingStep step;
        lock (_gate)
        {
            if (!Active || Finished || FindLocked(id) is not { } found) return false;
            step = found;
            if (step.State == StepState.Running) return true;
            if (step.State != StepState.Pending || !DependenciesDone(step)) return false;
            step.State = StepState.Running; step.StartedAt = _clock();
        }
        LoadingStepChanged?.Invoke(step); LoadingProgressChanged?.Invoke(Progress);
        return true;
    }

    /// <summary>Reports measured progress of a running step (starting it if it can start). Total may be null while unknown.</summary>
    public void Report(string id, long done, long? total = null)
    {
        LoadingStep step;
        lock (_gate)
        {
            if (!Active || Finished || FindLocked(id) is not { } found) return;
            step = found;
            if (step.State == StepState.Pending) { if (!DependenciesDone(step)) return; step.State = StepState.Running; step.StartedAt = _clock(); }
            if (step.State != StepState.Running) return;
            step.Done = Math.Max(0, done); if (total is not null) step.Total = total;
        }
        LoadingStepChanged?.Invoke(step); LoadingProgressChanged?.Invoke(Progress);
    }

    /// <summary>Finishes a step. False when it cannot finish (dependencies pending, already failed…); finishing twice is harmless.</summary>
    public bool Complete(string id)
    {
        LoadingStep step; bool allDone;
        lock (_gate)
        {
            if (!Active || Finished || FindLocked(id) is not { } found) return false;
            step = found;
            if (step.State == StepState.Completed) return true;
            if (step.State is StepState.Failed or StepState.Cancelled || !DependenciesDone(step)) return false;
            step.StartedAt ??= _clock(); step.State = StepState.Completed; step.CompletedAt = _clock(); step.Error = null;
            if (step.Total is > 0) step.Done = step.Total.Value;
            allDone = _steps.All(s => s.State == StepState.Completed);
            if (allDone) { Succeeded = true; Active = false; }
        }
        LoadingStepChanged?.Invoke(step); LoadingStepCompleted?.Invoke(step); LoadingProgressChanged?.Invoke(Progress);
        if (allDone) LoadingCompleted?.Invoke();
        return true;
    }

    public void Fail(string id, string error)
    {
        LoadingStep step;
        lock (_gate)
        {
            if (!Active || Finished || FindLocked(id) is not { } found) return;
            step = found; step.State = StepState.Failed; step.Error = error; step.CompletedAt = _clock();
            FailedStep = step; FailureMessage = error; Active = false;
            foreach (var other in _steps.Where(s => s.State is StepState.Pending or StepState.Running && s != step)) other.State = StepState.Cancelled;
        }
        LoadingStepChanged?.Invoke(step); LoadingFailed?.Invoke(step, error); LoadingProgressChanged?.Invoke(Progress);
    }

    /// <summary>Stops the loading: whatever has not completed is cancelled.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (!Active || Finished) return;
            foreach (var step in _steps.Where(s => s.State is StepState.Pending or StepState.Running)) step.State = StepState.Cancelled;
            Cancelled = true; Active = false;
        }
        LoadingCancelled?.Invoke(); LoadingProgressChanged?.Invoke(Progress);
    }

    /// <summary>After a failure or a cancellation: what completed stays, everything else goes back to waiting, and the loading runs again.</summary>
    public bool Retry()
    {
        lock (_gate)
        {
            if (!Failed && !Cancelled) return false;
            foreach (var step in _steps.Where(s => s.State != StepState.Completed)) { step.State = StepState.Pending; step.Done = 0; step.Total = null; step.StartedAt = null; step.CompletedAt = null; step.Error = null; }
            FailedStep = null; FailureMessage = null; Cancelled = false; Active = true;
        }
        LoadingStarted?.Invoke(); LoadingProgressChanged?.Invoke(Progress);
        return true;
    }

    private LoadingStep? FindLocked(string id) => _steps.FirstOrDefault(s => s.Id == id);
    private bool DependenciesDone(LoadingStep step) => step.DependsOn.All(d => FindLocked(d)?.State == StepState.Completed);
}
