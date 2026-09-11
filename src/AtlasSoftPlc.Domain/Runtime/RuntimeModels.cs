using AtlasSoftPlc.Domain.Logic;

namespace AtlasSoftPlc.Domain.Runtime;

/// <summary>Estados generales del runtime / watchdog (sección 19).</summary>
public enum RuntimeState
{
    Starting,
    Running,
    Degraded,
    Faulted,
    Stopped,
    Emergency
}

/// <summary>Modo de operación del proyecto (sección 24).</summary>
public enum RuntimeMode
{
    Simulation,
    Shadow,
    Physical
}

/// <summary>Estado de madurez del proyecto (sección 27).</summary>
public enum ProjectLifecycleState
{
    Draft,
    Validated,
    SimulationReady,
    SimulationPassed,
    ShadowReady,
    ShadowPassed,
    ReadyForActivation,
    Active,
    Paused,
    Faulted,
    Archived
}

/// <summary>Prioridad de un escritor de salida para el arbitraje (sección 17).</summary>
public enum OutputPriority
{
    DefaultFailsafe = 0,
    ManualNormalCommand = 1,
    AutomaticControl = 2,
    Interlock = 3,
    FaultHandling = 4,
    ManualForcedSafeCommand = 5,
    EmergencySafetyInterlock = 6
}

/// <summary>Propuesta de salida emitida por el motor lógico antes del arbitraje.</summary>
public sealed class OutputProposal
{
    public Guid VariableId { get; set; }
    public object Value { get; set; } = false;
    public OutputPriority Priority { get; set; } = OutputPriority.AutomaticControl;
    public Guid SourceRuleId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Conflicto de salida detectado por el arbitro (sección 17).</summary>
public sealed class OutputConflict
{
    public Guid VariableId { get; set; }
    public List<OutputProposal> Proposals { get; set; } = new();
    public bool Resolved { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

/// <summary>Interlock explícito (sección 57).</summary>
public sealed class Interlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ExpressionNode? Condition { get; set; }
    public List<Guid> AffectedOutputs { get; set; } = new();
    public bool SafeValue { get; set; }
    public OutputPriority Priority { get; set; } = OutputPriority.Interlock;
}