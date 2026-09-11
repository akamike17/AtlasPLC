using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>
/// Arbitra las propuestas de salida resolviendo conflictos por prioridad
/// (sección 17). Nunca "la última gana" silenciosamente.
/// </summary>
public sealed class OutputArbiter
{
    public sealed record ArbitrationDecision(
        Guid VariableId,
        PlcValue Value,
        bool Conflicted,
        IReadOnlyList<OutputProposal> CompetingProposals);

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

            // Inferimos el tipo a partir del valor propuesto (todas las propuestas
            // de una misma salida comparten el tipo de dato de la variable).
            var type = InferType(top.Value);
            decisions.Add(new ArbitrationDecision(
                group.Key,
                new PlcValue(type, top.Value),
                conflicted,
                ordered));
        }
        return decisions;
    }

    /// <summary>
    /// Aplica interlocks y failsafe sobre las decisiones: un interlock activo
    /// fuerza su SafeValue; ante conflicto irresoluble se usa el failsafe.
    /// Requiere los tipos de dato reales de las salidas.
    /// </summary>
    public List<ArbitrationDecision> ApplyInterlocksAndFailsafe(
        IReadOnlyCollection<ArbitrationDecision> decisions,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, bool> interlockActive,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues,
        IReadOnlyDictionary<Guid, PlcDataType> outputTypes)
    {
        var result = new List<ArbitrationDecision>();

        foreach (var decision in decisions)
        {
            PlcValue value = decision.Value;
            bool interlocked = false;

            foreach (var interlock in interlocks)
            {
                if (!interlock.AffectedOutputs.Contains(decision.VariableId)) continue;
                if (!interlockActive.TryGetValue(interlock.Id, out var active) || !active) continue;

                value = PlcValue.Bool(interlock.SafeValue);
                interlocked = true;
                break;
            }

            if (!interlocked && decision.Conflicted)
            {
                if (failsafeValues.TryGetValue(decision.VariableId, out var fs))
                    value = fs;
            }

            result.Add(new ArbitrationDecision(decision.VariableId, value, decision.Conflicted, decision.CompetingProposals));
        }
        return result;
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