using System.Collections.Generic;
using System.Threading.Tasks;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Alarms;
using AtlasSoftPlc.Domain.Audit;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Infrastructure.Persistence;
using Xunit;

namespace AtlasSoftPlc.Infrastructure.Tests;

/// <summary>
/// Crea una SQLite en archivo temporal nuevo por instancia (DB aislada por test).
/// </summary>
public sealed class TestDb : IDisposable
{
    public SqliteStore Store { get; }
    private readonly string _dbPath;

    public TestDb()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"atlas_test_{Guid.NewGuid():N}.db");
        Store = new SqliteStore(_dbPath);
        Store.EnsureCreated();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }
}

public class SqliteAlarmRepositoryTests
{
    [Fact]
    public async Task SaveInstance_And_GetActiveInstances_RoundTrip()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var instance = new AlarmInstance
        {
            Id = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            Message = "Test alarm",
            Severity = AlarmSeverity.High,
            State = AlarmState.ActiveUnacknowledged,
            RelatedVariableId = Guid.NewGuid(),
            RaisedUtc = DateTime.UtcNow
        };

        await repo.SaveInstanceAsync(instance);
        var actives = await repo.GetActiveInstancesAsync();

        Assert.Single(actives);
        Assert.Equal(instance.Id, actives[0].Id);
        Assert.Equal(instance.Message, actives[0].Message);
        Assert.Equal(AlarmState.ActiveUnacknowledged, actives[0].State);
    }

    [Fact]
    public async Task GetActiveInstances_Filters_OnlyActiveUnacknowledged()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);

        var active = new AlarmInstance
        {
            Id = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            Message = "Active",
            Severity = AlarmSeverity.Warning,
            State = AlarmState.ActiveUnacknowledged,
            RaisedUtc = DateTime.UtcNow
        };
        var acked = new AlarmInstance
        {
            Id = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            Message = "Acked",
            Severity = AlarmSeverity.Warning,
            State = AlarmState.ActiveAcknowledged,
            RaisedUtc = DateTime.UtcNow
        };
        var cleared = new AlarmInstance
        {
            Id = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            Message = "Cleared",
            Severity = AlarmSeverity.Info,
            State = AlarmState.ClearedUnacknowledged,
            RaisedUtc = DateTime.UtcNow
        };

        await repo.SaveInstanceAsync(active);
        await repo.SaveInstanceAsync(acked);
        await repo.SaveInstanceAsync(cleared);

        var actives = await repo.GetActiveInstancesAsync();

        Assert.Single(actives);
        Assert.Equal(active.Id, actives[0].Id);
    }

    [Fact]
    public async Task GetActiveInstances_ExcludesAcknowledgedAndCleared()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);

        var acked = new AlarmInstance
        {
            Id = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            Message = "Acked",
            Severity = AlarmSeverity.High,
            State = AlarmState.ActiveAcknowledged,
            AcknowledgedUtc = DateTime.UtcNow,
            AcknowledgedBy = "op",
            RaisedUtc = DateTime.UtcNow
        };

        await repo.SaveInstanceAsync(acked);
        var actives = await repo.GetActiveInstancesAsync();
        Assert.Empty(actives);
    }

    [Fact]
    public async Task GetDefinitionsAsync_ReturnsEmpty_List()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var defs = await repo.GetDefinitionsAsync();
        Assert.Empty(defs);
    }

    [Fact]
    public async Task SaveDefinitionAsync_Persiste_Y_Recupera()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var def = new AlarmDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Presión alta",
            Message = "La presión supera el umbral",
            Severity = AlarmSeverity.High,
            RelatedVariableId = Guid.NewGuid(),
        };

        await repo.SaveDefinitionAsync(def);
        var defs = await repo.GetDefinitionsAsync();

        Assert.Single(defs);
        Assert.Equal(def.Id, defs[0].Id);
        Assert.Equal("Presión alta", defs[0].Name);
        Assert.Equal(AlarmSeverity.High, defs[0].Severity);
    }

    [Fact]
    public async Task SaveDefinitionAsync_ActualizaExistente_PorId()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var id = Guid.NewGuid();
        await repo.SaveDefinitionAsync(new AlarmDefinition { Id = id, Name = "V1", Severity = AlarmSeverity.Info });
        await repo.SaveDefinitionAsync(new AlarmDefinition { Id = id, Name = "V2", Severity = AlarmSeverity.Critical });

        var defs = await repo.GetDefinitionsAsync();
        Assert.Single(defs);
        Assert.Equal("V2", defs[0].Name);
        Assert.Equal(AlarmSeverity.Critical, defs[0].Severity);
    }
}

