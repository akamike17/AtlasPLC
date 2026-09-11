using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>
/// Arbitra las propuestas de salida resolviendo conflictos por prioridad
/// (sección 17). Nunca "la última gana" ni "el primero de la lista gana".
/// </summary>
public sealed class OutputArbiter
{
    public sealed record ArbitrationDecision(
        Guid VariableId,
        PlcValue Value,
        bool Conflicted,
        IReadOnlyList<OutputProposal> CompetingProposals,
        bool Forced = false);

    /// <summary>Resultado completo del arbitraje: decisiones + errores de configuración.</summary>
    public sealed record ArbitrationOutcome(
        List<ArbitrationDecision> Decisions,
        List<string> Errors);

    /// <summary>
    /// Agrupa propuestas y selecciona la de mayor prioridad. Un empate en la prioridad
    /// máxima se marca como <c>Conflicted</c>; el valor provisional es la primera
    /// propuesta solo para diagnóstico — el valor final lo decide
    /// <see cref="ApplyInterlocksAndFailsafe"/> (failsafe o error de configuración),
    /// nunca el orden incidental de enumeración.
    /// </summary>
    public List<ArbitrationDecision> Arbitrate(IReadOnlyCollection<OutputProposal> proposals)
    {
        var grouped = proposals
            .GroupBy(p => p.VariableId)
            .ToList();

        var decisions = new List<ArbitrationDecision>();
        foreach (var group in grouped)
        {
            var ordered = group.OrderByDescending(p => (int)p.Priority).ToList();
            var top = ordered[0];
            var conflicted = ordered.Count > 1 &&
                ordered.Skip(1).Any(p => (int)p.Priority == (int)top.Priority);

            var type = InferType(top.Value);
            var forced = top.Priority == OutputPriority.ManualForcedSafeCommand;
            decisions.Add(new ArbitrationDecision(
                group.Key,
                new PlcValue(type, top.Value),
                conflicted,
                ordered,
                forced));
        }
        return decisions;
    }

    /// <summary>
    /// Resuelve el arbitraje completo para <b>cada salida definida</b> en el scan
    /// (P1-1): valor calculado, valor retenido explícitamente, o failsafe. Nunca
    /// "sin propuesta" como semántica accidental.
    /// </summary>
    /// <param name="proposals">Propuestas emitidas por el motor lógico este scan.</param>
    /// <param name="outputTypes">Tipo de dato real de cada salida definida.</param>
    /// <param name="lastOutputs">Último valor conocido de cada salida (para retención).</param>
    /// <param name="failsafeValues">Failsafe configurado por salida.</param>
    /// <param name="interlocks">Interlocks del programa.</param>
    /// <param name="interlockActive">Resultado de evaluar cada interlock (Id → activo).</param>
    /// <param name="interlockErrors">Interlocks no evaluables (Id → error), tratados fail-closed.</param>
    public ArbitrationOutcome Resolve(
        IReadOnlyCollection<OutputProposal> proposals,
        IReadOnlyDictionary<Guid, PlcDataType> outputTypes,
        IReadOnlyDictionary<Guid, RuntimeValue> lastOutputs,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, bool> interlockActive,
        IReadOnlyDictionary<Guid, string> interlockErrors)
    {
        var errors = new List<string>();
        var decisions = new List<ArbitrationDecision>();

        // 1. Arbitrar las salidas que recibieron propuesta.
        var proposed = Arbitrate(proposals);

        // 2. Toda salida definida debe tener una decisión.
        var coveredIds = new HashSet<Guid>(proposed.Select(d => d.VariableId));
        foreach (var (variableId, dataType) in outputTypes)
        {
            if (coveredIds.Contains(variableId))
                continue;

            // Sin propuesta este scan: retener valor explícito o failsafe.
            if (lastOutputs.TryGetValue(variableId, out var last) && last.Value.HasValue)
            {
                decisions.Add(new ArbitrationDecision(
                    variableId,
                    last.Value,
                    Conflicted: false,
                    CompetingProposals: new List<OutputProposal>()));
            }
            else if (failsafeValues.TryGetValue(variableId, out var fs))
            {
                decisions.Add(new ArbitrationDecision(
                    variableId,
                    fs,
                    Conflicted: false,
                    CompetingProposals: new List<OutputProposal>()));
            }
            else
            {
                // Salida definida sin valor previo y sin failsafe: error de configuración.
                errors.Add($"[Output {variableId}] Sin propuesta, sin valor retenido y sin failsafe");
            }
        }

        // 3. Aplicar interlocks (activos y no-evaluables fail-closed) + failsafe y conflictos.
        foreach (var decision in proposed)
        {
            PlcValue value = decision.Value;
            bool interlocked = false;
            bool forced = decision.Forced;

            foreach (var interlock in interlocks)
            {
                if (!interlock.AffectedOutputs.Contains(decision.VariableId)) continue;

                // Interlock no evaluable => fail-closed: se fuerza SafeValue (P0-2).
                var failClosed = interlockErrors.TryGetValue(interlock.Id, out _);

                if (failClosed || (interlockActive.TryGetValue(interlock.Id, out var active) && active))
                {
                    value = PlcValue.Bool(interlock.SafeValue);
                    interlocked = true;
                    forced = false; // interlock/failsafe de seguridad derogó el force
                    break;
                }
            }

            if (!interlocked && decision.Conflicted)
            {
                if (failsafeValues.TryGetValue(decision.VariableId, out var fs))
                {
                    value = fs;
                    forced = false; // failsafe por conflicto derogó el force
                }
                else
                {
                    // Conflicto irresoluble sin failsafe: error de configuración (P1-2).
                    // No activamos el valor provisional ("primero gana" queda prohibido).
                    var competidores = string.Join(", ",
                        decision.CompetingProposals.Select(p => p.Reason ?? p.SourceRuleId.ToString()));
                    errors.Add($"[Output {decision.VariableId}] Conflicto de igual prioridad sin failsafe. Reglas: {competidores}");
                    continue; // sin decisión operable: el scan reporta el error.
                }
            }

            decisions.Add(new ArbitrationDecision(
                decision.VariableId, value, decision.Conflicted, decision.CompetingProposals, forced));
        }

        // 4. Orden determinista por VariableId (independiente del orden de enumeración).
        decisions = decisions.OrderBy(d => d.VariableId).ToList();
        errors.Sort(StringComparer.Ordinal);

        return new ArbitrationOutcome(decisions, errors);
    }

