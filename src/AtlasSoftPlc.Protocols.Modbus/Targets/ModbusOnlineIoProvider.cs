using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Protocols.Modbus.Targets;

/// <summary>
/// Implementación online real para Modbus TCP. Sólo conecta, lee, escribe y
/// diagnostica registros; nunca genera ni despliega programas PLC.
/// </summary>
public sealed class ModbusOnlineIoProvider : IOnlineIoProvider
{
    private readonly ConcurrentDictionary<string, DriverHolder> _drivers = new(StringComparer.OrdinalIgnoreCase);

    public async Task<OnlineIoResult> ConnectAsync(TargetInstance instance, CancellationToken ct = default)
    {
        var driver = GetDriver(instance);
        var result = await driver.ConnectAsync(ct).ConfigureAwait(false);
        return result.Success
            ? new(true, "Connected", $"Modbus TCP conectado a {Host(instance)}:{Port(instance)}.")
            : new(false, "Faulted", result.Error ?? "No se pudo conectar al dispositivo Modbus TCP.");
    }

    public async Task<OnlineIoResult> ReadAsync(TargetInstance instance, IReadOnlyCollection<string> addresses, CancellationToken ct = default)
    {
        if (addresses is null || addresses.Count == 0)
            return new(false, "InvalidRequest", "Indica al menos una dirección Modbus para leer.");
        var invalid = addresses.Where(address => ModbusAddress.TryParse(address) is null).ToArray();
        if (invalid.Length > 0)
            return new(false, "InvalidAddress", $"Dirección(es) Modbus inválida(s): {string.Join(", ", invalid)}.");

        var driver = GetDriver(instance);
        var bindings = ConfigureBindings(driver, addresses, readWrite: "Read");
        var connected = await EnsureConnectedAsync(driver, ct).ConfigureAwait(false);
        if (!connected.Succeeded) return connected;

        var ids = bindings.Keys.ToArray();
        var values = await driver.ReadInputsAsync(ids, ct).ConfigureAwait(false);
        if (values.Count != bindings.Count)
            return new(false, "Faulted", $"El dispositivo no respondió todas las direcciones Modbus ({values.Count}/{bindings.Count}).");
        var output = bindings.ToDictionary(pair => pair.Value.Address,
            pair => values[pair.Key].AsString(),
            StringComparer.OrdinalIgnoreCase);
        return new(true, "Read", $"Lectura Modbus completada: {output.Count} dirección(es).", output);
    }

    public async Task<OnlineIoResult> WriteAsync(TargetInstance instance, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        if (values is null || values.Count == 0)
            return new(false, "InvalidRequest", "Indica al menos una dirección Modbus y su valor.");
        var invalid = values.Keys.Where(address => ModbusAddress.TryParse(address) is null).ToArray();
        if (invalid.Length > 0)
            return new(false, "InvalidAddress", $"Dirección(es) Modbus inválida(s): {string.Join(", ", invalid)}.");

        var driver = GetDriver(instance);
        var bindings = ConfigureBindings(driver, values.Keys, readWrite: "ReadWrite");
        var connected = await EnsureConnectedAsync(driver, ct).ConfigureAwait(false);
        if (!connected.Succeeded) return connected;

        var parsed = new Dictionary<Guid, PlcValue>();
        foreach (var (id, binding) in bindings)
        {
            if (!values.TryGetValue(binding.Address, out var raw)) continue;
            parsed[id] = ParseValue(raw, binding.DataType);
        }
        var result = await driver.WriteOutputsAsync(parsed, ct).ConfigureAwait(false);
        return result.Success
            ? new(true, "Written", $"Escritura Modbus completada: {result.WrittenCount} dirección(es).", values)
            : new(false, "Faulted", result.Error ?? "La escritura Modbus fue rechazada.");
    }

    public async Task<OnlineIoResult> DiagnosticsAsync(TargetInstance instance, CancellationToken ct = default)
    {
        var health = await GetDriver(instance).GetHealthAsync(ct).ConfigureAwait(false);
        var data = new Dictionary<string, string>
        {
            ["driverId"] = health.DriverId,
            ["state"] = health.State.ToString(),
            ["totalReads"] = health.TotalReads.ToString(CultureInfo.InvariantCulture),
            ["totalWrites"] = health.TotalWrites.ToString(CultureInfo.InvariantCulture),
            ["totalFailures"] = health.TotalFailures.ToString(CultureInfo.InvariantCulture)
        };
        return new(true, health.State.ToString(), health.LastError ?? "Diagnóstico Modbus disponible.", data);
    }

