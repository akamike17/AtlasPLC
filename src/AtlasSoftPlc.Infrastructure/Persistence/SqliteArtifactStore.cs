using System.Text.Json;
using AtlasSoftPlc.Application.Packages;

namespace AtlasSoftPlc.Infrastructure.Persistence;

public sealed class SqliteArtifactStore(SqliteStore store) : IArtifactStore
{
    public async Task SaveAsync(GeneratedArtifact artifact, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO GeneratedArtifacts(Id,ProgramId,ProgramHash,TargetInstanceId,Kind,FileName,Content,ArtifactHash,CreatedUtc,Status,DiagnosticsJson) VALUES($id,$program,$programHash,$target,$kind,$file,$content,$artifactHash,$created,$status,$diagnostics) ON CONFLICT(Id) DO UPDATE SET Status=$status,DiagnosticsJson=$diagnostics";
        cmd.Parameters.AddWithValue("$id", artifact.Id.ToString());
        cmd.Parameters.AddWithValue("$program", artifact.ProgramId.ToString());
        cmd.Parameters.AddWithValue("$programHash", artifact.ProgramHash);
        cmd.Parameters.AddWithValue("$target", (object?)artifact.TargetInstanceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", artifact.Kind);
        cmd.Parameters.AddWithValue("$file", artifact.FileName);
        cmd.Parameters.AddWithValue("$content", artifact.Content);
        cmd.Parameters.AddWithValue("$artifactHash", artifact.ArtifactHash);
        cmd.Parameters.AddWithValue("$created", artifact.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$status", artifact.Status);
        cmd.Parameters.AddWithValue("$diagnostics", JsonSerializer.Serialize(artifact.Diagnostics));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<GeneratedArtifact?> GetAsync(Guid artifactId, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id,ProgramId,ProgramHash,TargetInstanceId,Kind,FileName,Content,ArtifactHash,CreatedUtc,Status,DiagnosticsJson FROM GeneratedArtifacts WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", artifactId.ToString());
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<GeneratedArtifact>> GetByProgramAsync(Guid programId, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id,ProgramId,ProgramHash,TargetInstanceId,Kind,FileName,Content,ArtifactHash,CreatedUtc,Status,DiagnosticsJson FROM GeneratedArtifacts WHERE ProgramId=$program ORDER BY CreatedUtc DESC";
        cmd.Parameters.AddWithValue("$program", programId.ToString());
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<GeneratedArtifact>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    private static GeneratedArtifact Read(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var diagnostics = JsonSerializer.Deserialize<List<string>>(reader.GetString(10)) ?? new();
        return new GeneratedArtifact(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5), (byte[])reader[6], reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8)), reader.GetString(9), diagnostics);
    }
}
