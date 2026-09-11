using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Alarms;
using AtlasSoftPlc.Domain.Audit;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>Repositorio SQLite de alarmas.</summary>
public sealed class SqliteAlarmRepository : IAlarmRepository
{
    private readonly SqliteStore _store;
    public SqliteAlarmRepository(SqliteStore store) => _store = store;

    public async Task<IReadOnlyList<AlarmDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
    {
        // Definitions and instances share the table via JSON; we store only instances.
        return await Task.FromResult(new List<AlarmDefinition>());
    }

    public async Task SaveDefinitionAsync(AlarmDefinition def, CancellationToken ct = default)
    {
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AlarmInstance>> GetActiveInstancesAsync(CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM AlarmInstances";
        var list = new List<AlarmInstance>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var a = AtlasJson.Deserialize<AlarmInstance>(r.GetString(0));
            if (a is not null && a.State == AlarmState.ActiveUnacknowledged) list.Add(a);
        }
        return list;
    }

    public async Task SaveInstanceAsync(AlarmInstance instance, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO AlarmInstances (Id, Json) VALUES ($id, $json)
                            ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json";
        cmd.Parameters.AddWithValue("$id", instance.Id.ToString());
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(instance));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Repositorio SQLite de auditoría.</summary>
public sealed class SqliteAuditRepository : IAuditRepository
{
    private readonly SqliteStore _store;
    public SqliteAuditRepository(SqliteStore store) => _store = store;

    public async Task AppendAsync(AuditEvent evt, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO AuditEvents (Id, Json) VALUES ($id, $json)";
        cmd.Parameters.AddWithValue("$id", evt.Id.ToString());
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(evt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM AuditEvents ORDER BY Json DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", count);
        var list = new List<AuditEvent>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var e = AtlasJson.Deserialize<AuditEvent>(r.GetString(0));
            if (e is not null) list.Add(e);
        }
        return list;
    }
}

/// <summary>Repositorio SQLite de historian.</summary>
public sealed class SqliteHistorianRepository : IHistorianRepository
{
    private readonly SqliteStore _store;
    public SqliteHistorianRepository(SqliteStore store) => _store = store;

    public async Task AppendAsync(HistorianSample sample, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO HistorianSamples (Id, VariableId, TimestampUtc, Json) 
                            VALUES ($id, $vid, $ts, $json)";
        cmd.Parameters.AddWithValue("$id", sample.Id.ToString());
        cmd.Parameters.AddWithValue("$vid", sample.VariableId.ToString());
        cmd.Parameters.AddWithValue("$ts", sample.TimestampUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(sample));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<HistorianSample>> GetAsync(Guid variableId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Json FROM HistorianSamples 
                            WHERE VariableId = $vid AND TimestampUtc BETWEEN $from AND $to ORDER BY TimestampUtc";
        cmd.Parameters.AddWithValue("$vid", variableId.ToString());
        cmd.Parameters.AddWithValue("$from", from.ToString("o"));
        cmd.Parameters.AddWithValue("$to", to.ToString("o"));
        var list = new List<HistorianSample>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var s = AtlasJson.Deserialize<HistorianSample>(r.GetString(0));
            if (s is not null) list.Add(s);
        }
        return list;
    }

    public async Task<int> PruneOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HistorianSamples WHERE TimestampUtc < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("o"));
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Repositorio SQLite de versiones de programa.</summary>
public sealed class SqliteProgramVersionRepository : IProgramVersionRepository
{
    private readonly SqliteStore _store;
    public SqliteProgramVersionRepository(SqliteStore store) => _store = store;

    public async Task<IReadOnlyList<ProgramVersion>> GetByProgramAsync(Guid programId, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM LogicProgramVersions WHERE ProgramId = $pid ORDER BY VersionNumber DESC";
        cmd.Parameters.AddWithValue("$pid", programId.ToString());
        var list = new List<ProgramVersion>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var v = AtlasJson.Deserialize<ProgramVersion>(r.GetString(0));
            if (v is not null) list.Add(v);
        }
        return list;
    }

    public async Task<ProgramVersion?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM LogicProgramVersions WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : AtlasJson.Deserialize<ProgramVersion>(json);
    }

    public async Task SaveAsync(ProgramVersion version, CancellationToken ct = default)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO LogicProgramVersions (Id, ProgramId, Json, VersionNumber, CreatedUtc)
                            VALUES ($id, $pid, $json, $vn, $utc)
                            ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json";
        cmd.Parameters.AddWithValue("$id", version.Id.ToString());
        cmd.Parameters.AddWithValue("$pid", version.ProgramId.ToString());
        cmd.Parameters.AddWithValue("$json", AtlasJson.Serialize(version));
        cmd.Parameters.AddWithValue("$vn", version.VersionNumber);
        cmd.Parameters.AddWithValue("$utc", version.CreatedUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}