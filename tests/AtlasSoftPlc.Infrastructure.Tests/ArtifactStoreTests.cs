using AtlasSoftPlc.Application.Packages;
using AtlasSoftPlc.Infrastructure.Persistence;

namespace AtlasSoftPlc.Infrastructure.Tests;

public sealed class ArtifactStoreTests
{
    [Fact]
    public async Task Persists_artifact_content_and_metadata()
    {
        using var db = new TestDb();
        var repository = new SqliteArtifactStore(db.Store);
        var artifact = new GeneratedArtifact(
            Guid.NewGuid(), Guid.NewGuid(), "program-hash", "siemens-planta-1", "StructuredText",
            "atlas-test.st", "PROGRAM test END_PROGRAM"u8.ToArray(), "artifact-hash",
            DateTimeOffset.UtcNow, "Generated", Array.Empty<string>());

        await repository.SaveAsync(artifact);
        var loaded = await repository.GetAsync(artifact.Id);

        Assert.NotNull(loaded);
        Assert.Equal(artifact.ProgramId, loaded.ProgramId);
        Assert.Equal(artifact.TargetInstanceId, loaded.TargetInstanceId);
        Assert.Equal(artifact.Content, loaded.Content);
        Assert.Equal(artifact.ArtifactHash, loaded.ArtifactHash);
    }
}
