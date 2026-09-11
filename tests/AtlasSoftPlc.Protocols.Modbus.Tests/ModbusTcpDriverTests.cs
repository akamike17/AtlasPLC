using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Protocols.Modbus.Tests;

/// <summary>
/// Pruebas de integración contra <see cref="ModbusTcpServer"/> (servidor local en memoria),
/// sin PLC físico (sección 34).
/// </summary>
public class ModbusTcpDriverTests : IDisposable
{
    private readonly ModbusTcpServer _server;
    private readonly ModbusTcpDriver _driver;

    public ModbusTcpDriverTests()
    {
        _server = new ModbusTcpServer();
        _server.Start();

        var definition = new DeviceDefinition
        {
            Name = "test-plc",
            Protocol = DeviceProtocol.ModbusTcp,
            Host = "127.0.0.1",
            Port = _server.Port,
            UnitId = 1,
            TimeoutMs = 1000
        };

        _driver = new ModbusTcpDriver(definition);
    }

    public void Dispose()
    {
        _driver.DisconnectAsync().GetAwaiter().GetResult();
        _server.Dispose();
    }

    // --- Helpers ---

    private static TagBinding CoilBinding(Guid variableId, ushort address)
        => new() { VariableId = variableId, Address = $"coil:{address}", DataType = "bool", ReadWriteMode = "ReadWrite" };

    private static TagBinding HoldingBinding(Guid variableId, ushort address, string dataType = "uint16", string byteOrder = "BigEndian")
        => new() { VariableId = variableId, Address = $"holdingregister:{address}", DataType = dataType, ByteOrder = byteOrder, ReadWriteMode = "ReadWrite" };

    private static TagBinding InputRegisterBinding(Guid variableId, ushort address, string dataType = "uint16")
        => new() { VariableId = variableId, Address = $"inputregister:{address}", DataType = dataType, ReadWriteMode = "Read" };

    private async Task ConnectAsync()
    {
        var result = await _driver.ConnectAsync();
        Assert.True(result.Success, $"Conexión fallida: {result.Error}");
    }

    // --- Coil read/write ---

    [Fact]
    public async Task Coil_WriteThenRead_RoundTrips()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { CoilBinding(varId, 5) });
        await ConnectAsync();

        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.Bool(true) });
        Assert.True(write.Success, write.Error);

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.True(read.TryGetValue(varId, out var value));
        Assert.True(value.AsBool());
    }

    [Fact]
    public async Task Coil_WriteFalse_ReadsFalse()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { CoilBinding(varId, 7) });
        await ConnectAsync();

        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.Bool(false) });
        Assert.True(write.Success, write.Error);

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.False(read[varId].AsBool());
    }

    // --- Holding register read/write ---

    [Fact]
    public async Task HoldingRegister_WriteThenRead_RoundTrips()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { HoldingBinding(varId, 10) });
        await ConnectAsync();

        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.UInt16(4321) });
        Assert.True(write.Success, write.Error);

        var read = await _driver.ReadInputsAsync(new[] { varId });
        var value = read[varId];
        Assert.Equal(PlcDataType.UInt16, value.DataType);
        Assert.Equal((ushort)4321, value.As<ushort>());
    }

    // --- Input register read ---

    [Fact]
    public async Task InputRegister_Read_ReturnsSeededValue()
    {
        _server.DataStore.InputRegisters.WritePoints(20, new ushort[] { 0x1234 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { InputRegisterBinding(varId, 20) });
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Equal((ushort)0x1234, read[varId].As<ushort>());
    }

    // --- Endianness ---

    [Fact]
    public async Task Int32_BigEndian_ReadsCorrectValue()
    {
        // 0x0000_0064 = 100 en big-endian (word alta primero).
        _server.DataStore.HoldingRegisters.WritePoints(30, new ushort[] { 0x0000, 0x0064 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { HoldingBinding(varId, 30, "int32", "BigEndian") });
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Equal(100, read[varId].As<int>());
    }

    [Fact]
    public async Task Int32_LittleEndian_ReadsCorrectValue()
    {
        // 100 = 0x0000_0064; little-endian → word baja primero: [0x0064, 0x0000].
        _server.DataStore.HoldingRegisters.WritePoints(40, new ushort[] { 0x0064, 0x0000 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { HoldingBinding(varId, 40, "int32", "LittleEndian") });
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Equal(100, read[varId].As<int>());
    }

    // --- Float mapping ---

    [Fact]
    public async Task Float_Read_MapsIeee754()
    {
        // 3.5f = 0x40600000 (IEEE-754). Big-endian words: [0x4060, 0x0000].
        _server.DataStore.HoldingRegisters.WritePoints(50, new ushort[] { 0x4060, 0x0000 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { HoldingBinding(varId, 50, "float", "BigEndian") });
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Equal(3.5f, (float)read[varId].AsDouble());
    }

    // --- Timeout (dirección inválida que no acepta conexión) ---

    [Fact]
    public async Task Timeout_ConnectToClosedPort_ReturnsFailure()
    {
        var definition = new DeviceDefinition
        {
            Name = "unreachable",
            Protocol = DeviceProtocol.ModbusTcp,
            Host = "127.0.0.1",
            Port = 1, // puerto cerrado
            UnitId = 1,
            TimeoutMs = 300
        };
        var driver = new ModbusTcpDriver(definition);

        var result = await driver.ConnectAsync();
        Assert.False(result.Success);
    }

    // --- Unit id incorrecto ---

    [Fact]
    public async Task WrongUnitId_ReturnsError()
    {
        var definition = new DeviceDefinition
        {
            Name = "wrong-unit",
            Protocol = DeviceProtocol.ModbusTcp,
            Host = "127.0.0.1",
            Port = _server.Port,
            UnitId = 99, // servidor registra unit 1
            TimeoutMs = 1000
        };
        var driver = new ModbusTcpDriver(definition);

        var varId = Guid.NewGuid();
        driver.Configure(new[] { HoldingBinding(varId, 10) });

        var connect = await driver.ConnectAsync();
        Assert.True(connect.Success);

        var read = await driver.ReadInputsAsync(new[] { varId });
        Assert.False(read.ContainsKey(varId));

        await driver.DisconnectAsync();
    }

    // --- Dirección inválida ---

    [Fact]
    public async Task InvalidAddress_ReturnsError()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "no-such-area:12",
            DataType = "uint16"
        }});
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.False(read.ContainsKey(varId));
    }

    // --- Bulk read (varios coils en una llamada) ---

    [Fact]
    public async Task BulkRead_MultipleCoils_SingleCall()
    {
        _server.DataStore.CoilDiscretes.WritePoints(0, new[] { true, false, true });

        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        _driver.Configure(new[]
        {
            CoilBinding(v1, 0),
            CoilBinding(v2, 1),
            CoilBinding(v3, 2)
        });
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { v1, v2, v3 });
        Assert.Equal(3, read.Count);
        Assert.True(read[v1].AsBool());
        Assert.False(read[v2].AsBool());
        Assert.True(read[v3].AsBool());
    }

    // --- Reconnect tras desconexión ---

    [Fact]
    public async Task Reconnect_AfterDisconnect_Succeeds()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { CoilBinding(varId, 3) });
        await ConnectAsync();

        await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.Bool(true) });
        await _driver.DisconnectAsync();

        var reconnect = await _driver.ConnectAsync();
        Assert.True(reconnect.Success, reconnect.Error);

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.True(read[varId].AsBool());
    }
}