    /// <summary>
    /// Aplica interlocks y failsafe sobre decisiones ya arbitradas. Un interlock activo
    /// fuerza su SafeValue; un interlock no evaluable también se trata fail-closed; ante
    /// conflicto irresoluble y sin failsafe se reporta error (nunca "primero gana").
    /// </summary>
    public ArbitrationOutcome ApplyInterlocksAndFailsafe(
        IReadOnlyCollection<ArbitrationDecision> decisions,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, bool> interlockActive,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues,
        IReadOnlyDictionary<Guid, PlcDataType> outputTypes,
        IReadOnlyDictionary<Guid, string>? interlockErrors = null)
    {
        interlockErrors ??= new Dictionary<Guid, string>();

        var result = new List<ArbitrationDecision>();
        var errors = new List<string>();

        foreach (var decision in decisions)
        {
            PlcValue value = decision.Value;
            bool interlocked = false;
            bool forced = decision.Forced;

            foreach (var interlock in interlocks)
            {
                if (!interlock.AffectedOutputs.Contains(decision.VariableId)) continue;

                var failClosed = interlockErrors.TryGetValue(interlock.Id, out _);
                if (failClosed || (interlockActive.TryGetValue(interlock.Id, out var active) && active))
                {
                    value = PlcValue.Bool(interlock.SafeValue);
                    interlocked = true;
                    forced = false;
                    break;
                }
            }

            if (!interlocked && decision.Conflicted)
            {
                if (failsafeValues.TryGetValue(decision.VariableId, out var fs))
                {
                    value = fs;
                    forced = false;
                }
                else
                {
                    var competidores = string.Join(", ",
                        decision.CompetingProposals.Select(p => p.Reason ?? p.SourceRuleId.ToString()));
                    errors.Add($"[Output {decision.VariableId}] Conflicto de igual prioridad sin failsafe. Reglas: {competidores}");
                    continue;
                }
            }

            result.Add(new ArbitrationDecision(decision.VariableId, value, decision.Conflicted, decision.CompetingProposals, forced));
        }

        result.Sort((a, b) => a.VariableId.CompareTo(b.VariableId));
        errors.Sort(StringComparer.Ordinal);

        return new ArbitrationOutcome(result, errors);
    }

    /// <summary>Infiere el tipo de dato a partir del valor .NET propuesto.</summary>
    private static PlcDataType InferType(object value) => value switch
    {
        bool => PlcDataType.Bool,
        short => PlcDataType.Int16,
        ushort => PlcDataType.UInt16,
        int => PlcDataType.Int32,
        uint => PlcDataType.UInt32,
        long => PlcDataType.Int64,
        ulong => PlcDataType.UInt64,
        float => PlcDataType.Float,
        double => PlcDataType.Double,
        decimal => PlcDataType.Decimal,
        string => PlcDataType.String,
        DateTime => PlcDataType.DateTime,
        TimeSpan => PlcDataType.TimeSpan,
        _ => PlcDataType.String
    };
}