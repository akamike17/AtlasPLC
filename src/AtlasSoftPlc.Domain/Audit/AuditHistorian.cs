namespace AtlasSoftPlc.Domain.Audit;

public enum AuditEventType
{
    Login,
    DeviceConnect,
    DeviceDisconnect,
    BindingModified,
    LogicCreated,
    Validation,
    Simulation,
    Activation,
    Pause,
    Force,
    OutputWrite,
    Fault,
    Rollback,
    Shutdown,
    Startup
}

/// <summary>Evento de auditoría (sección 30).</summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string User { get; set; } = string.Empty;
    public AuditEventType Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>Política del historian (sección 33).</summary>
public enum HistorianPolicy
{
    OnChange,
    Interval,
    AlarmOnly,
    Disabled
}

/// <summary>Muestra del historian.</summary>
public sealed class HistorianSample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VariableId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Value { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
}