public class SqliteAuditRepositoryTests
{
    [Fact]
    public async Task AppendAsync_And_GetRecentAsync_RoundTrip()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);
        var evt = new AuditEvent
        {
            Id = Guid.NewGuid(),
            User = "tester",
            Action = AuditEventType.LogicCreated,
            EntityType = "Project",
            EntityId = Guid.NewGuid(),
            OldValue = null,
            NewValue = "{\"Name\":\"Test\"}",
            Result = "Success"
        };

        await repo.AppendAsync(evt);
        var recent = await repo.GetRecentAsync(10);

        Assert.Single(recent);
        Assert.Equal(evt.Id, recent[0].Id);
        Assert.Equal("tester", recent[0].User);
        Assert.Equal(AuditEventType.LogicCreated, recent[0].Action);
    }

    [Fact]
    public async Task GetRecentAsync_LimitsResults()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);
        for (int i = 0; i < 15; i++)
        {
            await repo.AppendAsync(new AuditEvent
            {
                Id = Guid.NewGuid(),
                User = "u",
                Action = AuditEventType.Validation,
                EntityType = "X",
                Result = "Success"
            });
        }

        var recent = await repo.GetRecentAsync(5);
        Assert.Equal(5, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_OrdenaPorFechaReal_NoPorJson()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);

        var viejo = new AuditEvent { Id = Guid.NewGuid(), User = "u1", Action = AuditEventType.Validation, EntityType = "X", Result = "Success", TimestampUtc = DateTime.UtcNow.AddHours(-5) };
        var nuevo = new AuditEvent { Id = Guid.NewGuid(), User = "u2", Action = AuditEventType.Validation, EntityType = "X", Result = "Success", TimestampUtc = DateTime.UtcNow };

        // Insertamos el más reciente PRIMERO para que cualquier orden por Id/Json falle.
        await repo.AppendAsync(nuevo);
        await repo.AppendAsync(viejo);

        var recent = await repo.GetRecentAsync(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal(nuevo.Id, recent[0].Id); // el más reciente primero (cronológico)
        Assert.Equal(viejo.Id, recent[1].Id);
    }
}

public class SqliteHistorianRepositoryTests
{
    [Fact]
    public async Task AppendAsync_And_GetAsync_RoundTrip()
    {
        using var db = new TestDb();
        var repo = new SqliteHistorianRepository(db.Store);
        var varId = Guid.NewGuid();
        var sample = new HistorianSample
        {
            Id = Guid.NewGuid(),
            VariableId = varId,
            TimestampUtc = DateTime.UtcNow,
            Value = "42.5"
        };

        await repo.AppendAsync(sample);
        var results = await repo.GetAsync(varId, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        Assert.Single(results);
        Assert.Equal(varId, results[0].VariableId);
        Assert.Equal("42.5", results[0].Value);
    }

    [Fact]
    public async Task GetAsync_FiltersByTimeRange()
    {
        using var db = new TestDb();
        var repo = new SqliteHistorianRepository(db.Store);
        var varId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now.AddMinutes(-10), Value = "1" });
        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now.AddMinutes(-5), Value = "2" });
        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now.AddMinutes(5), Value = "3" });

        var results = await repo.GetAsync(varId, now.AddMinutes(-6), now);
        Assert.Single(results);
        Assert.Equal("2", results[0].Value);
    }

    [Fact]
    public async Task GetAsync_FiltersByVariableId()
    {
        using var db = new TestDb();
        var repo = new SqliteHistorianRepository(db.Store);
        var var1 = Guid.NewGuid();
        var var2 = Guid.NewGuid();

        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = var1, TimestampUtc = DateTime.UtcNow, Value = "1" });
        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = var2, TimestampUtc = DateTime.UtcNow, Value = "2" });

        var results = await repo.GetAsync(var1, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Single(results);
        Assert.Equal(var1, results[0].VariableId);
    }

    [Fact]
    public async Task PruneOlderThan_EliminaSoloAntiguas()
    {
        using var db = new TestDb();
        var repo = new SqliteHistorianRepository(db.Store);
        var varId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now.AddDays(-40), Value = "old" });
        await repo.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now, Value = "new" });

        var pruned = await repo.PruneOlderThanAsync(now.AddDays(-30));

        Assert.Equal(1, pruned);
        var remaining = await repo.GetAsync(varId, now.AddDays(-60), now.AddDays(1));
        Assert.Single(remaining);
        Assert.Equal("new", remaining[0].Value);
    }
}

