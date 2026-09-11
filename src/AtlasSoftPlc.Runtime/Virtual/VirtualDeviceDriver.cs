using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Devices;

namespace AtlasSoftPlc.Runtime.Virtual;

/// <summary>
/// Driver de dispositivo virtual (sección 9). Mantiene un mapa de valores
/// en memoria que el operador puede manipular desde la UI de simulación.
/// </summary>
public sealed class VirtualDeviceDriver
{
    private readonly Dictionary<Guid, RuntimeValue> _values = new();
    private readonly object _lock = new();

    public Guid DeviceId { get; }
    public string DriverId => $"virtual-{DeviceId:N}";

    public VirtualDeviceDriver(Guid deviceId)
    {
        DeviceId = deviceId;
    }

    public string Name { get; set; } = "Dispositivo virtual";

    /// <summary>Establece el valor de una entrada virtual (interruptor, sensor, etc.).</summary>
    public void SetInput(Guid variableId, PlcValue value, Quality quality = Quality.Simulated)
    {
        lock (_lock)
        {
            _values[variableId] = new RuntimeValue
            {
                VariableId = variableId,
                Value = value,
                Quality = quality,
                Source = ValueSource.Simulation,
                TimestampUtc = DateTime.UtcNow
            };
        }
    }

    public void SetInput(Guid variableId, bool value)
        => SetInput(variableId, PlcValue.Bool(value));

    public IReadOnlyDictionary<Guid, RuntimeValue> ReadInputs(IReadOnlyCollection<Guid> variableIds)
    {
        lock (_lock)
        {
            var result = new Dictionary<Guid, RuntimeValue>();
            foreach (var id in variableIds)
            {
                if (_values.TryGetValue(id, out var v))
                    result[id] = v;
            }
            return result;
        }
    }

    public Dictionary<Guid, RuntimeValue> SnapshotInputs()
    {
        lock (_lock) return new Dictionary<Guid, RuntimeValue>(_values);
    }
}

/// <summary>Tipos de I/O virtual disponibles (sección 9).</summary>
public enum VirtualIoKind
{
    Switch,          // interruptor
    PushButton,      // pulsador
    DigitalSensor,   // sensor digital
    AnalogSensor,    // sensor analógico
    Potentiometer,   // potenciómetro
    Temperature,
    Pressure,
    Level,
    Motor,
    Valve,
    Lamp,
    Relay,
    Counter,
    Encoder
}