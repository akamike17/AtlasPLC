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
public sealed record ScanRequest(
    LogicProgram Program,
    IReadOnlyDictionary<Guid, VariableDefinition> Definitions,
    IReadOnlyDictionary<Guid, RuntimeValue> Inputs,
    IReadOnlyDictionary<Guid, RuntimeValue> Memory,
    IReadOnlyCollection<Interlock> Interlocks,
    IReadOnlyDictionary<Guid, PlcValue> FailsafeValues,
    IReadOnlyDictionary<Guid, PlcDataType> OutputTypes,
    double DeltaMs)
{
    public static ScanRequest Create(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> definitions,
        IReadOnlyDictionary<Guid, RuntimeValue> inputs,
        IReadOnlyDictionary<Guid, RuntimeValue> memory,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues,
        IReadOnlyDictionary<Guid, PlcDataType> outputTypes,
        double deltaMs) =>
        new(program, definitions, inputs, memory, interlocks, failsafeValues, outputTypes, deltaMs);
}