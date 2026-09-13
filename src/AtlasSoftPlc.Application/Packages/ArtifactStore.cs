using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Packages;

/// <summary>Artefacto generado y persistido con la identidad exacta del programa.</summary>
public sealed record GeneratedArtifact(
    Guid Id,
    Guid ProgramId,
    string ProgramHash,
    string? TargetInstanceId,
    string Kind,
    string FileName,
    byte[] Content,
    string ArtifactHash,
    DateTimeOffset CreatedUtc,
    string Status,
    IReadOnlyList<string> Diagnostics);

public interface IArtifactStore
{
    Task SaveAsync(GeneratedArtifact artifact, CancellationToken ct = default);
    Task<GeneratedArtifact?> GetAsync(Guid artifactId, CancellationToken ct = default);
    Task<IReadOnlyList<GeneratedArtifact>> GetByProgramAsync(Guid programId, CancellationToken ct = default);
}
