namespace AtlasSoftPlc.Application.Simulation;

public interface ISimulationClock { DateTimeOffset UtcNow { get; } void Advance(TimeSpan amount); }

public sealed class VirtualSimulationClock : ISimulationClock
{
    public VirtualSimulationClock(DateTimeOffset? start = null) => UtcNow = start ?? DateTimeOffset.UnixEpoch;
    public DateTimeOffset UtcNow { get; private set; }
    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
        UtcNow += amount;
    }
}

public sealed record ScenarioInputEvent(TimeSpan At, string Name, bool Value);
public sealed record ScenarioAssertion(TimeSpan At, Func<IReadOnlyDictionary<string, bool>, bool> Check, string Description);
public sealed record ScenarioTraceEntry(TimeSpan At, IReadOnlyDictionary<string, bool> Outputs);
public sealed class ScenarioDefinition
{
    public List<ScenarioInputEvent> Inputs { get; init; } = new();
    public List<ScenarioAssertion> Assertions { get; init; } = new();
    public TimeSpan Duration { get; init; }
}

public sealed class ScenarioRunResult
{
    public bool Passed { get; set; }
    public List<ScenarioTraceEntry> Trace { get; } = new();
    public List<string> Failures { get; } = new();
}

/// <summary>Runner pequeño y determinista; no sustituye el reloj productivo.</summary>
public sealed class ScenarioRunner
{
    public ScenarioRunResult Run(ScenarioDefinition scenario, Func<IReadOnlyDictionary<string, bool>, IReadOnlyDictionary<string, bool>> evaluate, ISimulationClock clock)
    {
        if (scenario.Duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(scenario.Duration));
        if (scenario.Inputs.Any(x => x.At < TimeSpan.Zero || x.At > scenario.Duration) || scenario.Assertions.Any(x => x.At < TimeSpan.Zero || x.At > scenario.Duration))
            throw new ArgumentOutOfRangeException(nameof(scenario), "Los eventos y assertions deben estar dentro de la duración del escenario.");
        var result = new ScenarioRunResult();
        var inputs = new Dictionary<string, bool>(StringComparer.Ordinal);
        var elapsed = TimeSpan.Zero;
        var points = scenario.Inputs.Select(x => x.At).Concat(scenario.Assertions.Select(x => x.At)).Append(scenario.Duration).Distinct().OrderBy(x => x);
        foreach (var point in points)
        {
            clock.Advance(point - elapsed);
            elapsed = point;
            foreach (var input in scenario.Inputs.Where(x => x.At == point)) inputs[input.Name] = input.Value;
            var outputs = new Dictionary<string, bool>(evaluate(inputs), StringComparer.Ordinal);
            result.Trace.Add(new ScenarioTraceEntry(point, outputs));
            foreach (var assertion in scenario.Assertions.Where(a => a.At == point).Where(a => !a.Check(outputs)))
                result.Failures.Add(assertion.Description);
        }
        result.Passed = result.Failures.Count == 0;
        return result;
    }
}
