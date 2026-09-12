using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Domain.Ir;

/// <summary>
/// Estado seguro de una salida (spec §39 FASE C / §3 safeState / §6 "salida con
/// safeState no garantizado"). Primera clase: hoy el failsafe es sólo un
/// diccionario en <c>PlcProgramDefinition</c>; la IR lo eleva a entidad explícita
/// para que validators y targets lo traten de forma canonical.
/// </summary>
public sealed class SafeState
{
    /// <summary>Variable de salida protegida.</summary>
    public Guid VariableId { get; init; }

    /// <summary>Valor seguro al que cae la salida en Stop/Fault/shutdown.</summary>
    public PlcValue Value { get; init; }

    /// <summary>Motivo/justificación del estado seguro (auditable).</summary>
    public string Reason { get; init; } = string.Empty;
}