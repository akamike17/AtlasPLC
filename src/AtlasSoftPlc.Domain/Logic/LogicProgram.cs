namespace AtlasSoftPlc.Domain.Logic;

/// <summary>Regla lógica del IR (sección 12).</summary>
public sealed class LogicRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public ExpressionNode? Condition { get; set; }
    public List<LogicAction> Actions { get; set; } = new();
    public List<LogicAction> ElseActions { get; set; } = new();
    public string SourceIntent { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
}

/// <summary>Programa lógico completo (IR raíz, sección 12).</summary>
public sealed class LogicProgram
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Proyecto al que pertenece el programa (relación de persistencia).</summary>
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public List<LogicRule> Rules { get; set; } = new();
    public List<Guid> TimerIds { get; set; } = new();
    public List<Guid> CounterIds { get; set; } = new();
}