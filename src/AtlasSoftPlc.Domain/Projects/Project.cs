using AtlasSoftPlc.Domain.Runtime;

namespace AtlasSoftPlc.Domain.Projects;

/// <summary>Proyecto Atlas (consorcio raíz de configuración).</summary>
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RuntimeMode Mode { get; set; } = RuntimeMode.Simulation;
    public ProjectLifecycleState LifecycleState { get; set; } = ProjectLifecycleState.Draft;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? ActiveProgramVersionId { get; set; }
}

/// <summary>Versión inmutable de un programa lógico (sección 28).</summary>
public sealed class ProgramVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProgramId { get; set; }
    public int VersionNumber { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}