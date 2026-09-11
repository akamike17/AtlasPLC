using System.Diagnostics;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

/// <summary>
/// Pruebas de las transiciones operacionales del runtime: STOP/PAUSE/FAULT/shutdown
/// llevan las salidas a failsafe (P0-1), force completo (P0-3) y watchdog (P0-4).
/// </summary>
public class PlcRuntimeServiceFailsafeTests
{
    private sealed class CountingNotifier : IRuntimeNotifier
    {
        public int OutputChanges;
        public int StateChanges;

        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) { OutputChanges++; return Task.CompletedTask; }
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) { StateChanges++; return Task.CompletedTask; }
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 3000, string? reason = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            Thread.Sleep(10);
        }
        throw new Xunit.Sdk.XunitException("Timed out waiting for condition: " + (reason ?? "unknown"));
    }

    private static (PlcRuntimeService svc, RuntimeStateStore store, CountingNotifier notifier, WatchdogService wd)
        CreateService()
    {
        var store = new RuntimeStateStore();
        var wd = new WatchdogService();
        var notifier = new CountingNotifier();
        var svc = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, wd, notifier);
        return (svc, store, notifier, wd);
    }

    /// <summary>Motor ON: condición siempre verdadera encenderá el motor en cada scan.</summary>
    private static (LogicProgram program, Dictionary<Guid, VariableDefinition> defs, Guid motor, Dictionary<Guid, PlcValue> failsafe)
        BuildMotorProgram()
    {
        var motor = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [motor] = new VariableDefinition { Id = motor, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
        };
        var program = new LogicProgram
        {
            Name = "Motor",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "AlwaysOn",
                    Priority = 10,
                    Condition = null, // siempre verdadera
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor, Value = "true" } }
                }
            }
        };
        var failsafe = new Dictionary<Guid, PlcValue> { [motor] = PlcValue.Bool(false) };
        return (program, defs, motor, failsafe);
    }

    [Fact]
    public async Task OutputOn_ThenStop_Failsafe()
    {
        var (svc, store, _, wd) = CreateService();
        var (program, defs, motor, failsafe) = BuildMotorProgram();

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));

        await svc.StartAsync(CancellationToken.None);

        // Esperar a que el scan encienda el motor.
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "output ON tras Activate");

        Assert.True(store.Snapshot.Outputs[motor].Value.AsBool());

        // Stop -> failsafe
        svc.Post(new StopCommand());
        WaitUntil(() => store.Snapshot.State == RuntimeState.Stopped &&
                       store.Snapshot.Outputs.TryGetValue(motor, out var o) && !o.Value.AsBool(),
            reason: "output failsafe tras Stop");

        Assert.False(store.Snapshot.Outputs[motor].Value.AsBool());

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OutputOn_ThenPause_Failsafe()
    {
        var (svc, store, _, _) = CreateService();
        var (program, defs, motor, failsafe) = BuildMotorProgram();

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));
        await svc.StartAsync(CancellationToken.None);

        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "output ON");

        svc.Post(new PauseCommand());
        WaitUntil(() => store.Snapshot.State == RuntimeState.Stopped &&
                       store.Snapshot.Outputs.TryGetValue(motor, out var o) && !o.Value.AsBool(),
            reason: "output failsafe tras Pause");

        Assert.False(store.Snapshot.Outputs[motor].Value.AsBool());

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MultipleOutputs_DistinctFailsafe()
    {
        var (svc, store, _, _) = CreateService();

        var pump = Guid.NewGuid();
        var valve = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [pump] = new VariableDefinition { Id = pump, Key = "Pump", DataType = PlcDataType.Bool, Direction = VariableDirection.Output },
            [valve] = new VariableDefinition { Id = valve, Key = "Valve", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
        };
        var program = new LogicProgram
        {
            Name = "Tank",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "PumpOn",
                    Priority = 10,
                    Condition = null,
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump, Value = "true" } }
                },
                new LogicRule
                {
                    Name = "ValveOn",
                    Priority = 10,
                    Condition = null,
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = valve, Value = "true" } }
                }
            }
        };
        var failsafe = new Dictionary<Guid, PlcValue>
        {
            [pump] = PlcValue.Bool(false),
            [valve] = PlcValue.Bool(true) // valve failsafe = OPEN (distinto)
        };

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));
        await svc.StartAsync(CancellationToken.None);

        WaitUntil(() => store.Snapshot.Outputs.Count == 2 &&
                       store.Snapshot.Outputs.TryGetValue(pump, out var p) && p.Value.AsBool(),
            reason: "ambos outputs ON");

        svc.Post(new StopCommand());
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(pump, out var p) && !p.Value.AsBool() &&
                       store.Snapshot.Outputs.TryGetValue(valve, out var v) && v.Value.AsBool(),
            reason: "failsafe distintos por output");

        Assert.False(store.Snapshot.Outputs[pump].Value.AsBool());
        Assert.True(store.Snapshot.Outputs[valve].Value.AsBool()); // failsafe = true

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Shutdown_AppliesFailsafe()
    {
        var (svc, store, _, _) = CreateService();
        var (program, defs, motor, failsafe) = BuildMotorProgram();

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));
        await svc.StartAsync(CancellationToken.None);

        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "output ON");

        // StopAsync invoca ShutdownAsync -> failsafe.
        await svc.StopAsync(CancellationToken.None);

        Assert.False(store.Snapshot.Outputs[motor].Value.AsBool());
    }

    [Fact]
    public async Task Force_OnOff_Clear_Expiration()
    {
        var (svc, store, _, _) = CreateService();
        var (program, defs, motor, failsafe) = BuildMotorProgram();

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));
        await svc.StartAsync(CancellationToken.None);

        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "output ON por lógica");

        // Force OFF: la lógica dice ON, el force lo lleva a OFF.
        svc.Post(new ForceOutputCommand(motor, PlcValue.Bool(false)));
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && !o.Value.AsBool(),
            reason: "force OFF aplicado");

        Assert.False(store.Snapshot.Outputs[motor].Value.AsBool());

        // ClearForce: restaura control automático (vuelve a ON en el siguiente scan).
        svc.Post(new ClearForceCommand(motor));
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "clear force restaura control automático");

        Assert.True(store.Snapshot.Outputs[motor].Value.AsBool());

        // Force con expiración corta: OFF ahora, vuelve a ON al expirar.
        svc.Post(new ForceOutputCommand(motor, PlcValue.Bool(false), TimeSpan.FromMilliseconds(100)));
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && !o.Value.AsBool(),
            reason: "force OFF aplicado (con expiración)");
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "force expirado -> control automático");

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Force_InvalidType_Ignored()
    {
        var (svc, store, _, _) = CreateService();
        var (program, defs, motor, failsafe) = BuildMotorProgram();

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));
        await svc.StartAsync(CancellationToken.None);

        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "output ON");

        // Tipo incorrecto (Int32 en vez de Bool) -> ignorado, sigue ON.
        svc.Post(new ForceOutputCommand(motor, PlcValue.Int32(1)));
        Thread.Sleep(100); // dar tiempo al loop de procesar el comando

        Assert.True(store.Snapshot.Outputs[motor].Value.AsBool());

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Force_NonexistentOutput_Ignored()
    {
        var (svc, store, _, _) = CreateService();
        var (program, defs, motor, failsafe) = BuildMotorProgram();

        svc.InstallConfiguration(program, defs, new List<Interlock>(), failsafe);
        svc.Post(new ActivateProgramCommand(program));
        await svc.StartAsync(CancellationToken.None);

        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(),
            reason: "output ON");

        // Output inexistente -> ignorado (no lanza, no fuerza).
        svc.Post(new ForceOutputCommand(Guid.NewGuid(), PlcValue.Bool(false)));
        Thread.Sleep(100);

        Assert.True(store.Snapshot.Outputs[motor].Value.AsBool());

        await svc.StopAsync(CancellationToken.None);
    }
}