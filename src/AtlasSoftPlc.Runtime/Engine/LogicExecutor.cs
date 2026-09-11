using System.Globalization;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Expressions;
using AtlasSoftPlc.Runtime.Snapshots;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>
/// Ejecuta las reglas del IR contra snapshots y produce OutputProposals.
/// No escribe directamente a ningún driver (sección 10).
/// </summary>
public sealed class LogicExecutor
{
    private readonly ExpressionEngine _engine = new();
    private readonly TimerManager _timers;
    private readonly CounterManager _counters;

    public LogicExecutor(TimerManager timers, CounterManager counters)
    {
        _timers = timers;
        _counters = counters;
    }

    public sealed class ExecutionResult
    {
        public List<OutputProposal> Proposals { get; } = new();
        public List<LogicAction> RaiseAlarmActions { get; } = new();
        public List<string> Errors { get; } = new();
        public List<RuleTrace> Traces { get; } = new();
        public IReadOnlyDictionary<Guid, bool> EdgeState { get; set; } = new Dictionary<Guid, bool>();
    }

    public sealed class RuleTrace
    {
        public Guid RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public bool ConditionResult { get; set; }
        public List<string> ActionDescriptions { get; set; } = new();
    }

    public ExecutionResult Execute(
        LogicProgram program,
        InputSnapshot inputs,
        MemorySnapshot memory,
        VariableSnapshotContext varContext,
        IReadOnlyDictionary<Guid, bool>? previousEdgeState = null)
    {
        var result = new ExecutionResult();
        var ctx = new ScanExpressionContext(inputs, memory, _timers, _counters, varContext, previousEdgeState);

        // registra timers/counters declarados
        RegisterTimersAndCounters(program, varContext);

        foreach (var rule in program.Rules.OrderByDescending(r => r.Priority))
        {
            if (!rule.Enabled) continue;

            var eval = _engine.Evaluate(rule.Condition, ctx);
            var trace = new RuleTrace { RuleId = rule.Id, RuleName = rule.Name };

            if (!eval.Ok)
            {
                result.Errors.Add($"[{rule.Name}] {eval.Errors}");
                continue;
            }

            var conditionTrue = eval.Value.AsBool();
            trace.ConditionResult = conditionTrue;

            var actions = conditionTrue ? rule.Actions : rule.ElseActions;
            foreach (var action in actions)
            {
                ApplyAction(action, rule, varContext, result, trace);
            }

            result.Traces.Add(trace);
        }

        result.EdgeState = ctx.EdgeState;
        return result;
    }

    private void RegisterTimersAndCounters(LogicProgram program, VariableSnapshotContext varContext)
    {
        // Los timers/counters se registran de forma perezosa cuando una StartTimerAction
        // los invoca; aquí pre-declaramos los IDs listados en el programa.
        foreach (var timerId in program.TimerIds)
            _timers.GetOrCreate(timerId, "TON", 0);
        foreach (var counterId in program.CounterIds)
            _counters.GetOrCreate(counterId, "CTU", 0);
    }

