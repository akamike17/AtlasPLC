using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Application.Services;

/// <summary>Contrato de repositorio de proyectos (implementado en Infrastructure).</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Project project, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IVariableRepository
{
    Task<IReadOnlyList<VariableDefinition>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task SaveAsync(VariableDefinition variable, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ILogicProgramRepository
{
    Task<LogicProgram?> GetActiveAsync(Guid projectId, CancellationToken ct = default);
    Task SaveAsync(LogicProgram program, CancellationToken ct = default);
}

public interface IGraphDocumentRepository
{
    Task<GraphDocument?> GetAsync(Guid programId, CancellationToken ct = default);
    Task SaveAsync(GraphDocument graph, CancellationToken ct = default);
    Task DeleteAsync(Guid programId, CancellationToken ct = default);
}

public interface IProgramTargetSelectionRepository
{
    Task<string?> GetAsync(Guid programId, CancellationToken ct = default);
    Task SaveAsync(Guid programId, string targetId, CancellationToken ct = default);
}

/// <summary>
/// Unidad de persistencia atómica para aplicar un diseño gráfico. La implementación
/// mantiene gráfico, programa y versión en la misma transacción de la base de datos.
/// </summary>
public interface IGraphApplyUnitOfWork
{
    Task CommitAsync(GraphDocument graph, PlcProgramDefinition program, ProgramVersion version, CancellationToken ct = default);
}

/// <summary>Servicio de proyectos (sección 43). Delgado, sin lógica industrial.</summary>
public sealed class ProjectService
{
    private readonly IProjectRepository _repo;

    public ProjectService(IProjectRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default) => _repo.GetAllAsync(ct);
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) => _repo.GetByIdAsync(id, ct);

    public async Task<Project> CreateAsync(string name, string description, CancellationToken ct = default)
    {
        var project = new Project { Name = name, Description = description };
        await _repo.SaveAsync(project, ct);
        return project;
    }

    public Task SaveAsync(Project project, CancellationToken ct = default) => _repo.SaveAsync(project, ct);
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);
}

/// <summary>Servicio de variables (sección 43).</summary>
public sealed class VariableService
{
    private readonly IVariableRepository _repo;

    public VariableService(IVariableRepository repo) => _repo = repo;

    public Task<IReadOnlyList<VariableDefinition>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
        => _repo.GetByProjectAsync(projectId, ct);

    public Task SaveAsync(VariableDefinition variable, CancellationToken ct = default)
    {
        variable.UpdatedUtc = DateTime.UtcNow;
        return _repo.SaveAsync(variable, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);
}
