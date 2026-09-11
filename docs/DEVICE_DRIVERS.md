# Drivers de dispositivo

## Contrato

```csharp
public interface IDeviceDriver
{
    string DriverId { get; }
    DriverCapabilities Capabilities { get; }
    Task<DeviceConnectionResult> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, PlcValue>> ReadInputsAsync(...);
    Task<WriteResult> WriteOutputsAsync(...);
    Task<DeviceHealth> GetHealthAsync(...);
}
```

Cada driver convierte `protocolo → VariableId Atlas` mediante `TagBinding`.
El motor lógico no conoce detalles Modbus/S7/OPC UA.

## Capacidades

`CanRead`, `CanWrite`, `CanBrowse`, `CanDiscover`, `CanSubscribe`,
`SupportsBulkRead`, `SupportsBulkWrite`, `SupportsQuality`. El front adapta
opciones según capacidades declaradas.

## Implementados

- **Virtual** (`VirtualDeviceDriver`) — I/O en memoria para simulación.
- **Modbus** (`ModbusTcpDriver`) — NModbus TCP client, coils/discrete/registers,
  endianness configurable.

## Roadmap

- Fase 1: Virtual, Modbus TCP, Modbus RTU, OPC UA Server.
- Fase 2: OPC UA Client, MQTT, ESP32 Remote I/O.
- Fase 3: Siemens (S7.Net+), Rockwell (libplctag.NET).

## Discovery

Read-only. Nunca se escribe al descubrir. `FoundDevice` con IP, candidatos de
protocolo, vendor (si se conoce; no inventar fabricante), confidence.