    private void ApplyAction(
        LogicAction action,
        LogicRule rule,
        VariableSnapshotContext varContext,
        ExecutionResult result,
        RuleTrace trace)
    {
        switch (action)
        {
            case SetOutputAction o:
                var outParse = ParseTyped(o.Value, o.VariableId, varContext);
                if (outParse.Error is not null)
                {
                    // Valor imposible de interpretar: no generamos propuesta inválida.
                    // La política segura (failsafe) la aplica el arbitrador sobre el output afectado.
                    result.Errors.Add($"[{rule.Name}] Output {o.VariableId}: {outParse.Error}");
                    break;
                }
                result.Proposals.Add(new OutputProposal
                {
                    VariableId = o.VariableId,
                    Value = outParse.Value!.Value.Raw ?? false,
                    Priority = OutputPriority.AutomaticControl,
                    SourceRuleId = rule.Id,
                    Reason = rule.Name
                });
                trace.ActionDescriptions.Add($"Output {o.VariableId} = {o.Value}");
                break;

            case SetMemoryAction m:
                var memParse = ParseTyped(m.Value, m.VariableId, varContext);
                if (memParse.Error is not null)
                {
                    result.Errors.Add($"[{rule.Name}] Memory {m.VariableId}: {memParse.Error}");
                    break;
                }
                varContext.SetMemory(m.VariableId, memParse.Value!.Value, ValueSource.Logic);
                trace.ActionDescriptions.Add($"Memory {m.VariableId} = {m.Value}");
                break;

            case ResetMemoryAction rm:
                varContext.SetMemory(rm.VariableId, PlcValue.Bool(false), ValueSource.Logic);
                trace.ActionDescriptions.Add($"Memory {rm.VariableId} = false");
                break;

            case StartTimerAction st:
                _timers.GetOrCreate(st.TimerId, GetTimerKind(rule, st.TimerId), st.PresetMs);
                _timers.SetInput(st.TimerId, true);
                trace.ActionDescriptions.Add($"Timer {st.TimerId} input=ON preset={st.PresetMs}ms");
                break;

            case ResetTimerAction rt:
                _timers.Reset(rt.TimerId);
                _timers.SetInput(rt.TimerId, false);
                trace.ActionDescriptions.Add($"Timer {rt.TimerId} reset");
                break;

            case IncrementCounterAction ic:
                _counters.Increment(ic.CounterId);
                trace.ActionDescriptions.Add($"Counter {ic.CounterId} ++");
                break;

            case ResetCounterAction rc:
                _counters.Reset(rc.CounterId);
                trace.ActionDescriptions.Add($"Counter {rc.CounterId} reset");
                break;

            case RaiseAlarmAction ra:
                result.RaiseAlarmActions.Add(ra);
                trace.ActionDescriptions.Add($"Alarm '${ra.Message}'");
                break;

            case AcknowledgeAlarmAction aa:
                result.RaiseAlarmActions.Add(aa);
                trace.ActionDescriptions.Add($"Ack alarm {aa.AlarmDefinitionId}");
                break;

            case LogEventAction le:
                trace.ActionDescriptions.Add($"Log: {le.Message}");
                break;

            default:
                result.Errors.Add($"[{rule.Name}] Acción no soportada: {action.GetType().Name}");
                break;
        }
    }

    private static string GetTimerKind(LogicRule rule, Guid timerId)
    {
        // Los timers se crean con TON por defecto; TOF/TP se declaran via ID prefijo o acción especializada.
        // Aquí inferimos TOF/TP sólo si el nombre de la regla lo indica (fase 1 simple).
        return "TON";
    }

    /// <summary>
    /// Interpreta el string de la acción según el tipo de la variable destino.
    /// Un valor imposible (overflow, formato, tipo incompatible) devuelve un error
    /// explícito — nunca un String silencioso — para que la capa superior aplique
    /// política segura (failsafe) sobre el output/memory afectado.
    /// </summary>
    private static (PlcValue? Value, string? Error) ParseTyped(string value, Guid variableId, VariableSnapshotContext varContext)
    {
        var type = varContext.GetVariableType(variableId);
        try
        {
            object v = type switch
            {
                PlcDataType.Bool => bool.TryParse(value, out var b) ? b
                    : value == "1" ? true : value == "0" ? false
                    : throw new FormatException($"No es bool: {value}"),
                PlcDataType.Int16 => short.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.UInt16 => ushort.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.Int32 => int.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.UInt32 => uint.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.Float => float.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.Double => double.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.Decimal => decimal.Parse(value, CultureInfo.InvariantCulture),
                PlcDataType.String => value,
                PlcDataType.DateTime => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                PlcDataType.TimeSpan => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
                _ => throw new FormatException($"Tipo no soportado para parseo: {type}")
            };
            return (new PlcValue(type, v), null);
        }
        catch (Exception ex)
        {
            // error explícito: nunca convertir silenciosamente a String.
            return (null, $"valor inválido para {type}: '{value}' ({ex.Message})");
        }
    }
}

/// <summary>Contexto combinado de evaluación y mutación de memoria por scan.</summary>
public sealed class VariableSnapshotContext
{
    private readonly Dictionary<Guid, VariableDefinition> _definitions;
    private readonly Dictionary<Guid, RuntimeValue> _memory;

    public VariableSnapshotContext(
        IReadOnlyDictionary<Guid, VariableDefinition> definitions,
        Dictionary<Guid, RuntimeValue> memory)
    {
        _definitions = new Dictionary<Guid, VariableDefinition>(definitions);
        _memory = memory;
    }

    public PlcDataType GetVariableType(Guid variableId) =>
        _definitions.TryGetValue(variableId, out var d) ? d.DataType : PlcDataType.String;

    public PlcValue? ReadMemory(Guid variableId) =>
        _memory.TryGetValue(variableId, out var v) ? v.Value : null;

    public void SetMemory(Guid variableId, PlcValue value, ValueSource source)
    {
        _memory[variableId] = new RuntimeValue
        {
            VariableId = variableId,
            Value = value,
            Quality = Quality.Good,
            Source = source,
            TimestampUtc = DateTime.UtcNow
        };
    }

    public IReadOnlyDictionary<Guid, RuntimeValue> Memory => _memory;
}