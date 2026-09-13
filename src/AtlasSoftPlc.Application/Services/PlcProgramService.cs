using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Services;

/// <summary>Contrato de repositorio de la biblioteca de programas PLC (implementado en Infrastructure).</summary>
public interface IPlcProgramRepository
{
    Task<IReadOnlyList<PlcProgramDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<PlcProgramDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(PlcProgramDefinition program, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Servicio de la biblioteca de programas PLC (sección biblioteca). Lista, guarda y carga
/// programas. No ejecuta industrial ni muta el runtime; el encendido/parada es del runtime.
/// </summary>
public sealed class PlcProgramService
{
    private readonly IPlcProgramRepository _repo;

    public PlcProgramService(IPlcProgramRepository repo) => _repo = repo;

    public Task<IReadOnlyList<PlcProgramDefinition>> GetAllAsync(CancellationToken ct = default)
        => _repo.GetAllAsync(ct);

    public Task<PlcProgramDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public async Task<PlcProgramDefinition> SaveAsync(PlcProgramDefinition program, CancellationToken ct = default)
    {
        program.UpdatedUtc = DateTime.UtcNow;
        program.Hash = ComputeHash(program);
        await _repo.SaveAsync(program, ct);
        return program;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);

    /// <summary>Si la biblioteca está vacía, siembra los programas iniciales.</summary>
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var existing = await _repo.GetAllAsync(ct);
        var templates = PlcProgramCatalog.BuildTemplateCatalog();
        var existingNames = existing.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var program in templates)
            if (!existingNames.Contains(program.Name))
            {
                program.IsTemplate = true;
                await _repo.SaveAsync(program, ct);
            }
    }

    /// <summary>Hash SHA-256 determinista del contenido SEMÁNTICO completo del programa (P0-3).</summary>
    public static string ComputeHash(PlcProgramDefinition program) =>
        CanonicalProgramHasher.ComputeHash(program);
}
