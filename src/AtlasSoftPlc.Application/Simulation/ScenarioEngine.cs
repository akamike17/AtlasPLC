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
        var result = new ScenarioRunResult();
        var inputs = new Dictionary<string, bool>(StringComparer.Ordinal);
        var elapsed = TimeSpan.Zero;
        foreach (var input in scenario.Inputs.OrderBy(x => x.At))
        {
            if (input.At < elapsed) throw new InvalidOperationException("Los eventos del escenario deben estar ordenados en tiempo no decreciente.");
            clock.Advance(input.At - elapsed);
            elapsed = input.At;
            inputs[input.Name] = input.Value;
            var outputs = new Dictionary<string, bool>(evaluate(inputs), StringComparer.Ordinal);
            result.Trace.Add(new ScenarioTraceEntry(input.At, outputs));
            foreach (var assertion in scenario.Assertions.Where(a => a.At == input.At) .Where(a => !a.Check(outputs)))
                result.Failures.Add(assertion.Description);
        }
        result.Passed = result.Failures.Count == 0;
        return result;
    }
}
