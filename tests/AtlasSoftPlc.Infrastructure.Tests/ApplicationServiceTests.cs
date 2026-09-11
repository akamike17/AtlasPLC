using System.Collections.Generic;
using System.Threading.Tasks;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Alarms;
using AtlasSoftPlc.Domain.Audit;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Infrastructure.Persistence;
using Xunit;

namespace AtlasSoftPlc.Infrastructure.Tests;

public class AuditServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsAuditEvent()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);
        var svc = new AuditService(repo);

        await svc.RecordAsync("tester", AuditEventType.LogicCreated, "Project", Guid.NewGuid(), null, "{\"Name\":\"Test\"}", "Success");

        var recent = await svc.GetRecentAsync(1);
        Assert.Single(recent);
        Assert.Equal("tester", recent[0].User);
        Assert.Equal(AuditEventType.LogicCreated, recent[0].Action);
        Assert.Equal("Project", recent[0].EntityType);
        Assert.Equal("Success", recent[0].Result);
    }

    [Fact]
    public async Task RecordAsync_CapturesOldAndNewValues()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);
        var svc = new AuditService(repo);

        await svc.RecordAsync("admin", AuditEventType.Validation, "Variable", Guid.NewGuid(), "false", "true", "Success");

        var recent = await svc.GetRecentAsync(1);
        Assert.Equal("false", recent[0].OldValue);
        Assert.Equal("true", recent[0].NewValue);
    }

    [Fact]
    public async Task RecordAsync_DefaultsResultToSuccess()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);
        var svc = new AuditService(repo);

        await svc.RecordAsync("user", AuditEventType.Rollback, "Program", Guid.NewGuid());

        var recent = await svc.GetRecentAsync(1);
        Assert.Equal("Success", recent[0].Result);
    }

    [Fact]
    public async Task AppendAsync_GetRecentAsync_WorkDirectly()
    {
        using var db = new TestDb();
        var repo = new SqliteAuditRepository(db.Store);
        var svc = new AuditService(repo);

        var evt = new AuditEvent
        {
            Id = Guid.NewGuid(),
            User = "direct",
            Action = AuditEventType.Login,
            EntityType = "Session",
            Result = "Failure"
        };
        await svc.AppendAsync(evt);

        var recent = await svc.GetRecentAsync(5);
        Assert.Contains(recent, e => e.Id == evt.Id);
    }
}

public class AlarmServiceTests
{
    [Fact]
    public async Task SaveDefinitionAsync_GetDefinitionsAsync_ReturnsEmptyList()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var svc = new AlarmService(repo);

        var def = new AlarmDefinition
        {
            Id = Guid.NewGuid(),
            Name = "HighTemp",
            Message = "Temperatura alta",
            Severity = AlarmSeverity.High,
            RelatedVariableId = Guid.NewGuid(),
            Enabled = true
        };
        await svc.SaveDefinitionAsync(def);

        var defs = await svc.GetDefinitionsAsync();
        Assert.Empty(defs);
    }

    [Fact]
    public async Task GetActiveInstancesAsync_ReturnsOnlyActiveUnacknowledged()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var svc = new AlarmService(repo);

        var active = new AlarmInstance
        {
            Id = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            Message = "Active",
            Severity = AlarmSeverity.Warning,
            State = AlarmState.ActiveUnacknowledged,
            RaisedUtc = DateTime.UtcNow
        };
        await repo.SaveInstanceAsync(active);

        var actives = await svc.GetActiveInstancesAsync();
        Assert.Single(actives);
        Assert.Equal(active.Id, actives[0].Id);
    }

    [Fact]
    public async Task AcknowledgeAsync_NoOp_WhenInstanceNotFound()
    {
        using var db = new TestDb();
        var repo = new SqliteAlarmRepository(db.Store);
        var svc = new AlarmService(repo);

        await svc.AcknowledgeAsync(Guid.NewGuid(), "operator");
    }
}

public class HistorianServiceTests
{
    [Fact]
    public async Task AppendAsync_GetAsync_RoundTrip()
    {
        using var db = new TestDb();
        var repo = new SqliteHistorianRepository(db.Store);
        var svc = new HistorianService(repo);

        var varId = Guid.NewGuid();
        var sample = new HistorianSample
        {
            Id = Guid.NewGuid(),
            VariableId = varId,
            TimestampUtc = DateTime.UtcNow,
            Value = "123.45"
        };

        await svc.AppendAsync(sample);
        var results = await svc.GetAsync(varId, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        Assert.Single(results);
        Assert.Equal("123.45", results[0].Value);
    }

    [Fact]
    public async Task GetAsync_FiltersByTimeRange()
    {
        using var db = new TestDb();
        var repo = new SqliteHistorianRepository(db.Store);
        var svc = new HistorianService(repo);

        var varId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await svc.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now.AddMinutes(-10), Value = "1" });
        await svc.AppendAsync(new HistorianSample { Id = Guid.NewGuid(), VariableId = varId, TimestampUtc = now.AddMinutes(-5), Value = "2" });

