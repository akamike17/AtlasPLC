using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>Repositorio de proyectos sobre SQLite con columna JSON.</summary>
public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly SqliteStore _store;

    public SqliteProjectRepository(SqliteStore store) => _store = store;

    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM Projects ORDER BY CreatedUtc, Id";
        var list = new List<Project>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var p = AtlasJson.Deserialize<Project>(r.GetString(0));
            if (p is not null) list.Add(p);
        }
        return list;
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM Projects WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : AtlasJson.Deserialize<Project>(json);
    }

    public async Task SaveAsync(Project project, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Projects (Id, Json, CreatedUtc) VALUES ($id, $json, $ts)
                            ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json";
        cmd.Parameters.AddWithValue("$id", project.Id.ToString());
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(project));
        cmd.Parameters.AddWithValue("$ts", project.CreatedUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Projects WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Repositorio de variables sobre SQLite.</summary>
public sealed class SqliteVariableRepository : IVariableRepository
{
    private readonly SqliteStore _store;

    public SqliteVariableRepository(SqliteStore store) => _store = store;

    public async Task<IReadOnlyList<VariableDefinition>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM Variables WHERE ProjectId = $pid";
        cmd.Parameters.AddWithValue("$pid", projectId.ToString());
        var list = new List<VariableDefinition>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var v = AtlasJson.Deserialize<VariableDefinition>(r.GetString(0));
            if (v is not null) list.Add(v);
        }
        return list;
    }

    public async Task SaveAsync(VariableDefinition variable, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Variables (Id, ProjectId, Json) VALUES ($id, $pid, $json)
                            ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json";
        cmd.Parameters.AddWithValue("$id", variable.Id.ToString());
        cmd.Parameters.AddWithValue("$pid", variable.ProjectId.ToString());
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(variable));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Variables WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Repositorio de programas lógicos sobre SQLite.</summary>
public sealed class SqliteLogicProgramRepository : ILogicProgramRepository
{
    private readonly SqliteStore _store;

    public SqliteLogicProgramRepository(SqliteStore store) => _store = store;

    public async Task<LogicProgram?> GetActiveAsync(Guid projectId, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM LogicPrograms WHERE ProjectId = $pid AND IsActive = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("$pid", projectId.ToString());
        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : AtlasJson.Deserialize<LogicProgram>(json);
    }

    public async Task SaveAsync(LogicProgram program, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO LogicPrograms (Id, ProjectId, Json, IsActive) VALUES ($id, $pid, $json, 1)
                            ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json, IsActive = 1";
        cmd.Parameters.AddWithValue("$id", program.Id.ToString());
        cmd.Parameters.AddWithValue("$pid", program.ProjectId.ToString());
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(program));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}