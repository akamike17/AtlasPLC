using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Infrastructure.Persistence;

public sealed class SqliteGraphDocumentRepository(SqliteStore store) : IGraphDocumentRepository
{
    public async Task<GraphDocument?> GetAsync(Guid programId, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM ProgramGraphs WHERE ProgramId=$id"; cmd.Parameters.AddWithValue("$id", programId.ToString());
        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : AtlasJson.Deserialize<GraphDocument>(json);
    }

    public async Task SaveAsync(GraphDocument graph, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO ProgramGraphs(ProgramId,SchemaVersion,Json,UpdatedUtc) VALUES($id,$v,$json,$utc) ON CONFLICT(ProgramId) DO UPDATE SET SchemaVersion=$v,Json=$json,UpdatedUtc=$utc";
        cmd.Parameters.AddWithValue("$id", graph.ProgramId.ToString()); cmd.Parameters.AddWithValue("$v", graph.SchemaVersion); cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(graph)); cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
