using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Runtime.Expressions;
using AtlasSoftPlc.Runtime.Snapshots;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>
/// Contexto de evaluación de un scan: lee entradas congeladas, memoria,
/// y estado de timers/counters/flancos.
/// </summary>
public sealed class ScanExpressionContext : IExpressionContext
{
    private readonly InputSnapshot _inputs;
    private readonly MemorySnapshot _memory;
    private readonly TimerManager _timers;
    private readonly CounterManager _counters;
    private readonly VariableSnapshotContext _vars;

    // rastreo de flancos por operando (valor previo del ciclo anterior)
    private readonly Dictionary<Guid, bool> _edgeState;

    public ScanExpressionContext(
        InputSnapshot inputs,
        MemorySnapshot memory,
        TimerManager timers,
        CounterManager counters,
        VariableSnapshotContext vars,
        IReadOnlyDictionary<Guid, bool>? previousEdgeState = null)
    {
        _inputs = inputs;
        _memory = memory;
        _timers = timers;
        _counters = counters;
        _vars = vars;
        _edgeState = previousEdgeState is null
            ? new Dictionary<Guid, bool>()
            : new Dictionary<Guid, bool>(previousEdgeState);
    }

    public PlcValue? ReadVariable(Guid variableId)
    {
        var input = _inputs.Get(variableId);
        if (input is not null) return input.Value;

        var mem = _memory.Get(variableId);
        if (mem is not null) return mem.Value;

        return _vars.ReadMemory(variableId);
    }

    public bool ReadTimerField(Guid timerId, string field, out double value) =>
        _timers.TryRead(timerId, field, out value);

    public bool ReadCounterField(Guid counterId, string field, out long value) =>
        _counters.TryRead(counterId, field, out value);

    public bool DetectEdge(bool currentValue, Guid operandId, bool expectedRising)
    {
        var prev = _edgeState.TryGetValue(operandId, out var p) && p;
        _edgeState[operandId] = currentValue;
        if (expectedRising)
            return currentValue && !prev; // rising edge
        return !currentValue && prev;     // falling edge
    }

    public IReadOnlyDictionary<Guid, bool> EdgeState => _edgeState;
}