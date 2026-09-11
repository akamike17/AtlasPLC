namespace AtlasSoftPlc.Domain.Alarms;

public enum AlarmSeverity
{
    Info,
    Warning,
    High,
    Critical
}

public enum AlarmState
{
    Inactive,
    ActiveUnacknowledged,
    ActiveAcknowledged,
    ClearedUnacknowledged,
    Closed
}

/// <summary>Definición de alarma (sección 32).</summary>
public sealed class AlarmDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;
    public Guid? RelatedVariableId { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Instancia activa de una alarma (sección 32).</summary>
public sealed class AlarmInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public AlarmSeverity Severity { get; set; }
    public AlarmState State { get; set; } = AlarmState.ActiveUnacknowledged;
    public DateTime RaisedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClearedUtc { get; set; }
    public DateTime? AcknowledgedUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public Guid? RelatedVariableId { get; set; }
}