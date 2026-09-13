using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

/// <summary>
/// P0-2 — Cambio transaccional de programa: Stop → failsafe → clear forces → install →
/// reset → (auto)start. Misma secuencia atómica, no encadenar comandos asíncronos.
/// Estos tests ejercitan <see cref="PlcRuntimeService.ReplaceProgram"/> sin necesidad de
/// arrancar el BackgroundService (la transición es síncrona y auto-contenida).
/// </summary>
public class ReplaceProgramTests
{
    private sealed class NullNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }

    private static (PlcRuntimeService svc, RuntimeStateStore store) NewRuntime()
    {
        var store = new RuntimeStateStore();
        var svc = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, new WatchdogService(), new NullNotifier());
        return (svc, store);
    }

    private static PlcProgramDefinition BuildProgram(string name, Guid motorId, string actionValue)
        => new()
        {
            Name = name,
            Variables = new List<VariableDefinition>
            {
                new() { Id = motorId, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output },
            },
            Logic = new LogicProgram
            {
                Name = name,
                Rules = new List<LogicRule>
                {
                    new()
                    {
                        Name = "on",
                        Priority = 100,
                        Condition = new ConstantExpression { Value = "true", DataType = "Bool" },
                        Actions = new List<LogicAction> { new SetOutputAction { VariableId = motorId, Value = actionValue } },
                    },
                },
            },
            Failsafe = new Dictionary<Guid, PlcValue> { [motorId] = PlcValue.Bool(false) },
        };

    [Fact]
    public void ReplaceProgram_Succeeds_AndInstallsNewProgram()
    {
        var (svc, store) = NewRuntime();
        var motorA = Guid.NewGuid();
        var motorB = Guid.NewGuid();

        Assert.True(svc.ReplaceProgram(BuildProgram("A", motorA, "true"), autoStart: true));

        // El snapshot queda en Running (autoStart) y con el programa A como activo.
        Assert.Equal(RuntimeState.Running, store.Snapshot.State);
        Assert.Equal("A", store.Snapshot.ActiveProgramName);

        Assert.True(svc.ReplaceProgram(BuildProgram("B", motorB, "false"), autoStart: true));
        Assert.Equal("B", store.Snapshot.ActiveProgramName);
    }

    [Fact]
    public void ReplaceProgram_ClearsForces_FromPreviousProgram()
    {
        var (svc, store) = NewRuntime();
        var motorA = Guid.NewGuid();
        var motorB = Guid.NewGuid();

        svc.ReplaceProgram(BuildProgram("A", motorA, "true"), autoStart: true);
        // Fuerza sobre A.
        svc.Post(new ForceOutputCommand(motorA, PlcValue.Bool(false)));

        // Al reemplazar a B, los forces de A no sobreviven: el force de A queda descartado.
        svc.ReplaceProgram(BuildProgram("B", motorB, "false"), autoStart: true);
        Assert.Equal("B", store.Snapshot.ActiveProgramName);

        // Un force sobre A ya no es válido: la definición de A desapareció.
        Assert.DoesNotContain(motorA, store.Snapshot.Outputs.Keys);
    }

    [Fact]
    public void ReplaceProgram_GoesThroughStoppedTransaction()
    {
        var (svc, store) = NewRuntime();
        var motor = Guid.NewGuid();

        // Con autoStart=false, queda Stopped tras instalar (estado seguro sin arrancar).
        svc.ReplaceProgram(BuildProgram("P", motor, "true"), autoStart: false);
        Assert.Equal(RuntimeState.Stopped, store.Snapshot.State);
        Assert.Equal("P", store.Snapshot.ActiveProgramName);
    }
}

/// <summary>
/// P0-1 — Snapshot atómico de I/O: estado + generación + hash, para que un bridge
/// descarte escrituras obsoletas. <see cref="PlcRuntimeService.GetIoSnapshot"/>.
/// </summary>
public class IoSnapshotTests
{
    private sealed class NullNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }

    [Fact]
    public void GetIoSnapshot_ReturnsStateGenerationAndHash_Coherently()
    {
        var store = new RuntimeStateStore();
        var svc = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, new WatchdogService(), new NullNotifier());

        var motor = Guid.NewGuid();
        svc.ReplaceProgram(new PlcProgramDefinition
        {
            Name = "P",
            Variables = new List<VariableDefinition>
            {
                new() { Id = motor, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output },
            },
            Logic = new LogicProgram { Name = "P" },
            Failsafe = new Dictionary<Guid, PlcValue> { [motor] = PlcValue.Bool(false) },
        }, autoStart: true);

        var snap1 = svc.GetIoSnapshot();
        Assert.Equal(RuntimeState.Running, snap1.State);
        Assert.NotNull(snap1.ActiveProgramHash);

        // Al reemplazar, la generación debe avanzar (anti-stale).
        svc.ReplaceProgram(new PlcProgramDefinition
        {
            Name = "P",
            Variables = new List<VariableDefinition>
            {
                new() { Id = motor, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output },
            },
            Logic = new LogicProgram { Name = "P" },
            Failsafe = new Dictionary<Guid, PlcValue> { [motor] = PlcValue.Bool(false) },
        }, autoStart: true);

        var snap2 = svc.GetIoSnapshot();
        Assert.True(snap2.Generation > snap1.Generation, "La generación debe avanzar en cada transición.");
    }

    [Fact]
    public void IsOperationalState_OnlyRunning()
    {
        Assert.True(PlcRuntimeService.IsOperationalState(RuntimeState.Running));
        Assert.False(PlcRuntimeService.IsOperationalState(RuntimeState.Stopped));
        Assert.False(PlcRuntimeService.IsOperationalState(RuntimeState.Faulted));
        Assert.False(PlcRuntimeService.IsOperationalState(RuntimeState.Emergency));
    }
}