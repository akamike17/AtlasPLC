using System.Net;
using System.Net.Sockets;
using NModbus;

namespace AtlasSoftPlc.Protocols.Modbus;

/// <summary>
/// Implementación de <see cref="IPointSource{T}"/> respaldada por un array en memoria.
/// Se usa para construir el almacén del servidor de pruebas.
/// </summary>
public sealed class ArrayPointSource<T> : IPointSource<T> where T : struct
{
    private readonly T[] _values;
    private readonly object _gate = new();

    public ArrayPointSource(int count)
    {
        _values = new T[Math.Max(1, count)];
    }

    public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
    {
        lock (_gate)
        {
            var result = new T[numberOfPoints];
            for (var i = 0; i < numberOfPoints; i++)
            {
                var idx = startAddress + i;
                result[i] = idx < _values.Length ? _values[idx] : default;
            }
            return result;
        }
    }

    public void WritePoints(ushort startAddress, T[] points)
    {
        lock (_gate)
        {
            for (var i = 0; i < points.Length; i++)
            {
                var idx = startAddress + i;
                if (idx < _values.Length)
                    _values[idx] = points[i];
            }
        }
    }

    /// <summary>Acceso directo para que los tests puedan sembrar/inspeccionar valores.</summary>
    public T this[int index]
    {
        get { lock (_gate) return index < _values.Length ? _values[index] : default; }
        set { lock (_gate) { if (index < _values.Length) _values[index] = value; } }
    }
}

/// <summary>
/// Almacén de datos en memoria para el servidor Modbus de pruebas.
/// Mantiene coils, entradas discretas, registros de entrada y de retención.
/// </summary>
public sealed class InMemorySlaveDataStore : ISlaveDataStore
{
    public IPointSource<bool> CoilDiscretes { get; }
    public IPointSource<bool> CoilInputs { get; }
    public IPointSource<ushort> HoldingRegisters { get; }
    public IPointSource<ushort> InputRegisters { get; }

    public InMemorySlaveDataStore(
        int coils = 128,
        int discreteInputs = 128,
        int holdingRegisters = 256,
        int inputRegisters = 256)
    {
        CoilDiscretes = new ArrayPointSource<bool>(coils);
        CoilInputs = new ArrayPointSource<bool>(discreteInputs);
        HoldingRegisters = new ArrayPointSource<ushort>(holdingRegisters);
        InputRegisters = new ArrayPointSource<ushort>(inputRegisters);
    }
}

/// <summary>
/// Servidor Modbus TCP de pruebas (sección 34) que se ejecuta en un puerto dinámico y
/// mantiene los valores en memoria, sin necesidad de un PLC físico.
/// </summary>
public sealed class ModbusTcpServer : IDisposable
{
    private readonly IModbusSlaveNetwork _network;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }
    public byte UnitId { get; }
    public InMemorySlaveDataStore DataStore { get; }

    public ModbusTcpServer(
        int coils = 128,
        int discreteInputs = 128,
        int holdingRegisters = 256,
        int inputRegisters = 256,
        byte unitId = 1)
    {
        UnitId = unitId;
        DataStore = new InMemorySlaveDataStore(coils, discreteInputs, holdingRegisters, inputRegisters);

        // TcpListener en puerto 0 → el SO asigna un puerto libre.
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var factory = new ModbusFactory();
        var slave = factory.CreateSlave(unitId, DataStore);
        _network = factory.CreateSlaveNetwork(_listener);
        _network.AddSlave(slave);
    }

    /// <summary>Inicia el bucle de escucha en segundo plano.</summary>
    public void Start()
    {
        _ = _network.ListenAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _network.Dispose();
        }
        catch
        {
            // Supresión deliberada del error de cierre durante los tests.
        }
        _listener.Stop();
        _cts.Dispose();
    }
}