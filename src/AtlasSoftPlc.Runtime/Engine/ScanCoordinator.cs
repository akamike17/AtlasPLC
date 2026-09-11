using System.Diagnostics;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Expressions;
using AtlasSoftPlc.Runtime.Snapshots;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>Clase Chicken-inicial del scan: coordina captura, ejecución y arbitraje.</summary>
public sealed class ScanCoordinator
{
    private readonly TimerManager _timers;
    private readonly CounterManager _counters;
    private readonly LogicExecutor _executor;
    private readonly OutputArbiter _arbiter;
    private readonly ExpressionEngine _exprEngine;

    private IReadOnlyDictionary<Guid, bool> _edgeState = new Dictionary<Guid, bool>();
    private long _scanNumber;

    public ScanCoordinator()
    {
        _timers = new TimerManager();
        _counters = new CounterManager();
        _executor = new LogicExecutor(_timers, _counters);
        _arbiter = new OutputArbiter();
        _exprEngine = new ExpressionEngine();
    }

    /// <summary>Resultado completo de un scan, diagnósticamente útil y determinista.</summary>
    public sealed class ScanResult
    {
        public long ScanNumber { get; init; }
        public InputSnapshot Inputs { get; init; } = InputSnapshot.Empty;
        public OutputSnapshot Outputs { get; init; } = OutputSnapshot.Empty;
        public IReadOnlyList<OutputArbiter.ArbitrationDecision> Decisions { get; init; } = new List<OutputArbiter.ArbitrationDecision>();
        public IReadOnlyList<LogicExecutor.RuleTrace> Traces { get; init; } = new List<LogicExecutor.RuleTrace>();
        public IReadOnlyList<string> Errors { get; init; } = new List<string>();
        public List<LogicAction> Alarms { get; init; } = new();
        public MemorySnapshot Memory { get; init; } = MemorySnapshot.Empty;

        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// Ejecuta un scan completo y determinista.
    /// </summary>
    public ScanResult Scan(ScanRequest request)
    {
        var program = request.Program;
        var definitions = request.Definitions;
        var inputs = request.Inputs;
        var memory = request.Memory;
        var interlocks = request.Interlocks;
        var failsafeValues = request.FailsafeValues;
        var outputTypes = request.OutputTypes;
        var deltaMs = request.DeltaMs;

        _scanNumber++;
        var inputSnapshot = new InputSnapshot(inputs) { ScanNumber = _scanNumber };
        var memorySnapshot = new MemorySnapshot(memory);

        // 1. avanzar timers
        _timers.AdvanceAll(deltaMs);

        // 2. ejecutar lógica
        var mutableMemory = new Dictionary<Guid, RuntimeValue>(memory);
        var varContext = new VariableSnapshotContext(definitions, mutableMemory);
        var execResult = _executor.Execute(program, inputSnapshot, memorySnapshot, varContext, _edgeState);
        var newMemorySnapshot = new MemorySnapshot(mutableMemory);

        // 3. arbitrar salidas
        var decisions = _arbiter.Arbitrate(execResult.Proposals);

        // 4. interlocks + failsafe
        var interlockActive = EvaluateInterlocks(interlocks, inputSnapshot, memorySnapshot, varContext, out var interlockErrors);
        var finalDecisions = _arbiter.ApplyInterlocksAndFailsafe(
            decisions, interlocks, interlockActive, failsafeValues, outputTypes);

        // 5. construir output snapshot
        var outputs = new Dictionary<Guid, RuntimeValue>();
        foreach (var d in finalDecisions)
        {
            outputs[d.VariableId] = new RuntimeValue
            {
                VariableId = d.VariableId,
                Value = d.Value,
                Quality = Quality.Good,
                Source = ValueSource.Logic,
                TimestampUtc = DateTime.UtcNow,
                SequenceNumber = _scanNumber
            };
        }

        // 6. actualizar edge state para el siguiente scan
        _edgeState = new Dictionary<Guid, bool>(execResult.EdgeState);

        var errors = execResult.Errors.Concat(interlockErrors).ToList();

        return new ScanResult
        {
            ScanNumber = _scanNumber,
            Inputs = inputSnapshot,
            Outputs = new OutputSnapshot(outputs) { ScanNumber = _scanNumber },
            Decisions = finalDecisions,
            Traces = execResult.Traces,
            Errors = errors,
            Alarms = execResult.RaiseAlarmActions,
            Memory = newMemorySnapshot
        };
    }

    /// <summary>
    /// Sobrecarga de compatibilidad para los llamadores que aún pasan parámetros sueltos.
    /// </summary>
    public ScanResult Scan(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> definitions,
        IReadOnlyDictionary<Guid, RuntimeValue> inputs,
        IReadOnlyDictionary<Guid, RuntimeValue> memory,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues,
        IReadOnlyDictionary<Guid, PlcDataType> outputTypes,
        double deltaMs) =>
        Scan(ScanRequest.Create(program, definitions, inputs, memory, interlocks, failsafeValues, outputTypes, deltaMs));

    private Dictionary<Guid, bool> EvaluateInterlocks(
        IReadOnlyCollection<Interlock> interlocks,
        InputSnapshot inputs,
        MemorySnapshot memory,
        VariableSnapshotContext varContext,
        out List<string> errors)
    {
        var active = new Dictionary<Guid, bool>();
        errors = new List<string>();
        var ctx = new ScanExpressionContext(inputs, memory, _timers, _counters, varContext, _edgeState);

        foreach (var interlock in interlocks)
        {
            var eval = _exprEngine.Evaluate(interlock.Condition, ctx);
            if (!eval.Ok)
            {
                errors.Add($"[Interlock {interlock.Name}] {eval.Errors}");
                active[interlock.Id] = false; // ante error no activamos interlock (fail-closed se maneja en failsafe)
                continue;
            }
            active[interlock.Id] = eval.Value.AsBool();
        }
        return active;
    }
}