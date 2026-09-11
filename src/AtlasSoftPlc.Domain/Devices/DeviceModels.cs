namespace AtlasSoftPlc.Domain.Devices;

/// <summary>Protocolo de un dispositivo.</summary>
public enum DeviceProtocol
{
    Virtual,
    ModbusTcp,
    ModbusRtu,
    OpcUa,
    Mqtt,
    SiemensS7,
    RockwellCip
}

/// <summary>Estado de conexión de un driver (sección 52).</summary>
public enum DriverState
{
    Disconnected,
    Connecting,
    Connected,
    Degraded,
    RetryWaiting,
    Faulted
}

/// <summary>Clase de polling (sección 54).</summary>
public enum PollClass
{
    CriticalFast,
    Fast,
    Normal,
    Slow,
    OnDemand
}

/// <summary>Capacidades declaradas por un driver (sección 37).</summary>
[Flags]
public enum DriverCapabilities
{
    None = 0,
    CanRead = 1,
    CanWrite = 2,
    CanBrowse = 4,
    CanDiscover = 8,
    CanSubscribe = 16,
    SupportsBulkRead = 32,
    SupportsBulkWrite = 64,
    SupportsQuality = 128
}

public sealed class DeviceDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DeviceProtocol Protocol { get; set; } = DeviceProtocol.Virtual;
    public DriverCapabilities Capabilities { get; set; }

    // Modbus TCP
    public string? Host { get; set; }
    public int? Port { get; set; }
    public byte? UnitId { get; set; }
    public int? TimeoutMs { get; set; }
    public int? Retries { get; set; }

    // Modbus RTU
    public string? SerialPort { get; set; }
    public int? BaudRate { get; set; }
    public string? Parity { get; set; }
    public int? DataBits { get; set; }
    public string? StopBits { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class TagBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VariableId { get; set; }
    public Guid DeviceId { get; set; }
    public DeviceProtocol Protocol { get; set; }
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ByteOrder { get; set; } = "BigEndian";
    public int? BitIndex { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; } = 0.0;
    public string ReadWriteMode { get; set; } = "Read";
    public int PollIntervalMs { get; set; } = 100;
    public int TimeoutMs { get; set; } = 1000;
    public int Retries { get; set; } = 2;
    public PollClass PollClass { get; set; } = PollClass.Normal;
}