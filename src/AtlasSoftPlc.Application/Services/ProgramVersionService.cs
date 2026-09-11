using System.Security.Cryptography;
using System.Text;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Services;

public interface IProgramVersionRepository
{
    Task<IReadOnlyList<ProgramVersion>> GetByProgramAsync(Guid programId, CancellationToken ct = default);
    Task SaveAsync(ProgramVersion version, CancellationToken ct = default);
    Task<ProgramVersion?> GetAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Servicio de versionado de programas lógicos (sección 28/43).</summary>
public sealed class ProgramVersionService
{
    private readonly IProgramVersionRepository _repo;

    public ProgramVersionService(IProgramVersionRepository repo) => _repo = repo;

    public Task<IReadOnlyList<ProgramVersion>> GetByProgramAsync(Guid programId, CancellationToken ct = default)
        => _repo.GetByProgramAsync(programId, ct);

    public Task<ProgramVersion?> GetAsync(Guid id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    /// <summary>Crea una versión nueva con hash SHA-256 de la definición JSON.</summary>
    public async Task<ProgramVersion> CreateAsync(Guid programId, int versionNumber, string definitionJson,
        string createdBy, string reason, CancellationToken ct = default)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definitionJson)));
        var version = new ProgramVersion
        {
            ProgramId = programId,
            VersionNumber = versionNumber,
            DefinitionJson = definitionJson,
            CreatedBy = createdBy,
            Reason = reason,
            Hash = hash.ToLowerInvariant()
        };
        await _repo.SaveAsync(version, ct);
        return version;
    }
}