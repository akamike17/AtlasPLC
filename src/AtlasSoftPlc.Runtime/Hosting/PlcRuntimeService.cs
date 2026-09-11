using System.Diagnostics;
using AtlasSoftPlc.Domain.Audit;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtlasSoftPlc.Runtime.Hosting;

/// <summary>
/// Servicio de runtime del scan (sección 10). BackgroundService que ejecuta
/// el ciclo de scan de forma single-writer. Nunca desde un Controller MVC.
/// </summary>
public sealed class PlcRuntimeService : BackgroundService
{
    private readonly ILogger<PlcRuntimeService> _logger;
    private readonly RuntimeStateStore _store;
    private readonly WatchdogService _watchdog;
    private readonly IRuntimeNotifier _notifier;
    private readonly IRuntimeAuditSink? _audit;
    private readonly ScanCoordinator _coordinator;
    private readonly System.Threading.Channels.Channel<RuntimeCommand> _commands =
        System.Threading.Channels.Channel.CreateUnbounded<RuntimeCommand>();

    private readonly Stopwatch _monotonic = new();
    private readonly Stopwatch _scanWatch = new();

    private LogicProgram? _activeProgram;
    private string? _activeProgramHash;
    private volatile IReadOnlyDictionary<Guid, VariableDefinition> _definitions = new Dictionary<Guid, VariableDefinition>();
    private Dictionary<Guid, RuntimeValue> _inputs = new();
    private Dictionary<Guid, RuntimeValue> _memory = new();
    private IReadOnlyCollection<Interlock> _interlocks = new List<Interlock>();
    private volatile IReadOnlyDictionary<Guid, PlcValue> _failsafeValues = new Dictionary<Guid, PlcValue>();
    private volatile IReadOnlyDictionary<Guid, PlcDataType> _outputTypes = new Dictionary<Guid, PlcDataType>();
    private readonly Dictionary<Guid, ForcedOutput> _forcedOutputs = new();

    private volatile RuntimeState _state = RuntimeState.Stopped;
    private RuntimeMode _mode = RuntimeMode.Simulation;

    private double _targetScanMs = 50;
    private double _avgScanMs;
    private double _maxScanMs = double.MinValue;
    private double _minScanMs = double.MaxValue;
    private long _overruns;
    private long _totalScans;
    private DateTime? _lastCompleted;

    private IReadOnlyDictionary<Guid, RuntimeValue> _lastInputs = new Dictionary<Guid, RuntimeValue>();
    private volatile IReadOnlyDictionary<Guid, RuntimeValue> _lastOutputs = new Dictionary<Guid, RuntimeValue>();

    public PlcRuntimeService(
        ILogger<PlcRuntimeService> logger,
        RuntimeStateStore store,
        WatchdogService watchdog,
        IRuntimeNotifier notifier,
        IRuntimeAuditSink? audit = null,
        ScanCoordinator? coordinator = null)
    {
        _logger = logger;
        _store = store;
        _watchdog = watchdog;
        _notifier = notifier;
        _audit = audit;
        _coordinator = coordinator ?? new ScanCoordinator();
    }

    public System.Threading.Channels.Channel<RuntimeCommand> Commands => _commands;

    public bool Post(RuntimeCommand command) => _commands.Writer.TryWrite(command);

    /// <summary>Instala la configuración de un programa (validada externamente) de forma atómica.</summary>
    public void InstallConfiguration(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> definitions,
        IReadOnlyCollection<Interlock> interlocks,
        IReadOnlyDictionary<Guid, PlcValue> failsafeValues)
    {
        // atomic swap (sección 48): construimos todo nuevo y lo intercambiamos
        var newProgram = program;
        var newDefs = definitions;
        var newInterlocks = interlocks;
        var newFailsafe = failsafeValues;

        var outputTypes = new Dictionary<Guid, PlcDataType>();
        foreach (var d in newDefs.Values)
            if (d.Direction == VariableDirection.Output)
                outputTypes[d.Id] = d.DataType;

        // swap atómico bajo lock del store
        _store.Update(m => { });
        lock (_coordinator)
        {
            _activeProgram = newProgram;
            _activeProgramHash = ComputeProgramHash(newProgram);
            _definitions = newDefs;
            _interlocks = newInterlocks;
            _failsafeValues = new Dictionary<Guid, PlcValue>(newFailsafe);
            _outputTypes = outputTypes;
            _memory = new Dictionary<Guid, RuntimeValue>();
            _inputs = new Dictionary<Guid, RuntimeValue>();
            _forcedOutputs.Clear();
        }

        _logger.LogInformation("Configuración instalada: {Program} v{Version}", newProgram.Name, newProgram.Version);
    }

