using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Protocols.Modbus;
using AtlasSoftPlc.Runtime.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Puente de I/O Modbus TCP (modalidad explícita). Conecta un <see cref="IDeviceDriver"/>
/// Modbus al runtime: lee entradas (coils) → <see cref="PlcRuntimeService.SetInputs"/>, y
/// escribe las salidas calculadas por el scan → coils del dispositivo.
///
/// Política de seguridad compatible con el runtime:
///  - timeout por operación y cycle; nunca bloquea indefinidamente;
///  - pérdida de conexión / timeout / excepción: no tumba el proceso, se registra salud,
///    los inputs afectados pasan a su valores failsafe (NO conservan el valor viejo);
///  - reconexión con intentos máximos + backoff;
///  - anti-stale: si el runtime está Faulted/Stopped o la generación de scan cambió, las
///    escrituras tardías se descartan (no publican datos de un estado obsoleto).
/// </summary>
public sealed class ModbusIoService : BackgroundService
{
    private readonly ILogger<ModbusIoService> _logger;
    private readonly IOptions<ModbusOptions> _options;
    private readonly PlcRuntimeService _runtime;
    private readonly SimulationService _simulation;

    private readonly object _gate = new();
    private IDeviceDriver? _driver;
    private DeviceHealth? _lastHealth;
    private string? _lastError;

    public ModbusIoService(
        ILogger<ModbusIoService> logger,
        IOptions<ModbusOptions> options,
        PlcRuntimeService runtime,
        SimulationService simulation)
    {
        _logger = logger;
        _options = options;
        _runtime = runtime;
        _simulation = simulation;
    }

    public DeviceHealth? LastHealth => _lastHealth;
    public string? LastError => _lastError;

