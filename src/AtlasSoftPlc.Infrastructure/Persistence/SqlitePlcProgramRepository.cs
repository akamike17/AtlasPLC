using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>Repositorio SQLite de la biblioteca de programas PLC (migración v4).</summary>
public sealed class SqlitePlcProgramRepository : IPlcProgramRepository
{
    private readonly SqliteStore _store;
    public SqlitePlcProgramRepository(SqliteStore store) => _store = store;

    public async Task<IReadOnlyList<PlcProgramDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM PlcPrograms ORDER BY Name, Id";
        var list = new List<PlcProgramDefinition>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var p = AtlasJson.Deserialize<PlcProgramDefinition>(r.GetString(0));
            if (p is not null) list.Add(p);
        }
        return list;
    }

    public async Task<PlcProgramDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM PlcPrograms WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : AtlasJson.Deserialize<PlcProgramDefinition>(json);
    }

    public async Task SaveAsync(PlcProgramDefinition program, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO PlcPrograms (Id, Name, Json) VALUES ($id, $name, $json)
                            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, Json = excluded.Json";
        cmd.Parameters.AddWithValue("$id", program.Id.ToString());
        cmd.Parameters.AddWithValue("$name", program.Name);
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(program));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PlcPrograms WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}