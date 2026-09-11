using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

public class RuntimeStateStoreTests
{
    [Fact]
    public void Snapshot_Initial_IsStopped()
    {
        var store = new RuntimeStateStore();
        var snap = store.Snapshot;
        Assert.Equal(RuntimeState.Stopped, snap.State);
        Assert.Equal(RuntimeMode.Simulation, snap.Mode);
        Assert.Equal(0, snap.ScanNumber);
    }

    [Fact]
    public void Update_MutatesState()
    {
        var store = new RuntimeStateStore();
        store.Update(m => m.ScanNumber = 42);

        var snap = store.Snapshot;
        Assert.Equal(42, snap.ScanNumber);
    }

    [Fact]
    public void Update_ComputesImmutableSnapshot()
    {
        var store = new RuntimeStateStore();
        store.Update(m =>
        {
            m.State = RuntimeState.Running;
            m.ScanNumber = 10;
            m.TotalScans = 100;
        });

        var snap = store.Snapshot;
        Assert.Equal(RuntimeState.Running, snap.State);
        Assert.Equal(10, snap.ScanNumber);
        Assert.Equal(100, snap.TotalScans);
    }

    [Fact]
    public void Update_IsolateFromInitial()
    {
        var store = new RuntimeStateStore();
        // Initial snapshot unaffected by later updates
        store.Update(m => m.ScanNumber = 99);
        Assert.Equal(99, store.Snapshot.ScanNumber);

        var fresh = new RuntimeStateStore();
        Assert.Equal(0, fresh.Snapshot.ScanNumber);
    }
}

public class RuntimeSnapshotTests
{
    [Fact]
    public void Initial_IsStoppedReducedDefaults()
    {
        var snap = RuntimeSnapshot.Initial;
        Assert.Equal(RuntimeState.Stopped, snap.State);
        Assert.Equal(RuntimeMode.Simulation, snap.Mode);
        Assert.Null(snap.ActiveProgramName);
        Assert.Null(snap.ActiveProgramVersion);
        Assert.Empty(snap.Inputs);
        Assert.Empty(snap.Outputs);
    }

    [Fact]
    public void ToMutable_RoundTrips_AllFields()
    {
        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [Guid.NewGuid()] = new RuntimeValue { VariableId = Guid.NewGuid(), Value = PlcValue.Bool(true) }
        };
        var snap = new RuntimeSnapshot
        {
            State = RuntimeState.Running,
            Mode = RuntimeMode.Physical,
            ActiveProgramName = "Tank",
            ActiveProgramHash = "abc",
            ActiveProgramVersion = 3,
            ScanNumber = 42,
            LastScanMs = 1.5,
            AverageScanMs = 2.0,
            MaxScanMs = 5.0,
            MinScanMs = 0.5,
            Overruns = 2,
            TotalScans = 100,
            LastCompletedUtc = DateTime.UtcNow,
            DetectedUncleanShutdown = true,
            Inputs = inputs,
            Outputs = new Dictionary<Guid, RuntimeValue>()
        };

        var mutable = snap.ToMutable();
        var back = mutable.ToImmutable();

        Assert.Equal(snap.State, back.State);
        Assert.Equal(snap.Mode, back.Mode);
        Assert.Equal(snap.ActiveProgramName, back.ActiveProgramName);
        Assert.Equal(snap.ActiveProgramHash, back.ActiveProgramHash);
        Assert.Equal(snap.ActiveProgramVersion, back.ActiveProgramVersion);
        Assert.Equal(snap.ScanNumber, back.ScanNumber);
        Assert.Equal(snap.LastScanMs, back.LastScanMs);
        Assert.Equal(snap.AverageScanMs, back.AverageScanMs);
        Assert.Equal(snap.MaxScanMs, back.MaxScanMs);
        Assert.Equal(snap.MinScanMs, back.MinScanMs);
        Assert.Equal(snap.Overruns, back.Overruns);
        Assert.Equal(snap.TotalScans, back.TotalScans);
        Assert.Equal(snap.DetectedUncleanShutdown, back.DetectedUncleanShutdown);
        Assert.Same(snap.Inputs, back.Inputs);
    }

    [Fact]
    public void ToMutable_AllowsIndependentMutation()
    {
        var snap = RuntimeSnapshot.Initial;
        var mutable = snap.ToMutable();
        mutable.ScanNumber = 999;
        mutable.State = RuntimeState.Emergency;

        // Original unchanged
        Assert.Equal(0, snap.ScanNumber);
        Assert.Equal(RuntimeState.Stopped, snap.State);

        // Mutable holds new values
        Assert.Equal(999, mutable.ScanNumber);
        Assert.Equal(RuntimeState.Emergency, mutable.State);
    }
}