public class SqliteProgramVersionRepositoryTests
{
    [Fact]
    public async Task SaveAsync_And_GetAsync_RoundTrip()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var programId = Guid.NewGuid();
        var version = new ProgramVersion
        {
            Id = Guid.NewGuid(),
            ProgramId = programId,
            VersionNumber = 1,
            DefinitionJson = "{\"Rules\":[]}",
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = "tester",
            Reason = "Initial",
            Hash = "abc123"
        };

        await repo.SaveAsync(version);
        var retrieved = await repo.GetAsync(version.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(version.Id, retrieved!.Id);
        Assert.Equal(1, retrieved.VersionNumber);
        Assert.Equal("Initial", retrieved.Reason);
    }

    [Fact]
    public async Task GetByProgramAsync_ReturnsVersions_Descending()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var programId = Guid.NewGuid();

        var v1 = new ProgramVersion { Id = Guid.NewGuid(), ProgramId = programId, VersionNumber = 1, DefinitionJson = "{}", CreatedUtc = DateTime.UtcNow.AddMinutes(-10), CreatedBy = "a", Reason = "v1", Hash = "h1" };
        var v2 = new ProgramVersion { Id = Guid.NewGuid(), ProgramId = programId, VersionNumber = 2, DefinitionJson = "{}", CreatedUtc = DateTime.UtcNow.AddMinutes(-5), CreatedBy = "b", Reason = "v2", Hash = "h2" };
        var v3 = new ProgramVersion { Id = Guid.NewGuid(), ProgramId = programId, VersionNumber = 3, DefinitionJson = "{}", CreatedUtc = DateTime.UtcNow, CreatedBy = "c", Reason = "v3", Hash = "h3" };

        await repo.SaveAsync(v1);
        await repo.SaveAsync(v2);
        await repo.SaveAsync(v3);

        var versions = await repo.GetByProgramAsync(programId);
        Assert.Equal(3, versions.Count);
        Assert.Equal(3, versions[0].VersionNumber);
        Assert.Equal(2, versions[1].VersionNumber);
        Assert.Equal(1, versions[2].VersionNumber);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var result = await repo.GetAsync(Guid.NewGuid());
        Assert.Null(result);
    }
}

