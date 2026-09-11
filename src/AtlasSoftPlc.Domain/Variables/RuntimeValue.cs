using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Domain.Variables;

/// <summary>Valor de una variable en un instante de ejecución (sección 7).</summary>
public sealed class RuntimeValue
{
    public Guid VariableId { get; set; }
    public PlcValue Value { get; set; }
    public Quality Quality { get; set; } = Quality.Good;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public ValueSource Source { get; set; } = ValueSource.None;
    public long SequenceNumber { get; set; }

    public RuntimeValue Clone() => (RuntimeValue)MemberwiseClone();
}