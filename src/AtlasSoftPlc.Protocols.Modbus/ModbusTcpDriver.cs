using System.Net.Sockets;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Values;
using NModbus;

namespace AtlasSoftPlc.Protocols.Modbus;

/// <summary>
/// Adaptador de dispositivo Modbus TCP basado en NModbus (sección 34).
/// Lee y escribe coils, entradas discretas y registros (input/holding) resolviendo
/// direcciones textuales por binding, con conversión de endianness y circuit-breaker.
/// </summary>
public sealed class ModbusTcpDriver : IDeviceDriver
{
    // Valores por defecto de timeout (ms).
    public const int DefaultConnectTimeoutMs = 3000;
    public const int DefaultReadTimeoutMs = 1000;
    public const int DefaultWriteTimeoutMs = 1000;
    public const int DefaultRetries = 2;
    public const int DefaultPort = 502;

    private readonly DeviceDefinition _definition;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, TagBinding> _bindings = new();
    private readonly CircuitBreaker _breaker;

    private IModbusMaster? _client;
    private DriverState _state = DriverState.Disconnected;
    private string? _lastError;
    private DateTime _lastSuccessfulIoUtc;
    private int _totalReads;
    private int _totalWrites;
    private int _totalFailures;

    public ModbusTcpDriver(DeviceDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _breaker = new CircuitBreaker();
    }

    public string DriverId => $"modbus-tcp:{_definition.Id}";

    public DriverCapabilities Capabilities =>
        DriverCapabilities.CanRead |
        DriverCapabilities.CanWrite |
        DriverCapabilities.SupportsBulkRead |
        DriverCapabilities.SupportsBulkWrite;

    // --- Configuración de bindings ---

    /// <summary>Registra el mapa VariableId → binding usado para resolver direcciones.</summary>
    public void Configure(IReadOnlyCollection<TagBinding> bindings)
    {
        lock (_gate)
        {
            _bindings.Clear();
            if (bindings is null)
                return;

            foreach (var b in bindings)
            {
                if (b is not null)
                    _bindings[b.VariableId] = b;
            }
        }
    }

    // --- Ciclo de vida ---

