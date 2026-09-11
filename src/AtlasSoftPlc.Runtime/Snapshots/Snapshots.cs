using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Runtime.Snapshots;

/// <summary>Imagen congelada de entradas por scan (sección 11). Inmutable.</summary>
public sealed class InputSnapshot
{
    private readonly IReadOnlyDictionary<Guid, RuntimeValue> _values;

    public InputSnapshot(IReadOnlyDictionary<Guid, RuntimeValue> values)
    {
        _values = values;
    }

    public IReadOnlyDictionary<Guid, RuntimeValue> Values => _values;

    public long ScanNumber { get; init; }

    public RuntimeValue? Get(Guid variableId) =>
        _values.TryGetValue(variableId, out var v) ? v : null;

    public static InputSnapshot Empty { get; } =
        new InputSnapshot(new Dictionary<Guid, RuntimeValue>());
}

/// <summary>Imagen de memoria (variables Memory/Internal) congelada por scan.</summary>
public sealed class MemorySnapshot
{
    private readonly IReadOnlyDictionary<Guid, RuntimeValue> _values;

    public MemorySnapshot(IReadOnlyDictionary<Guid, RuntimeValue> values)
    {
        _values = values;
    }

    public IReadOnlyDictionary<Guid, RuntimeValue> Values => _values;

    public RuntimeValue? Get(Guid variableId) =>
        _values.TryGetValue(variableId, out var v) ? v : null;

    public static MemorySnapshot Empty { get; } =
        new MemorySnapshot(new Dictionary<Guid, RuntimeValue>());
}

/// <summary>Imagen de salidas resultante del scan, ya arbitrada (sección 11).</summary>
public sealed class OutputSnapshot
{
    private readonly IReadOnlyDictionary<Guid, RuntimeValue> _values;

    public OutputSnapshot(IReadOnlyDictionary<Guid, RuntimeValue> values)
    {
        _values = values;
    }

    public IReadOnlyDictionary<Guid, RuntimeValue> Values => _values;

    public RuntimeValue? Get(Guid variableId) =>
        _values.TryGetValue(variableId, out var v) ? v : null;

    public long ScanNumber { get; init; }

    public static OutputSnapshot Empty { get; } =
        new OutputSnapshot(new Dictionary<Guid, RuntimeValue>());
}