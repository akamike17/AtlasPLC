using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>
/// Entrada inmutable de un scan: agrupa todos los insumos que el <see cref="ScanCoordinator"/>
/// necesita para ejecutar un ciclo, evitando métodos con firmas largas (deuda técnica).
/// </summary>
/// <param name="ForcedOutputs">
/// Propuestas de fuerza (P0-3) emitidas por el operador. Entran al arbitraje con prioridad
/// <see cref="OutputPriority.ManualForcedSafeCommand"/>, por encima del control automático
/// pero por debajo de interlocks/failsafe de seguridad (que se aplican después del arbitraje).
/// </param>
public sealed record ScanRequest(
    LogicProgram Program,
    IReadOnlyDictionary<Guid, VariableDefinition> Definitions,
    IReadOnlyDictionary<Guid, RuntimeValue> Inputs,
    IReadOnlyDictionary<Guid, RuntimeValue> Memory,
    IReadOnlyCollection<Interlock> Interlocks,
    IReadOnlyDictionary<Guid, PlcValue> FailsafeValues,
    IReadOnlyDictionary<Guid, PlcDataType> OutputTypes,
    double DeltaMs,
    IReadOnlyCollection<OutputProposal>? ForcedOutputs = null)
{
    public static ScanRequest Create(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> definitions,
        IReadOnlyDictionary<Guid, RuntimeValue> inputs,
        IReadOnlyDictionary<Guid, RuntimeValue> memory,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues,
        IReadOnlyDictionary<Guid, PlcDataType> outputTypes,
        double deltaMs,
        IReadOnlyCollection<OutputProposal>? forcedOutputs = null) =>
        new(program, definitions, inputs, memory, interlocks, failsafeValues, outputTypes, deltaMs, forcedOutputs);
}