    private async Task<OnlineIoResult> EnsureConnectedAsync(ModbusTcpDriver driver, CancellationToken ct)
    {
        var health = await driver.GetHealthAsync(ct).ConfigureAwait(false);
        if (health.State == DriverState.Connected) return new(true, "Connected", "Modbus TCP ya estaba conectado.");
        return await ConnectDriverAsync(driver, ct).ConfigureAwait(false);
    }

    private static async Task<OnlineIoResult> ConnectDriverAsync(ModbusTcpDriver driver, CancellationToken ct)
    {
        var result = await driver.ConnectAsync(ct).ConfigureAwait(false);
        return result.Success
            ? new(true, "Connected", "Modbus TCP conectado.")
            : new(false, "Faulted", result.Error ?? "No se pudo conectar al dispositivo Modbus TCP.");
    }

    private static Dictionary<Guid, TagBinding> ConfigureBindings(ModbusTcpDriver driver, IEnumerable<string> addresses, string readWrite)
    {
        var bindings = addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(AddressId, a => new TagBinding
            {
                VariableId = AddressId(a),
                DeviceId = Guid.Empty,
                Protocol = DeviceProtocol.ModbusTcp,
                Address = a,
                DataType = ModbusAddress.TryParse(a)?.Area is ModbusArea.Coil or ModbusArea.DiscreteInput ? "bool" : "uint16",
                ReadWriteMode = readWrite
            });
        driver.Configure(bindings.Values.ToArray());
        return bindings;
    }

    private static PlcValue ParseValue(string raw, string dataType)
    {
        if (dataType.Equals("bool", StringComparison.OrdinalIgnoreCase))
            return PlcValue.Bool(bool.TryParse(raw, out var bit) && bit);
        return PlcValue.UInt16(ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var word) ? word : (ushort)0);
    }

    private ModbusTcpDriver GetDriver(TargetInstance instance)
    {
        var key = string.Join("|", Host(instance), Port(instance), Byte(instance, "unitId", 1), Int(instance, "timeoutMs", ModbusTcpDriver.DefaultConnectTimeoutMs), Int(instance, "retries", ModbusTcpDriver.DefaultRetries));
        var holder = _drivers.AddOrUpdate(instance.Id,
            _ => new DriverHolder(key, BuildDriver(instance)),
            (_, existing) => existing.ConfigurationKey.Equals(key, StringComparison.Ordinal) ? existing : new DriverHolder(key, BuildDriver(instance)));
        return holder.Driver;
    }

    private static ModbusTcpDriver BuildDriver(TargetInstance instance) => new(new DeviceDefinition
    {
        Id = AddressId(instance.Id),
        Name = instance.DisplayName,
        Protocol = DeviceProtocol.ModbusTcp,
        Host = Host(instance),
        Port = Port(instance),
        UnitId = Byte(instance, "unitId", 1),
        TimeoutMs = Int(instance, "timeoutMs", ModbusTcpDriver.DefaultConnectTimeoutMs),
        Retries = Int(instance, "retries", ModbusTcpDriver.DefaultRetries),
        Enabled = true
    });

    private sealed record DriverHolder(string ConfigurationKey, ModbusTcpDriver Driver);

    private static string Host(TargetInstance instance) => instance.Configuration.TryGetValue("endpoint", out var value) && !string.IsNullOrWhiteSpace(value) ? value : "127.0.0.1";
    private static int Port(TargetInstance instance) => Int(instance, "port", ModbusTcpDriver.DefaultPort);
    private static int Int(TargetInstance instance, string key, int fallback) => instance.Configuration.TryGetValue(key, out var value) && int.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 65535 ? parsed : fallback;
    private static byte Byte(TargetInstance instance, string key, byte fallback) => instance.Configuration.TryGetValue(key, out var value) && byte.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;
    private static Guid AddressId(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)).AsSpan());
}
