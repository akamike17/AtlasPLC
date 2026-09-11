using System.Threading.Channels;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Snapshots;

namespace AtlasSoftPlc.Runtime.Hosting;

/// <summary>Comandos que la UI/configuración envia al runtime (sección 46).</summary>
public abstract record RuntimeCommand;

public sealed record ActivateProgramCommand(AtlasSoftPlc.Domain.Logic.LogicProgram Program) : RuntimeCommand;
public sealed record PauseCommand : RuntimeCommand;
public sealed record ResumeCommand : RuntimeCommand;
public sealed record StopCommand : RuntimeCommand;
public sealed record SetManualInputCommand(Guid VariableId, PlcValue Value) : RuntimeCommand;
public sealed record ForceOutputCommand(Guid VariableId, PlcValue Value, TimeSpan? ExpiresAfter = null) : RuntimeCommand;
public sealed record ClearForceCommand(Guid VariableId) : RuntimeCommand;
public sealed record SetRuntimeModeCommand(RuntimeMode Mode) : RuntimeCommand;

/// <summary>
/// Estado observable del runtime. Single-writer: solo el loop de runtime lo muta.
/// La UI lee snapshots inmutables.
/// </summary>
public sealed class RuntimeStateStore
{
    private readonly object _lock = new();
    private RuntimeSnapshot _current = RuntimeSnapshot.Initial;

    public RuntimeSnapshot Snapshot
    {
        get { lock (_lock) return _current; }
    }

    public void Update(Action<RuntimeSnapshot.Mutable> mutate)
    {
        lock (_lock)
        {
            var m = _current.ToMutable();
            mutate(m);
            _current = m.ToImmutable();
        }
    }
}

public sealed class RuntimeSnapshot
{
    public RuntimeState State { get; init; } = RuntimeState.Stopped;
    public RuntimeMode Mode { get; init; } = RuntimeMode.Simulation;
    public string? ActiveProgramName { get; init; }
    public string? ActiveProgramHash { get; init; }
    public int? ActiveProgramVersion { get; init; }
    public long ScanNumber { get; init; }
    public double LastScanMs { get; init; }
    public double AverageScanMs { get; init; }
    public double MaxScanMs { get; init; }
    public double MinScanMs { get; init; }
    public long Overruns { get; init; }
    public long TotalScans { get; init; }
    public DateTime? LastCompletedUtc { get; init; }
    public bool DetectedUncleanShutdown { get; init; }
    public IReadOnlyDictionary<Guid, RuntimeValue> Inputs { get; init; } = new Dictionary<Guid, RuntimeValue>();
    public IReadOnlyDictionary<Guid, RuntimeValue> Outputs { get; init; } = new Dictionary<Guid, RuntimeValue>();
    public OutputSnapshot LastOutputs { get; init; } = OutputSnapshot.Empty;

    public static RuntimeSnapshot Initial { get; } = new RuntimeSnapshot();

    public Mutable ToMutable() => new(this);

    public sealed class Mutable
    {
        public RuntimeState State { get; set; }
        public RuntimeMode Mode { get; set; }
        public string? ActiveProgramName { get; set; }
        public string? ActiveProgramHash { get; set; }
        public int? ActiveProgramVersion { get; set; }
        public long ScanNumber { get; set; }
        public double LastScanMs { get; set; }
        public double AverageScanMs { get; set; }
        public double MaxScanMs { get; set; }
        public double MinScanMs { get; set; }
        public long Overruns { get; set; }
        public long TotalScans { get; set; }
        public DateTime? LastCompletedUtc { get; set; }
        public bool DetectedUncleanShutdown { get; set; }
        public IReadOnlyDictionary<Guid, RuntimeValue> Inputs { get; set; } = new Dictionary<Guid, RuntimeValue>();
        public IReadOnlyDictionary<Guid, RuntimeValue> Outputs { get; set; } = new Dictionary<Guid, RuntimeValue>();
        public OutputSnapshot LastOutputs { get; set; } = OutputSnapshot.Empty;

        public Mutable(RuntimeSnapshot s)
        {
            State = s.State;
            Mode = s.Mode;
            ActiveProgramName = s.ActiveProgramName;
            ActiveProgramHash = s.ActiveProgramHash;
            ActiveProgramVersion = s.ActiveProgramVersion;
            ScanNumber = s.ScanNumber;
            LastScanMs = s.LastScanMs;
            AverageScanMs = s.AverageScanMs;
            MaxScanMs = s.MaxScanMs;
            MinScanMs = s.MinScanMs;
            Overruns = s.Overruns;
            TotalScans = s.TotalScans;
            LastCompletedUtc = s.LastCompletedUtc;
            DetectedUncleanShutdown = s.DetectedUncleanShutdown;
            Inputs = s.Inputs;
            Outputs = s.Outputs;
            LastOutputs = s.LastOutputs;
        }

        public RuntimeSnapshot ToImmutable() => new()
        {
            State = State,
            Mode = Mode,
            ActiveProgramName = ActiveProgramName,
            ActiveProgramHash = ActiveProgramHash,
            ActiveProgramVersion = ActiveProgramVersion,
            ScanNumber = ScanNumber,
            LastScanMs = LastScanMs,
            AverageScanMs = AverageScanMs,
            MaxScanMs = MaxScanMs,
            MinScanMs = MinScanMs,
            Overruns = Overruns,
            TotalScans = TotalScans,
            LastCompletedUtc = LastCompletedUtc,
            DetectedUncleanShutdown = DetectedUncleanShutdown,
            Inputs = Inputs,
            Outputs = Outputs,
            LastOutputs = LastOutputs
        };
    }
}

/// <summary>Watchdog lógico (sección 19) que registra heartbeat del scan.</summary>
public sealed class WatchdogService
{
    private long _lastHeartbeatTicks = DateTime.UtcNow.Ticks;
    private long _timeoutTicks = TimeSpan.FromMilliseconds(2000).Ticks;

    public RuntimeState Classification { get; private set; } = RuntimeState.Stopped;

    public void SetTimeoutMs(double ms) => _timeoutTicks = (long)(ms * TimeSpan.TicksPerMillisecond);

    public void Heartbeat() => Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);

    public bool IsAlive => (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastHeartbeatTicks)) <= _timeoutTicks;
}