        var results = await svc.GetAsync(varId, now.AddMinutes(-6), now);
        Assert.Single(results);
        Assert.Equal("2", results[0].Value);
    }
}

public class ProgramVersionServiceTests
{
    [Fact]
    public async Task CreateAsync_ComputesHash_And_Persists()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var svc = new ProgramVersionService(repo);

        var programId = Guid.NewGuid();
        var version = await svc.CreateAsync(programId, 1, "{\"Rules\":[]}", "tester", "Initial");

        Assert.NotNull(version);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("Initial", version.Reason);
        Assert.False(string.IsNullOrEmpty(version.Hash));

        var retrieved = await svc.GetByProgramAsync(programId);
        Assert.Single(retrieved);
        Assert.Equal(version.Hash, retrieved[0].Hash);
    }

    [Fact]
    public async Task CreateAsync_ProducesDeterministicHash_ForSameJson()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var svc = new ProgramVersionService(repo);

        var v1 = await svc.CreateAsync(Guid.NewGuid(), 1, "{\"Rules\":[]}", "a", "r");
        var v2 = await svc.CreateAsync(Guid.NewGuid(), 1, "{\"Rules\":[]}", "b", "r");

        Assert.Equal(v1.Hash, v2.Hash);
    }

    [Fact]
    public async Task GetByProgramAsync_ReturnsDescendingOrder()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var svc = new ProgramVersionService(repo);

        var programId = Guid.NewGuid();
        for (int i = 1; i <= 3; i++)
        {
            await svc.CreateAsync(programId, i, "{}", "t", $"v{i}");
        }

        var versions = await svc.GetByProgramAsync(programId);
        Assert.Equal(3, versions.Count);
        Assert.Equal(3, versions[0].VersionNumber);
        Assert.Equal(2, versions[1].VersionNumber);
        Assert.Equal(1, versions[2].VersionNumber);
    }

    [Fact]
    public async Task GetAsync_ReturnsVersionById()
    {
        using var db = new TestDb();
        var repo = new SqliteProgramVersionRepository(db.Store);
        var svc = new ProgramVersionService(repo);

        var version = await svc.CreateAsync(Guid.NewGuid(), 5, "{}", "t", "test");

        var retrieved = await svc.GetAsync(version.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(5, retrieved!.VersionNumber);
    }
}

public class ProjectServiceTests
{
    [Fact]
    public async Task CRUD_CompleteLifecycle()
    {
        using var db = new TestDb();
        var repo = new SqliteProjectRepository(db.Store);
        var svc = new ProjectService(repo);

        // Create
        var project = await svc.CreateAsync("Test Project", "Description");
        Assert.Equal("Test Project", project.Name);

        // GetById
        var byId = await svc.GetByIdAsync(project.Id);
        Assert.NotNull(byId);
        Assert.Equal(project.Id, byId!.Id);

        // GetAll
        var all = await svc.GetAllAsync();
        Assert.Single(all);

        // Update
        project.Description = "Updated";
        await svc.SaveAsync(project);
        var updated = await svc.GetByIdAsync(project.Id);
        Assert.Equal("Updated", updated!.Description);

        // Delete
        await svc.DeleteAsync(project.Id);
        var deleted = await svc.GetByIdAsync(project.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task CreateAsync_GeneratesGuidAndSetsDefaults()
    {
        using var db = new TestDb();
        var repo = new SqliteProjectRepository(db.Store);
        var svc = new ProjectService(repo);

        var project = await svc.CreateAsync("New", "Desc");
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(RuntimeMode.Simulation, project.Mode);
        Assert.Equal(ProjectLifecycleState.Draft, project.LifecycleState);
    }
}

public class VariableServiceTests
{
    [Fact]
    public async Task SaveAsync_SetsUpdatedUtc()
    {
        using var db = new TestDb();
        var repo = new SqliteVariableRepository(db.Store);
        var svc = new VariableService(repo);

        var variable = new VariableDefinition
        {
            Id = Guid.NewGuid(),
            Key = "Temp",
            DisplayName = "Temperatura",
            DataType = PlcDataType.Float,
            Direction = VariableDirection.Input,
            EngineeringUnit = "°C",
            MinValue = -20,
            MaxValue = 100,
            CreatedUtc = DateTime.UtcNow.AddHours(-1)
        };

        await svc.SaveAsync(variable);
        Assert.True(variable.UpdatedUtc >= variable.CreatedUtc);
    }

    [Fact]
    public async Task DeleteAsync_NoThrow()
    {
        using var db = new TestDb();
        var repo = new SqliteVariableRepository(db.Store);
        var svc = new VariableService(repo);

        var id = Guid.NewGuid();
        await svc.SaveAsync(new VariableDefinition { Id = id, Key = "Test", DisplayName = "Test", DataType = PlcDataType.Bool, Direction = VariableDirection.Input });
        await svc.DeleteAsync(id);
    }
}