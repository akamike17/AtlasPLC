using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Graph;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>
/// Commit único del diseño gráfico. No depende de los repositorios de escritura
/// individuales porque todos abrirían conexiones distintas y no podrían compartir
/// la misma transacción SQLite.
/// </summary>
public sealed class SqliteGraphApplyUnitOfWork(SqliteStore store) : IGraphApplyUnitOfWork
{
    public async Task CommitAsync(GraphDocument graph, PlcProgramDefinition program, ProgramVersion version, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(version);

        using var connection = store.OpenConnection();
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await ExecuteAsync(connection, transaction,
                "INSERT INTO ProgramGraphs(ProgramId,SchemaVersion,Json,UpdatedUtc) VALUES($id,$version,$json,$utc) ON CONFLICT(ProgramId) DO UPDATE SET SchemaVersion=$version,Json=$json,UpdatedUtc=$utc",
                new Dictionary<string, object?>
                {
                    ["$id"] = graph.ProgramId.ToString(),
                    ["$version"] = graph.SchemaVersion,
                    ["$json"] = AtlasJson.Serialize(graph),
                    ["$utc"] = DateTimeOffset.UtcNow.ToString("O")
                }, ct);

            await ExecuteAsync(connection, transaction,
                "INSERT INTO PlcPrograms(Id,Name,Json) VALUES($id,$name,$json) ON CONFLICT(Id) DO UPDATE SET Name=$name,Json=$json",
                new Dictionary<string, object?>
                {
                    ["$id"] = program.Id.ToString(),
                    ["$name"] = program.Name,
                    ["$json"] = AtlasJson.Serialize(program)
                }, ct);

            await ExecuteAsync(connection, transaction,
                "INSERT INTO LogicProgramVersions(Id,ProgramId,Json,VersionNumber,CreatedUtc) VALUES($id,$program,$json,$number,$utc) ON CONFLICT(Id) DO UPDATE SET Json=$json,VersionNumber=$number,CreatedUtc=$utc",
                new Dictionary<string, object?>
                {
                    ["$id"] = version.Id.ToString(),
                    ["$program"] = version.ProgramId.ToString(),
                    ["$json"] = AtlasJson.Serialize(version),
                    ["$number"] = version.VersionNumber,
                    ["$utc"] = version.CreatedUtc.ToString("O")
                }, ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ExecuteAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values)
            command.Parameters.AddWithValue(value.Key, value.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
