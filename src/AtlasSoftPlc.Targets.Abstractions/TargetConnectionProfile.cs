namespace AtlasSoftPlc.Targets;

public enum TargetConnectionType { Simulator, Physical }
public enum TargetTransport { Ethernet, Serial, VendorUsb }

public sealed class TargetConnectionProfile
{
    public TargetConnectionType ConnectionType { get; init; } = TargetConnectionType.Physical;
    public TargetTransport Transport { get; init; } = TargetTransport.Ethernet;
    public string? NetworkAdapterId { get; init; }
    public bool AutoSelectNetworkAdapter { get; init; } = true;
    public string? Address { get; init; }
    public int? Port { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public bool SupportsDiscovery { get; init; }
    public string? SerialPort { get; init; }
    public int? BaudRate { get; init; }
    public int? DataBits { get; init; }
    public string? Parity { get; init; }
    public int? StopBits { get; init; }
    public string? VendorUsbDriver { get; init; }
}
