using AtlasSoftPlc.Application.Services;

namespace AtlasSoftPlc.Infrastructure.Persistence;

public sealed class SqliteProgramTargetSelectionRepository(SqliteStore store) : IProgramTargetSelectionRepository
{
    public async Task<string?> GetAsync(Guid programId, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TargetId FROM ProgramTargetSelections WHERE ProgramId=$id"; cmd.Parameters.AddWithValue("$id", programId.ToString());
        return (string?)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task SaveAsync(Guid programId, string targetId, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO ProgramTargetSelections(ProgramId,TargetId,UpdatedUtc) VALUES($id,$target,$utc) ON CONFLICT(ProgramId) DO UPDATE SET TargetId=$target,UpdatedUtc=$utc";
        cmd.Parameters.AddWithValue("$id", programId.ToString()); cmd.Parameters.AddWithValue("$target", targetId); cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
