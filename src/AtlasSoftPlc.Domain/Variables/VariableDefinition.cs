using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Domain.Variables;

/// <summary>Definición de una variable del modelo (sección 7).</summary>
public sealed class VariableDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Proyecto al que pertenece la variable (relación de persistencia).</summary>
    public Guid ProjectId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PlcDataType DataType { get; set; } = PlcDataType.Bool;
    public VariableDirection Direction { get; set; } = VariableDirection.Memory;
    public string? EngineeringUnit { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public bool Retentive { get; set; }
    public bool SafetyCritical { get; set; }
    public bool ReadOnly { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Valor por defecto normalizado al tipo de dato.</summary>
    public object? DefaultValue { get; set; }

    /// <summary>I/O primitiva usada por el dispositivo virtual (switch, sensor, motor...).</summary>
    public string? VirtualIoKind { get; set; }

    public PlcValue DefaultPlcValue =>
        DefaultValue is null ? PlcValue.Null(DataType) : new PlcValue(DataType, DefaultValue);

    public bool IsBoolLike => DataType == PlcDataType.Bool;

    public VariableDefinition Clone() => (VariableDefinition)MemberwiseClone();
}