public class WatchdogServiceTests
{
    [Fact]
    public void Heartbeat_KeepsAlive()
    {
        var wd = new WatchdogService();
        wd.SetTimeoutMs(5000);
        wd.Heartbeat();

        Assert.True(wd.IsAlive);
    }

    [Fact]
    public void SetTimeoutMs_AdjustsThreshold()
    {
        var wd = new WatchdogService();
        wd.SetTimeoutMs(1); // 1ms timeout
        wd.Heartbeat();
        Thread.Sleep(5);
        Assert.False(wd.IsAlive); // expired
    }

    [Fact]
    public void Classification_DefaultsToStopped()
    {
        var wd = new WatchdogService();
        Assert.Equal(RuntimeState.Stopped, wd.Classification);
    }

    [Fact]
    public void IsAlive_True_WhenRecentHeartbeat()
    {
        var wd = new WatchdogService();
        wd.SetTimeoutMs(100_000);
        wd.Heartbeat();
        Assert.True(wd.IsAlive);
    }

    [Fact]
    public void IsAlive_True_WithoutHeartbeat_WithinArmingTimeout()
    {
        // Sin heartbeat previo el plazo se mide desde el armado/construcción: dentro del
        // timeout, el watchdog está sano (el primer scan dispone del timeout completo).
        var wd = new WatchdogService();
        wd.SetTimeoutMs(100_000);
        Assert.True(wd.IsAlive);
        Assert.Null(wd.LastHeartbeatUtc);
    }

    [Fact]
    public void IsAlive_False_WhenTimeoutExpiresWithoutHeartbeat()
    {
        // Sin heartbeat y con timeout corto: al vencer el plazo, deja de estar vivo.
        var wd = new WatchdogService();
        wd.SetTimeoutMs(1);
        Thread.Sleep(10);
        Assert.False(wd.IsAlive);
    }

    [Fact]
    public void LastHeartbeatUtc_Updates_OnHeartbeat()
    {
        var wd = new WatchdogService();
        wd.Heartbeat();
        Assert.NotNull(wd.LastHeartbeatUtc);
        var first = wd.LastHeartbeatUtc!.Value;
        Thread.Sleep(5);
        wd.Heartbeat();
        Assert.True(wd.LastHeartbeatUtc!.Value >= first);
    }

    [Fact]
    public void TimeoutMs_ReportsConfiguredThreshold()
    {
        var wd = new WatchdogService();
        wd.SetTimeoutMs(250);
        Assert.Equal(250, wd.TimeoutMs);
    }

    [Fact]
    public void IsAlive_False_AfterTimeout()
    {
        var wd = new WatchdogService();
        wd.SetTimeoutMs(1);
        wd.Heartbeat();
        Thread.Sleep(10);
        Assert.False(wd.IsAlive);
    }
}