public class SqliteProjectRepositoryTests
{
    [Fact]
    public async Task SaveAsync_GetByIdAsync_GetAllAsync_RoundTrip()
    {
        using var db = new TestDb();
        var repo = new SqliteProjectRepository(db.Store);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Test Project",
            Description = "Description",
            Mode = RuntimeMode.Simulation,
            LifecycleState = ProjectLifecycleState.Draft,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await repo.SaveAsync(project);
        var byId = await repo.GetByIdAsync(project.Id);
        var all = await repo.GetAllAsync();

        Assert.NotNull(byId);
        Assert.Equal(project.Name, byId!.Name);
        Assert.Single(all);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProject()
    {
        using var db = new TestDb();
        var repo = new SqliteProjectRepository(db.Store);
        var project = new Project { Id = Guid.NewGuid(), Name = "ToDelete", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
        await repo.SaveAsync(project);
        await repo.DeleteAsync(project.Id);

        var byId = await repo.GetByIdAsync(project.Id);
        var all = await repo.GetAllAsync();

        Assert.Null(byId);
        Assert.Empty(all);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExisting_OnConflict()
    {
        using var db = new TestDb();
        var repo = new SqliteProjectRepository(db.Store);
        var id = Guid.NewGuid();
        await repo.SaveAsync(new Project { Id = id, Name = "Original", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });
        await repo.SaveAsync(new Project { Id = id, Name = "Updated", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });

        var byId = await repo.GetByIdAsync(id);
        Assert.Equal("Updated", byId!.Name);
    }
}

public class SqliteVariableRepositoryTests
{
    [Fact]
    public async Task SaveVariable_ConProjectId_RecuperaPorProyecto()
    {
        using var db = new TestDb();
        var repo = new SqliteVariableRepository(db.Store);
        var projA = Guid.NewGuid();
        var projB = Guid.NewGuid();

        var v1 = new VariableDefinition { Id = Guid.NewGuid(), ProjectId = projA, Key = "Low", DisplayName = "Nivel bajo", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var v2 = new VariableDefinition { Id = Guid.NewGuid(), ProjectId = projB, Key = "Temp", DisplayName = "Temperatura", DataType = PlcDataType.Float, Direction = VariableDirection.Input };

        await repo.SaveAsync(v1);
        await repo.SaveAsync(v2);

        var ofA = await repo.GetByProjectAsync(projA);
        var ofB = await repo.GetByProjectAsync(projB);

        Assert.Single(ofA);
        Assert.Equal("Low", ofA[0].Key);
        Assert.Single(ofB);
        Assert.Equal("Temp", ofB[0].Key);
    }

    [Fact]
    public async Task SaveVariable_SinProjectId_NoContaminaOtrosProyectos()
    {
        using var db = new TestDb();
        var repo = new SqliteVariableRepository(db.Store);
        var projA = Guid.NewGuid();

        var v1 = new VariableDefinition { Id = Guid.NewGuid(), ProjectId = projA, Key = "Real", DisplayName = "Real", DataType = PlcDataType.Bool };
        await repo.SaveAsync(v1);

        // Otra variable en un proyecto distinto (B) no debe aparecer en A.
        var ofA = await repo.GetByProjectAsync(projA);
        Assert.Single(ofA);
        Assert.Equal("Real", ofA[0].Key);
    }
}

public class SqliteLogicProgramRepositoryTests
{
    [Fact]
    public async Task SaveProgram_ConProjectId_RecuperaActivoPorProyecto()
    {
        using var db = new TestDb();
        var repo = new SqliteLogicProgramRepository(db.Store);
        var projA = Guid.NewGuid();
        var projB = Guid.NewGuid();

        var p1 = new LogicProgram { Id = Guid.NewGuid(), ProjectId = projA, Name = "Tanque A" };
        var p2 = new LogicProgram { Id = Guid.NewGuid(), ProjectId = projB, Name = "Tanque B" };

        await repo.SaveAsync(p1);
        await repo.SaveAsync(p2);

        var activeA = await repo.GetActiveAsync(projA);
        var activeB = await repo.GetActiveAsync(projB);

        Assert.NotNull(activeA);
        Assert.Equal("Tanque A", activeA!.Name);
        Assert.NotNull(activeB);
        Assert.Equal("Tanque B", activeB!.Name);
    }
}

public class SqliteStoreTests
{
    [Fact]
    public void EnsureCreated_CreatesAllTables()
    {
        using var db = new TestDb();
        using var conn = db.Store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));

        var expected = new[] { "AlarmInstances", "AuditEvents", "HistorianSamples", "LogicProgramVersions", "LogicPrograms", "Projects", "Variables", "Devices", "TagBindings", "Settings" };
        foreach (var t in expected) Assert.Contains(t, tables);
    }

    [Fact]
    public void OpenConnection_ReturnsOpenConnection()
    {
        using var db = new TestDb();
        using var conn = db.Store.OpenConnection();
        Assert.Equal(System.Data.ConnectionState.Open, conn.State);
    }
}