    public async Task<DeviceConnectionResult> ConnectAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _state = DriverState.Connecting;
        }

        try
        {
            var host = _definition.Host;
            if (string.IsNullOrWhiteSpace(host))
                return FailConnectionResult("No se especificó Host para el dispositivo Modbus TCP.");

            var port = _definition.Port ?? DefaultPort;
            var connectTimeout = _definition.TimeoutMs ?? DefaultConnectTimeoutMs;

            var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(connectTimeout);

            try
            {
                await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                client.Dispose();
                return FailConnectionResult($"Tiempo de conexión agotado ({connectTimeout} ms) hacia {host}:{port}.");
            }

            var factory = new ModbusFactory();
            var readTimeout = Math.Max(DefaultReadTimeoutMs, _definition.TimeoutMs ?? DefaultReadTimeoutMs);
            var writeTimeout = Math.Max(DefaultWriteTimeoutMs, _definition.TimeoutMs ?? DefaultWriteTimeoutMs);

            var master = factory.CreateMaster(client);
            master.Transport.ReadTimeout = readTimeout;
            master.Transport.WriteTimeout = writeTimeout;
            master.Transport.Retries = _definition.Retries ?? DefaultRetries;
            master.Transport.WaitToRetryMilliseconds = 200;

            lock (_gate)
            {
                _client = master;
                _state = DriverState.Connected;
                _breaker.OnSuccess();
                _lastSuccessfulIoUtc = DateTime.UtcNow;
                _lastError = null;
            }

            return new DeviceConnectionResult { Success = true, State = DriverState.Connected };
        }
        catch (Exception ex)
        {
            return FailConnectionResult($"Fallo al conectar con el dispositivo Modbus TCP: {ex.Message}");
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            try
            {
                _client?.Dispose();
            }
            finally
            {
                _client = null;
                _state = DriverState.Disconnected;
            }
        }
        return Task.CompletedTask;
    }

    // --- Lectura ---

    public async Task<IReadOnlyDictionary<Guid, PlcValue>> ReadInputsAsync(
        IReadOnlyCollection<Guid> variableIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, PlcValue>();
        if (variableIds is null || variableIds.Count == 0)
            return result;

        var bindings = ResolveBindings(variableIds);
        foreach (var (variableId, binding) in bindings)
        {
            ct.ThrowIfCancellationRequested();

            var read = await ReadBindingAsync(binding, ct).ConfigureAwait(false);
            if (read.Success)
                result[variableId] = read.Value;
        }

        return result;
    }

    private async Task<PlcReadResult> ReadBindingAsync(TagBinding binding, CancellationToken ct)
    {
        var address = ModbusAddress.TryParse(binding.Address);
        if (address is null)
            return FailRead($"Dirección Modbus inválida: '{binding.Address}'.");

        var value = address.Value;

        try
        {
            var master = EnsureConnectedOrThrow();
            var dataType = ParseDataType(binding.DataType);
            var endianness = ModbusEndiannessExtensions.Parse(binding.ByteOrder);
            var unitId = _definition.UnitId ?? 1;
            var perReadTimeout = ResolveTimeout(binding.TimeoutMs, DefaultReadTimeoutMs);

            PlcValue result;
            ushort[] rawWords;
            ushort start = value.Address;
            ushort count = (ushort)Math.Max(1, RegisterConverter.RegisterCount(dataType));

            switch (value.Area)
            {
                case ModbusArea.Coil:
                {
                    var bits = await RunWithTimeoutAsync(
                        () => master.ReadCoilsAsync(unitId, start, count),
                        perReadTimeout, ct).ConfigureAwait(false);
                    result = BindingBit(bits, binding, dataType, value.Address);
                    break;
                }
                case ModbusArea.DiscreteInput:
                {
                    var bits = await RunWithTimeoutAsync(
                        () => master.ReadInputsAsync(unitId, start, count),
                        perReadTimeout, ct).ConfigureAwait(false);
                    result = BindingBit(bits, binding, dataType, value.Address);
                    break;
                }
                case ModbusArea.InputRegister:
                {
                    rawWords = await RunWithTimeoutAsync(
                        () => master.ReadInputRegistersAsync(unitId, start, count),
                        perReadTimeout, ct).ConfigureAwait(false);
                    result = FromRegisters(rawWords, binding, dataType, endianness);
                    break;
                }
                case ModbusArea.HoldingRegister:
                {
                    rawWords = await RunWithTimeoutAsync(
                        () => master.ReadHoldingRegistersAsync(unitId, start, count),
                        perReadTimeout, ct).ConfigureAwait(false);
                    if (binding.BitIndex is int bitIndex)
                    {
                        result = PlcValue.Bool((rawWords[0] & (1 << bitIndex)) != 0);
                    }
                    else
                    {
                        result = FromRegisters(rawWords, binding, dataType, endianness);
                    }
                    break;
                }
                default:
                    return FailRead($"Área Modbus no soportada: '{value.Area}'.");
            }

            result = ApplyScale(result, binding);
            RecordSuccess();
            return PlcReadResult.Ok(result);
        }
        catch (TimeoutException)
        {
            return FailRead($"Tiempo de lectura agotado para '{binding.Address}'.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsClassifiedNetworkError(ex))
        {
            return FailRead($"Fallo de lectura en '{binding.Address}': {ex.Message}");
        }
    }

    // --- Escritura ---

    public async Task<WriteResult> WriteOutputsAsync(
        IReadOnlyDictionary<Guid, PlcValue> outputs, CancellationToken ct = default)
    {
        if (outputs is null || outputs.Count == 0)
            return WriteResult.Ok(0);

        int written = 0;
        foreach (var (variableId, value) in outputs)
        {
            ct.ThrowIfCancellationRequested();

            if (!_bindings.TryGetValue(variableId, out var binding))
                return WriteResult.Fail($"No existe binding para la variable '{variableId}'.");

            var write = await WriteBindingAsync(binding, value, ct).ConfigureAwait(false);
            if (!write.Success)
                return write;
            written++;
        }

        return WriteResult.Ok(written);
    }

    private async Task<WriteResult> WriteBindingAsync(TagBinding binding, PlcValue value, CancellationToken ct)
    {
        var address = ModbusAddress.TryParse(binding.Address);
        if (address is null)
            return WriteResult.Fail($"Dirección Modbus inválida: '{binding.Address}'.");

        var parsed = address.Value;
        if (!parsed.IsWritable)
            return WriteResult.Fail($"El área '{parsed.Area}' no admite escritura.");

        try
        {
            var master = EnsureConnectedOrThrow();
            var unitId = _definition.UnitId ?? 1;
            var perWriteTimeout = ResolveTimeout(binding.TimeoutMs, DefaultWriteTimeoutMs);

            var dataType = ParseDataType(binding.DataType);
            var endianness = ModbusEndiannessExtensions.Parse(binding.ByteOrder);

            switch (parsed.Area)
            {
                case ModbusArea.Coil:
                {
                    var bit = value.AsBool();
                    await RunWithTimeoutAsync(
                        () => master.WriteSingleCoilAsync(unitId, parsed.Address, bit),
                        perWriteTimeout, ct).ConfigureAwait(false);
                    break;
                }
                case ModbusArea.HoldingRegister:
                {
                    if (binding.BitIndex is int bitIndex)
                    {
                        var existing = await RunWithTimeoutAsync(
                            () => master.ReadHoldingRegistersAsync(unitId, parsed.Address, 1),
                            perWriteTimeout, ct).ConfigureAwait(false);
                        var word = existing[0];
                        if (value.AsBool())
                            word = (ushort)(word | (1 << bitIndex));
                        else
                            word = (ushort)(word & ~(1 << bitIndex));
                        await RunWithTimeoutAsync(
                            () => master.WriteSingleRegisterAsync(unitId, parsed.Address, word),
                            perWriteTimeout, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!RegisterConverter.TryToRegisters(value, endianness, out var words) || words is null)
                            return WriteResult.Fail($"Tipo de dato no soportado para escritura: '{binding.DataType}'.");

                        if (words.Length == 1)
                            await RunWithTimeoutAsync(
                                () => master.WriteSingleRegisterAsync(unitId, parsed.Address, words[0]),
                                perWriteTimeout, ct).ConfigureAwait(false);
                        else
                            await RunWithTimeoutAsync(
                                () => master.WriteMultipleRegistersAsync(unitId, parsed.Address, words),
                                perWriteTimeout, ct).ConfigureAwait(false);
                    }
                    break;
                }
                default:
                    return WriteResult.Fail($"Área no escribible: '{parsed.Area}'.");
            }

            RecordWrite();
            return WriteResult.Ok(1);
        }
        catch (TimeoutException)
        {
            return WriteResult.Fail($"Tiempo de escritura agotado para '{binding.Address}'.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsClassifiedNetworkError(ex))
        {
            return WriteResult.Fail($"Fallo de escritura en '{binding.Address}': {ex.Message}");
        }
    }

    // --- Salud ---

    public Task<DeviceHealth> GetHealthAsync(CancellationToken ct = default)
    {
        DeviceHealth health;
        lock (_gate)
        {
            health = new DeviceHealth
            {
                DriverId = DriverId,
                State = _state,
                LastSuccessfulIoUtc = _lastSuccessfulIoUtc,
                ConsecutiveFailures = _breaker.CurrentFailures,
                TotalReads = _totalReads,
                TotalWrites = _totalWrites,
                TotalFailures = _totalFailures,
                LastError = _lastError
            };
        }
        return Task.FromResult(health);
    }

    // --- Helpers internos ---

    private IModbusMaster EnsureConnectedOrThrow()
    {
        lock (_gate)
        {
            if (_state is DriverState.Connecting or DriverState.Connected && _client is not null)
                return _client;

            throw new InvalidOperationException("El driver Modbus no está conectado.");
        }
    }

    private List<(Guid VariableId, TagBinding Binding)> ResolveBindings(IReadOnlyCollection<Guid> variableIds)
    {
        var resolved = new List<(Guid, TagBinding)>();
        lock (_gate)
        {
            foreach (var id in variableIds)
            {
                if (_bindings.TryGetValue(id, out var binding))
                    resolved.Add((id, binding));
                else
                    RecordFailure($"No existe binding para la variable '{id}'.");
            }
        }
        return resolved;
    }

    private void RecordFailure(string error)
    {
        lock (_gate)
        {
            _totalFailures++;
            _lastError = error;
            _breaker.OnFailure();
            if (_breaker.IsOpen())
                _state = DriverState.RetryWaiting;
        }
    }

    private void RecordSuccess()
    {
        lock (_gate)
        {
            _totalReads++;
            _lastSuccessfulIoUtc = DateTime.UtcNow;
            _lastError = null;
            _breaker.OnSuccess();
            if (_state == DriverState.RetryWaiting)
                _state = DriverState.Connected;
        }
    }

    private void RecordWrite()
    {
        lock (_gate)
        {
            _totalWrites++;
            _lastSuccessfulIoUtc = DateTime.UtcNow;
            _lastError = null;
            _breaker.OnSuccess();
        }
    }

    private DeviceConnectionResult FailConnectionResult(string message)
    {
        lock (_gate)
        {
            _state = DriverState.Faulted;
            _lastError = message;
        }
        return new DeviceConnectionResult { Success = false, Error = message, State = DriverState.Faulted };
    }

    private PlcReadResult FailRead(string error)
    {
        lock (_gate)
        {
            _totalFailures++;
            _lastError = error;
            _breaker.OnFailure();
        }
        return PlcReadResult.Empty(error);
    }

    private static PlcDataType ParseDataType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
            return PlcDataType.UInt16;

        return dataType.Trim().ToLowerInvariant() switch
        {
            "bool" or "boolean" => PlcDataType.Bool,
            "int16" or "short" => PlcDataType.Int16,
            "uint16" or "ushort" or "word" => PlcDataType.UInt16,
            "int32" or "int" => PlcDataType.Int32,
            "uint32" or "uint" or "dword" => PlcDataType.UInt32,
            "int64" or "long" => PlcDataType.Int64,
            "uint64" or "ulong" => PlcDataType.UInt64,
            "float" or "single" => PlcDataType.Float,
            "double" => PlcDataType.Double,
            "decimal" => PlcDataType.Decimal,
            "string" => PlcDataType.String,
            _ => PlcDataType.UInt16
        };
    }

    private static PlcValue BindingBit(bool[] bits, TagBinding binding, PlcDataType dataType, ushort start)
    {
        // Para coils y entradas discretas cada elemento del array ya es un bit independiente.
        // Si además se especificó BitIndex, se interpreta sobre el primer bit leído.
        if (binding.BitIndex is int bitIndex)
            return PlcValue.Bool(bitIndex < bits.Length && bits[bitIndex]);
        return PlcValue.Bool(bits.Length > 0 && bits[0]);
    }

    private static PlcValue FromRegisters(ushort[] rawWords, TagBinding binding, PlcDataType dataType, ModbusEndianness endianness)
    {
        if (binding.BitIndex is int bit)
            return PlcValue.Bool(rawWords.Length > 0 && (rawWords[0] & (1 << bit)) != 0);

        if (RegisterConverter.TryFromRegisters(rawWords, dataType, endianness, out var converted))
            return converted;

        return PlcValue.Null(dataType);
    }

    private static PlcValue ApplyScale(PlcValue value, TagBinding binding)
    {
        if (binding.Scale == 1.0 && binding.Offset == 0.0)
            return value;
        if (!value.IsNumeric || !value.HasValue)
            return value;

        var scaled = value.AsDouble() * binding.Scale + binding.Offset;
        return value.DataType switch
        {
            PlcDataType.Float => PlcValue.Float((float)scaled),
            PlcDataType.Double => PlcValue.Double(scaled),
            PlcDataType.Int32 => PlcValue.Int32((int)scaled),
            PlcDataType.UInt32 => PlcValue.UInt32((uint)scaled),
            _ => value
        };
    }

    private static bool IsClassifiedNetworkError(Exception ex) =>
        ex is SocketException or IOException or TimeoutException or SlaveException
            or InvalidOperationException or ObjectDisposedException;

    private static int ResolveTimeout(int bindingTimeoutMs, int fallback) =>
        bindingTimeoutMs > 0 ? bindingTimeoutMs : fallback;

    /// <summary>
    /// Ejecuta una operación de red asíncrona con un timeout explícito. NModbus 3.x no
    /// admite <see cref="CancellationToken"/> en sus métodos async, así que se compite
    /// contra un retardo y se lanza <see cref="TimeoutException"/> si se supera el plazo.
    /// </summary>
    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<Task<T>> operation, int timeoutMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = operation();
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, timeoutCts.Token)).ConfigureAwait(false);

        if (completed != task)
            throw new TimeoutException($"La operación superó el timeout de {timeoutMs} ms.");

        return await task.ConfigureAwait(false);
    }

    /// <summary>Sobrecarga para operaciones asíncronas sin resultado (escrituras).</summary>
    private static async Task RunWithTimeoutAsync(
        Func<Task> operation, int timeoutMs, CancellationToken ct)
    {
        await RunWithTimeoutAsync(
            async () => { await operation().ConfigureAwait(false); return true; },
            timeoutMs, ct).ConfigureAwait(false);
    }

    private readonly struct PlcReadResult
    {
        public bool Success { get; }
        public PlcValue Value { get; }
        public string? Error { get; }

        private PlcReadResult(bool success, PlcValue value, string? error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static PlcReadResult Ok(PlcValue value) => new(true, value, null);
        public static PlcReadResult Empty(string error) => new(false, default, error);
    }
}