public class RuntimeCommandTests
{
    [Fact]
    public void Commands_AreRecords()
    {
        var program = new AtlasSoftPlc.Domain.Logic.LogicProgram { Id = Guid.NewGuid(), Name = "Tank" };
        var varId = Guid.NewGuid();

        RuntimeCommand[] commands = new RuntimeCommand[]
        {
            new ActivateProgramCommand(program),
            new PauseCommand(),
            new ResumeCommand(),
            new StopCommand(),
            new SetManualInputCommand(varId, PlcValue.Bool(true)),
            new ForceOutputCommand(varId, PlcValue.Bool(true), TimeSpan.FromSeconds(5)),
            new ClearForceCommand(varId),
            new SetRuntimeModeCommand(RuntimeMode.Physical)
        };

        Assert.Equal(8, commands.Length);
        Assert.IsType<ActivateProgramCommand>(commands[0]);
        Assert.Same(program, ((ActivateProgramCommand)commands[0]).Program);
        Assert.IsType<PauseCommand>(commands[1]);
        Assert.IsType<ResumeCommand>(commands[2]);
        Assert.IsType<StopCommand>(commands[3]);

        var m = Assert.IsType<SetManualInputCommand>(commands[4]);
        Assert.Equal(varId, m.VariableId);
        Assert.True(m.Value.AsBool());

        var f = Assert.IsType<ForceOutputCommand>(commands[5]);
        Assert.Equal(varId, f.VariableId);
        Assert.Equal(TimeSpan.FromSeconds(5), f.ExpiresAfter);

        Assert.IsType<ClearForceCommand>(commands[6]);
        Assert.Equal(RuntimeMode.Physical, ((SetRuntimeModeCommand)commands[7]).Mode);
    }
}

public class NullNotifierTests
{
    private sealed class NullNotifier : IRuntimeNotifier
    {
        public int SnapshotCount { get; private set; }
        public int OutputCount { get; private set; }
        public int InputCount { get; private set; }
        public int StateCount { get; private set; }

        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) { SnapshotCount++; return Task.CompletedTask; }
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) { OutputCount++; return Task.CompletedTask; }
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) { InputCount++; return Task.CompletedTask; }
        public Task NotifyStateChangedAsync(RuntimeState state) { StateCount++; return Task.CompletedTask; }
    }

    [Fact]
    public void InstallConfiguration_And_PostCommands_Work()
    {
        var store = new RuntimeStateStore();
        var wd = new WatchdogService();
        var notifier = new NullNotifier();
        var svc = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, wd, notifier);

        var program = new AtlasSoftPlc.Domain.Logic.LogicProgram
        {
            Id = Guid.NewGuid(),
            Name = "Tank",
            Rules = new List<AtlasSoftPlc.Domain.Logic.LogicRule>()
        };
        var motor = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [motor] = new VariableDefinition { Id = motor, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
        };

        // Install configuration (no throw)
        svc.InstallConfiguration(program, defs, new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [motor] = PlcValue.Bool(false) });

        // Post commands (all accepted)
        Assert.True(svc.Post(new ActivateProgramCommand(program)));
        Assert.True(svc.Post(new StopCommand()));
        Assert.True(svc.Post(new SetManualInputCommand(motor, PlcValue.Bool(true))));
        Assert.True(svc.Post(new ForceOutputCommand(motor, PlcValue.Bool(true))));
        Assert.True(svc.Post(new ClearForceCommand(motor)));
        Assert.True(svc.Post(new SetRuntimeModeCommand(RuntimeMode.Physical)));
    }

    [Fact]
    public void SetInputs_UpdatesInternalState()
    {
        var store = new RuntimeStateStore();
        var wd = new WatchdogService();
        var notifier = new NullNotifier();
        var svc = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, wd, notifier);

        var motor = Guid.NewGuid();
        svc.SetInputs(new Dictionary<Guid, RuntimeValue>
        {
            [motor] = new RuntimeValue { VariableId = motor, Value = PlcValue.Bool(true), Quality = Quality.Good }
        });

        // no exception - internal state updated
    }

    [Fact]
    public void GetOutputs_ReflejaEstadoRealDelStore()
    {
        var store = new RuntimeStateStore();
        var wd = new WatchdogService();
        var notifier = new NullNotifier();
        var svc = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, wd, notifier);

        var motor = Guid.NewGuid();
        // El snapshot inicial no tiene salidas.
        Assert.Empty(svc.GetOutputs());

        // Un scan (o force) actualiza el store con el valor real de la salida.
        store.Update(m =>
        {
            m.Outputs = new Dictionary<Guid, RuntimeValue>
            {
                [motor] = new RuntimeValue { VariableId = motor, Value = PlcValue.Bool(true), Quality = Quality.Good }
            };
        });

        var outputs = svc.GetOutputs();
        Assert.True(outputs[motor].Value.AsBool());
    }
}