    public void SetInputs(IReadOnlyDictionary<Guid, RuntimeValue> inputs)
    {
        lock (_coordinator)
        {
            foreach (var kv in inputs)
                _inputs[kv.Key] = kv.Value;
        }
    }

    /// <summary>Snapshot inmutable de las salidas actuales del runtime (para la UI).</summary>
    public IReadOnlyDictionary<Guid, RuntimeValue> GetOutputs() => _store.Snapshot.Outputs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlcRuntimeService iniciando");
        _monotonic.Start();
        _scanWatch.Start();
        _state = RuntimeState.Stopped;

        // Watchdog independiente: detecta scan bloqueado desde un hilo del thread-pool,
        // no desde este loop (Riesgo 1 corregido).
        _watchdog.OnTimeout = HandleWatchdogTimeout;
        _watchdog.Start();

        // detectar shutdown sucio (sección 86): si hay un flag previo, marcarlo
        // (persistencia real del flag se implementa en Infrastructure).

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_targetScanMs));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // procesar comandos
                while (_commands.Reader.TryRead(out var cmd))
                    await ProcessCommand(cmd, stoppingToken);

                if (_state == RuntimeState.Running)
                {
                    _watchdog.Armed = true;
                    RunScan();
                }
                else
                {
                    _watchdog.Armed = false;
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown normal
        }

        await ShutdownAsync();
    }

    /// <summary>
    /// Transición a Faulted + failsafe disparada por el watchdog independiente. NO adquiere
    /// <c>_coordinator</c> ni ningún otro recurso que un scan bloqueado pueda retener:
    /// aplica el failsafe por la ruta lock-free. Así el watchdog cumple el failsafe aunque
    /// el scan vigilado esté colgado dentro de <c>lock(_coordinator)</c>.
    /// </summary>
    private void HandleWatchdogTimeout()
    {
        if (_state != RuntimeState.Running) return;
        _logger.LogError("Watchdog: scan superó el timeout ({Timeout}ms). Faulted + failsafe.",
            _watchdog.TimeoutMs);
        _state = RuntimeState.Faulted;
        _watchdog.Armed = false;

        // Failsafe lock-free: no depende del lock del coordinator retenido por el scan.
        ApplyFailsafeOutputsLockFree("watchdog timeout");

        _ = _notifier.NotifyStateChangedAsync(_state);
        Audit(AuditEventType.Fault, "Runtime", null, null, "watchdog timeout", "Faulted");
    }

    private void RunScan()
    {
        var deltaMs = _monotonic.Elapsed.TotalMilliseconds;
        _monotonic.Restart();

        _scanWatch.Restart();
        try
        {
            if (_activeProgram is null)
                return;

            lock (_coordinator)
            {
                var snapshotInputs = new Dictionary<Guid, RuntimeValue>(_inputs);
                var forcedProposals = GetActiveForceProposals();
                var request = ScanRequest.Create(
                    _activeProgram,
                    _definitions,
                    snapshotInputs,
                    _memory,
                    _interlocks,
                    _failsafeValues,
                    _outputTypes,
                    deltaMs,
                    forcedProposals);
                var result = _coordinator.Scan(request);

                _memory = result.Memory.Values.ToDictionary(k => k.Key, v => new RuntimeValue
                {
                    VariableId = v.Value.VariableId,
                    Value = v.Value.Value,
                    Quality = v.Value.Quality,
                    Source = v.Value.Source,
                    TimestampUtc = v.Value.TimestampUtc,
                    SequenceNumber = v.Value.SequenceNumber
                });

                _lastInputs = result.Inputs.Values;
                _lastOutputs = result.Outputs.Values;

                // Notificar cambios de salida individual
                foreach (var outKv in result.Outputs.Values)
                {
                    _ = _notifier.NotifyOutputChangedAsync(outKv.Key, outKv.Value.Value);
                }

                _totalScans++;
                _lastCompleted = DateTime.UtcNow;
            }

            _scanWatch.Stop();
            var elapsedMs = _scanWatch.Elapsed.TotalMilliseconds;
            UpdateMetrics(elapsedMs);

            // Heartbeat de scan COMPLETADO correctamente (P0-4): no es "loop vivo".
            _watchdog.Heartbeat();
        }
        catch (Exception ex)
        {
            _scanWatch.Stop();
            _logger.LogError(ex, "Error en el scan");
            _state = RuntimeState.Faulted;
            ApplyFailsafeOutputs("scan fault");
            UpdateMetrics(_scanWatch.Elapsed.TotalMilliseconds);
            _ = _notifier.NotifyStateChangedAsync(_state);
            Audit(AuditEventType.Fault, "Runtime", null, null, ex.Message, "Faulted");
        }
    }

    private void UpdateMetrics(double elapsedMs)
    {
        _avgScanMs = _avgScanMs == 0 ? elapsedMs : (_avgScanMs * 0.9 + elapsedMs * 0.1);
        _maxScanMs = Math.Max(_maxScanMs, elapsedMs);
        _minScanMs = _minScanMs == double.MaxValue ? elapsedMs : Math.Min(_minScanMs, elapsedMs);
        if (elapsedMs > _targetScanMs) _overruns++;

        _store.Update(m =>
        {
            m.State = _state;
            m.Mode = _mode;
            m.ActiveProgramName = _activeProgram?.Name;
            m.ActiveProgramHash = _activeProgramHash;
            m.ActiveProgramVersion = _activeProgram?.Version;
            m.ScanNumber = _totalScans;
            m.LastScanMs = elapsedMs;
            m.AverageScanMs = _avgScanMs;
            m.MaxScanMs = _maxScanMs;
            m.MinScanMs = _minScanMs;
            m.Overruns = _overruns;
            m.TotalScans = _totalScans;
            m.LastCompletedUtc = _lastCompleted;
            m.Inputs = _lastInputs;
            m.Outputs = _lastOutputs;
        });

        // Notificar snapshot completo a SignalR (cada scan)
        _ = _notifier.NotifySnapshotAsync(MapToDto(_store.Snapshot));
    }

    private static RuntimeSnapshotDto MapToDto(RuntimeSnapshot s) => new()
    {
        State = s.State,
        Mode = s.Mode,
        ActiveProgramName = s.ActiveProgramName,
        ActiveProgramHash = s.ActiveProgramHash,
        ActiveProgramVersion = s.ActiveProgramVersion ?? 0,
        ScanNumber = s.ScanNumber,
        LastScanMs = s.LastScanMs,
        AverageScanMs = s.AverageScanMs,
        MaxScanMs = s.MaxScanMs,
        MinScanMs = s.MinScanMs,
        Overruns = s.Overruns,
        TotalScans = s.TotalScans,
        LastCompletedUtc = s.LastCompletedUtc,
        Inputs = s.Inputs.ToDictionary(k => k.Key, v => v.Value),
        Outputs = s.Outputs.ToDictionary(k => k.Key, v => v.Value)
    };

    /// <summary>Calcula un hash SHA-256 determinista de la configuración del programa activo.</summary>
    private static string ComputeProgramHash(LogicProgram program)
    {
        // Hash determinista basado en nombre, versión y reglas (para el MVP, suficiente
        // para verificar integridad de la configuración instalada).
        using var sha = System.Security.Cryptography.SHA256.Create();
        var sb = new System.Text.StringBuilder();
        sb.Append(program.Name).Append('|').Append(program.Version).Append('|').Append(program.Rules.Count);
        foreach (var rule in program.Rules.OrderBy(r => r.Id))
            sb.Append('|').Append(rule.Id).Append(':').Append(rule.Name).Append(':').Append(rule.Priority);
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task ProcessCommand(RuntimeCommand cmd, CancellationToken ct)
    {
        var previousState = _state;
        switch (cmd)
        {
            case ActivateProgramCommand a:
                InstallConfiguration(a.Program, _definitions, _interlocks, _failsafeValues);
                _state = RuntimeState.Running;
                break;
            case PauseCommand:
                _state = RuntimeState.Stopped;
                ApplyFailsafeOutputs("pause");
                break;
            case ResumeCommand:
                if (_activeProgram is not null) _state = RuntimeState.Running;
                break;
            case StopCommand:
                _state = RuntimeState.Stopped;
                ClearForces();
                ApplyFailsafeOutputs("stop");
                break;
            case SetManualInputCommand m:
                SetInputs(new Dictionary<Guid, RuntimeValue>
                {
                    [m.VariableId] = new RuntimeValue
                    {
                        VariableId = m.VariableId,
                        Value = m.Value,
                        Quality = Quality.Simulated,
                        Source = ValueSource.Manual,
                        TimestampUtc = DateTime.UtcNow
                    }
                });
                // Notificar cambio de input
                _ = _notifier.NotifyInputChangedAsync(m.VariableId, m.Value);
                break;
            case ForceOutputCommand f:
                SetForce(f);
                break;
            case ClearForceCommand c:
                ClearForce(c.VariableId);
                break;
            case SetRuntimeModeCommand mode:
                _mode = mode.Mode;
                break;
        }

        // Notificar cambio de estado si cambió
        if (previousState != _state)
        {
            _ = _notifier.NotifyStateChangedAsync(_state);
        }
        await Task.CompletedTask;
    }

    private async Task ShutdownAsync()
    {
        _state = RuntimeState.Stopped;
        _watchdog.Armed = false;
        ClearForces();
        ApplyFailsafeOutputs("shutdown");
        _logger.LogInformation("PlcRuntimeService detenido");
        Audit(AuditEventType.Shutdown, "Runtime", null, null, null, "Success");
        await Task.CompletedTask;
    }

    /// <summary>Forzado de salida completo: valor + expiración + metadata (P0-3).</summary>
    private sealed record ForcedOutput(PlcValue Value, DateTime? ExpiresAt, string Reason);

    /// <summary>Registra un evento de auditoría de seguridad operacional si hay sumidero.</summary>
    private void Audit(AuditEventType action, string entityType, Guid? entityId,
        string? oldValue = null, string? newValue = null, string result = "Success")
    {
        if (_audit is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _audit.AppendAsync(new AuditEvent
                {
                    User = "runtime",
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Result = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo auditar {Action} para {EntityId}", action, entityId);
            }
        });
    }

    private void SetForce(ForceOutputCommand f)
    {
        lock (_coordinator)
        {
            // Validar que la variable exista y sea Output.
            if (!_definitions.TryGetValue(f.VariableId, out var def) || def.Direction != VariableDirection.Output)
            {
                _logger.LogWarning("Force ignorado: variable {Id} inexistente o no es Output", f.VariableId);
                return;
            }

            // Validar tipo.
            if (def.DataType != f.Value.DataType)
            {
                _logger.LogWarning("Force ignorado: tipo {Actual} no coincide con el esperado {Esperado} para {Id}",
                    f.Value.DataType, def.DataType, f.VariableId);
                return;
            }

            DateTime? expiresAt = f.ExpiresAfter is null ? null : DateTime.UtcNow + f.ExpiresAfter.Value;
            _forcedOutputs[f.VariableId] = new ForcedOutput(f.Value, expiresAt, "manual");
            _logger.LogInformation("Force activado: {Id} = {Value} (expira {ExpiresAt})",
                f.VariableId, f.Value, expiresAt?.ToString("O"));
            Audit(AuditEventType.Force, "Output", f.VariableId, null, f.Value.AsString());
        }
    }

    private void ClearForce(Guid variableId)
    {
        lock (_coordinator)
        {
            _forcedOutputs.Remove(variableId);
        }
        _logger.LogInformation("Force liberado: {Id}", variableId);
        Audit(AuditEventType.Force, "Output", variableId, null, null, "Cleared");
    }

    private void ClearForces()
    {
        lock (_coordinator)
        {
            _forcedOutputs.Clear();
        }
    }

    /// <summary>
    /// Devuelve las propuestas de fuerza activas (P0-3). Los forces vencidos se expiran
    /// automáticamente. Cada fuerza entra al arbitraje con prioridad
    /// <see cref="OutputPriority.ManualForcedSafeCommand"/>: por encima del control
    /// automático, por debajo de interlocks/failsafe de seguridad.
    /// </summary>
    private IReadOnlyCollection<OutputProposal> GetActiveForceProposals()
    {
        var proposals = new List<OutputProposal>();
        lock (_coordinator)
        {
            var now = DateTime.UtcNow;
            var expired = _forcedOutputs.Where(kv => kv.Value.ExpiresAt is not null && kv.Value.ExpiresAt <= now)
                .Select(kv => kv.Key).ToList();
            foreach (var id in expired)
            {
                _forcedOutputs.Remove(id);
                _logger.LogInformation("Force expirado automáticamente: {Id}", id);
                Audit(AuditEventType.Force, "Output", id, null, null, "Expired");
            }

            foreach (var (id, forced) in _forcedOutputs)
            {
                proposals.Add(new OutputProposal
                {
                    VariableId = id,
                    Value = forced.Value.Raw ?? false,
                    Priority = OutputPriority.ManualForcedSafeCommand,
                    Reason = "manual force"
                });
            }
        }
        return proposals;
    }

    /// <summary>
    /// Lleva TODAS las salidas a sus valores failsafe de forma explícita y atómica
    /// (P0-1). Se usa en Stop, Pause, Fault y shutdown. No depende de que exista una
    /// propuesta de lógica en ese scan. Corre en el propio hilo del loop, por lo que
    /// puede adquirir <c>_coordinator</c>.
    /// </summary>
    private void ApplyFailsafeOutputs(string reason)
    {
        lock (_coordinator)
        {
            ApplyFailsafeOutputsCore(reason, _definitions, _failsafeValues);
        }
    }

    /// <summary>
    /// Variante lock-free para la transición watchdog ⟶ Faulted+failsafe. Un scan
    /// bloqueado puede retener <c>_coordinator</c> indefinidamente; esta ruta NO adquiere
    /// ese lock (ni ningún recurso retenible por el scan vigilado): lee la configuración
    /// publicada por referencias atómicas (<see cref="_definitions"/>, <see cref="_failsafeValues"/>)
    /// y aplica el failsafe a través del store, que tiene su propio lock independiente.
    /// </summary>
    private void ApplyFailsafeOutputsLockFree(string reason, Action? afterStoreUpdate = null)
    {
        var defs = _definitions;             // referencia publicada (swap atómico + volatile)
        var failsafe = _failsafeValues;      // idem
        ApplyFailsafeOutputsCore(reason, defs, failsafe, afterStoreUpdate);
    }

    /// <summary>Núcleo común: construye y publica los failsafe sin lock sobre el coordinator.</summary>
    private void ApplyFailsafeOutputsCore(
        string reason,
        IReadOnlyDictionary<Guid, VariableDefinition> defs,
        IReadOnlyDictionary<Guid, PlcValue> failsafe,
        Action? afterStoreUpdate = null)
    {
        var outputs = new Dictionary<Guid, RuntimeValue>();
        foreach (var (id, def) in defs)
        {
            if (def.Direction != VariableDirection.Output) continue;

            var value = GetFailsafeOrDefault(id, def.DataType, failsafe);
            outputs[id] = new RuntimeValue
            {
                VariableId = id,
                Value = value,
                Quality = Quality.Good,
                Source = ValueSource.Default,
                TimestampUtc = DateTime.UtcNow,
                SequenceNumber = _totalScans
            };
        }

        _lastOutputs = outputs;

        _store.Update(m =>
        {
            m.State = _state;
            m.Outputs = _lastOutputs;
        });

        afterStoreUpdate?.Invoke();

        foreach (var (id, rv) in outputs)
        {
            _ = _notifier.NotifyOutputChangedAsync(id, rv.Value);
        }

        _logger.LogInformation("Failsafe aplicado a todas las salidas (motivo: {Reason})", reason);
    }

    /// <summary>
    /// Failsafe configurado o, si falta, la política por defecto documentada en
    /// <see cref="FailsafePolicy"/>. Nunca un valor implícito (P0-1 + riesgo 4).
    /// </summary>
    private static PlcValue GetFailsafeOrDefault(Guid variableId, PlcDataType type, IReadOnlyDictionary<Guid, PlcValue> failsafe)
    {
        if (failsafe.TryGetValue(variableId, out var fs))
            return fs;

        return FailsafePolicy.Default(type);
    }
}