using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Domain.Devices;

/// <summary>Contrato base de abstracción de dispositivos (sección 8).</summary>
public interface IDeviceDriver
{
    string DriverId { get; }
    DriverCapabilities Capabilities { get; }
    Task<DeviceConnectionResult> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, PlcValue>> ReadInputsAsync(IReadOnlyCollection<Guid> variableIds, CancellationToken ct = default);
    Task<WriteResult> WriteOutputsAsync(IReadOnlyDictionary<Guid, PlcValue> outputs, CancellationToken ct = default);
    Task<DeviceHealth> GetHealthAsync(CancellationToken ct = default);
}