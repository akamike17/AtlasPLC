using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Protocols.Modbus.Targets;

public static class ModbusConnectionProfiles
{
    public static TargetConnectionProfile Physical() => new()
    {
        ConnectionType = TargetConnectionType.Physical,
        Transport = TargetTransport.Ethernet,
        Protocol = "ModbusTcp",
        Port = 502,
        Address = null,
        AutoSelectNetworkAdapter = true
    };

    public static TargetConnectionProfile Simulator() => new()
    {
        ConnectionType = TargetConnectionType.Simulator,
        Transport = TargetTransport.Ethernet,
        Protocol = "ModbusTcp",
        Address = "127.0.0.1",
        Port = 502,
        AutoSelectNetworkAdapter = false
    };
}