    private IReadOnlyDictionary<string, VariableDefinition> ResolveVariablesByKey()
    {
        // Bootstrap del proyecto demo si aún no existe (idempotente a nivel de app:
        // el primer request o este puente lo crean). Garantiza variables disponibles.
        _simulation.EnsureLibrary();
        _simulation.BootstrapTankDemo();

        var byKey = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in _simulation.Variables.Values)
        {
            if (!string.IsNullOrWhiteSpace(def.Key))
                byKey[def.Key] = def;
        }
        return byKey;
    }

    /// <summary>Mapa Modbus del programa activo (VariableKey → dirección).</summary>
    private IReadOnlyDictionary<string, string> ResolveModbusMap() => _simulation.ActiveModbusMap;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("Modbus IO deshabilitado; el runtime opera en Simulation.");
            return;
        }

        EnsureDriver(opts);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(opts.CycleMs, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Ejecuta un ciclo completo de I/O (public para pruebas deterministas).</summary>
    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled) return;

        var variablesByKey = ResolveVariablesByKey();

        IDeviceDriver driver;
        lock (_gate)
        {
            driver = _driver ??= BuildDriver(opts);
            ReconfigureDriver(driver, variablesByKey);
        }

        // 1. Asegurar conexión (con intentos máximos; no bloquea indefinidamente).
        if (!await EnsureConnectedAsync(driver, opts, ct).ConfigureAwait(false))
        {
            // Sin conexión → aplicar failsafe a todas las entradas mapeadas.
            ApplyInputFailsafe(variablesByKey);
            return;
        }

        // 2. Leer entradas.
        var inputs = await ReadInputsAsync(driver, opts, variablesByKey, ct).ConfigureAwait(false);
        if (inputs.Count > 0)
        {
            _runtime.SetInputs(inputs);
        }

        // 3. Escribir salidas SOLO si el runtime está operativo (Running) y la
        // generación/programa no cambiaron desde la captura (anti-stale, P0-1).
        var ioSnapshot = _runtime.GetIoSnapshot();
        if (PlcRuntimeService.IsOperationalState(ioSnapshot.State) &&
            ioSnapshot.Outputs is { Count: > 0 } outputs)
        {
            // Re-verificar justo antes de publicar: si el estado/generación/programa
            // cambió mientras leíamos, descartamos esta escritura (jamás un snapshot viejo).
            var verify = _runtime.GetIoSnapshot();
            if (PlcRuntimeService.IsOperationalState(verify.State) &&
                verify.Generation == ioSnapshot.Generation &&
                verify.ActiveProgramHash == ioSnapshot.ActiveProgramHash)
            {
                await WriteOutputsAsync(driver, outputs, variablesByKey, ct).ConfigureAwait(false);
            }
            else
            {
                _lastError = "Escritura descartada: el runtime cambió de estado/generación/programa durante el ciclo.";
            }
        }

        var health = await driver.GetHealthAsync(ct).ConfigureAwait(false);
        RecordHealth(health);
    }

    // ── Construcción del driver y bindings ────────────────────────────────

    private void EnsureDriver(ModbusOptions opts)
    {
        lock (_gate)
        {
            _driver ??= BuildDriver(opts);
        }
    }

    /// <summary>Reconfigura los bindings del driver con el mapa/variables del programa activo.</summary>
    private void ReconfigureDriver(IDeviceDriver driver, IReadOnlyDictionary<string, VariableDefinition> variablesByKey)
    {
        if (driver is ModbusTcpDriver modbus)
        {
            var map = ResolveModbusMap();
            var bindings = new List<TagBinding>();
            foreach (var (key, address) in map)
            {
                if (!variablesByKey.TryGetValue(key, out var def))
                    continue;

                bindings.Add(new TagBinding
                {
                    VariableId = def.Id,
                    DeviceId = Guid.Empty,
                    Protocol = DeviceProtocol.ModbusTcp,
                    Address = address,
                    DataType = def.DataType switch
                    {
                        PlcDataType.Bool => "bool",
                        PlcDataType.Int16 => "int16",
                        PlcDataType.UInt16 => "uint16",
                        PlcDataType.Int32 => "int32",
                        PlcDataType.UInt32 => "uint32",
                        PlcDataType.Int64 => "int64",
                        PlcDataType.UInt64 => "uint64",
                        PlcDataType.Float => "float",
                        PlcDataType.Double => "double",
                        _ => "uint16"
                    },
                    ReadWriteMode = def.Direction == VariableDirection.Output ? "ReadWrite" : "Read"
                });
            }
            modbus.Configure(bindings);
        }
    }

    private static IDeviceDriver BuildDriver(ModbusOptions opts)
    {
        var driver = new ModbusTcpDriver(new DeviceDefinition
        {
            Name = "modbus-tcp-io",
            Protocol = DeviceProtocol.ModbusTcp,
            Host = opts.Host,
            Port = opts.Port,
            UnitId = opts.UnitId,
            TimeoutMs = opts.TimeoutMs,
            Retries = opts.Retries,
            Enabled = true
        });

        return driver;
    }

    // ── Conexión con backoff ──────────────────────────────────────────────

    private async Task<bool> EnsureConnectedAsync(IDeviceDriver driver, ModbusOptions opts, CancellationToken ct)
    {
        var health = await driver.GetHealthAsync(ct).ConfigureAwait(false);
        if (health.State == DriverState.Connected)
            return true;

        for (var attempt = 1; attempt <= opts.MaxConnectAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await driver.ConnectAsync(ct).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.LogInformation("Modbus conectado a {Host}:{Port}", opts.Host, opts.Port);
                return true;
            }

            _lastError = result.Error;
            if (attempt < opts.MaxConnectAttempts)
            {
                // Backoff breve: evita saturar la red en un dispositivo caído.
                await Task.Delay(opts.TimeoutMs, ct).ConfigureAwait(false);
            }
        }

        _logger.LogWarning("No se pudo conectar a Modbus ({Host}:{Port}) tras {Attempts} intentos.",
            opts.Host, opts.Port, opts.MaxConnectAttempts);
        return false;
    }

    // ── Lectura segura ────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, RuntimeValue>> ReadInputsAsync(
        IDeviceDriver driver, ModbusOptions opts, IReadOnlyDictionary<string, VariableDefinition> variablesByKey, CancellationToken ct)
    {
        var inputIds = variablesByKey.Values
            .Where(v => v.Direction == VariableDirection.Input)
            .Select(v => v.Id)
            .ToList();

        if (inputIds.Count == 0)
            return new Dictionary<Guid, RuntimeValue>();

        var result = new Dictionary<Guid, RuntimeValue>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(opts.TimeoutMs);

            var read = await driver.ReadInputsAsync(inputIds, cts.Token).ConfigureAwait(false);

            foreach (var id in inputIds)
            {
                if (read.TryGetValue(id, out var value))
                {
                    result[id] = new RuntimeValue
                    {
                        VariableId = id,
                        Value = value,
                        Quality = Quality.Good,
                        Source = ValueSource.Device,
                        TimestampUtc = DateTime.UtcNow
                    };
                }
                else
                {
                    // No se leyó esta entrada: aplicar failsafe (NO conservar valor viejo).
                    result[id] = FailsafeInput(id, variablesByKey);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout del ciclo: todas las entradas a failsafe.
            _lastError = $"Timeout de lectura Modbus ({opts.TimeoutMs} ms).";
            foreach (var id in inputIds)
                result[id] = FailsafeInput(id, variablesByKey);
        }
        catch (Exception ex)
        {
            _lastError = $"Error de lectura Modbus: {ex.Message}";
            foreach (var id in inputIds)
                result[id] = FailsafeInput(id, variablesByKey);
        }

        return result;
    }

    private static RuntimeValue FailsafeInput(Guid variableId, IReadOnlyDictionary<string, VariableDefinition> variablesByKey)
    {
        // Política segura: entradas afectadas caen al valor más seguro (false para bool,
        // 0 para numéricos), marcadas Bad, nunca un valor viejo que finge validez.
        PlcValue value = PlcValue.Bool(false);
        if (variablesByKey.Values.FirstOrDefault(v => v.Id == variableId) is { } def)
            value = FailsafePolicy.Default(def.DataType);

        return new RuntimeValue
        {
            VariableId = variableId,
            Value = value,
            Quality = Quality.Bad,
            Source = ValueSource.Device,
            TimestampUtc = DateTime.UtcNow
        };
    }

    private void ApplyInputFailsafe(IReadOnlyDictionary<string, VariableDefinition> variablesByKey)
    {
        var inputIds = variablesByKey.Values
            .Where(v => v.Direction == VariableDirection.Input)
            .Select(v => v.Id)
            .ToList();
        if (inputIds.Count == 0) return;

        var values = inputIds.ToDictionary(id => id, id => FailsafeInput(id, variablesByKey));
        _runtime.SetInputs(values);
    }

    // ── Escritura segura ──────────────────────────────────────────────────

    private async Task WriteOutputsAsync(
        IDeviceDriver driver, IReadOnlyDictionary<Guid, RuntimeValue> outputs, IReadOnlyDictionary<string, VariableDefinition> variablesByKey, CancellationToken ct)
    {
        var outputIds = variablesByKey.Values
            .Where(v => v.Direction == VariableDirection.Output)
            .Select(v => v.Id)
            .ToList();

        var toWrite = new Dictionary<Guid, PlcValue>();
        foreach (var id in outputIds)
        {
            if (outputs.TryGetValue(id, out var rv))
                toWrite[id] = rv.Value;
        }

        if (toWrite.Count == 0)
            return;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.Value.TimeoutMs);

            var result = await driver.WriteOutputsAsync(toWrite, cts.Token).ConfigureAwait(false);
            if (!result.Success)
                _lastError = result.Error;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _lastError = "Timeout de escritura Modbus.";
        }
        catch (Exception ex)
        {
            _lastError = $"Error de escritura Modbus: {ex.Message}";
        }
    }

    private void RecordHealth(DeviceHealth health)
    {
        lock (_gate)
        {
            _lastHealth = health;
        }
    }
}