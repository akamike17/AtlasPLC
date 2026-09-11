using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Values;
using Xunit;

namespace AtlasSoftPlc.Protocols.Modbus.Tests;

/// <summary>
/// Cobertura adicional del ModbusTcpDriver (ramas de BitIndex, escala, discrete input,
/// escritura multi-registro, y errores) contra el servidor local.
/// </summary>
public class ModbusTcpDriverBranchTests : IDisposable
{
    private readonly ModbusTcpServer _server;
    private readonly ModbusTcpDriver _driver;

    public ModbusTcpDriverBranchTests()
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

    private async Task ConnectAsync()
    {
        var result = await _driver.ConnectAsync();
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task DiscreteInput_Read_ReturnsBit()
    {
        _server.DataStore.CoilInputs.WritePoints(3, new[] { true, false, true });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "discreteinput:3",
            DataType = "bool",
            ReadWriteMode = "Read"
        }});
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.True(read[varId].AsBool());
    }

    [Fact]
    public async Task HoldingRegister_WithBitIndex_ReadsBit()
    {
        // word = 0b1010 = 0x000A; bit 3 set, bit 1 set
        _server.DataStore.HoldingRegisters.WritePoints(10, new ushort[] { 0x000A });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:10",
            DataType = "uint16",
            BitIndex = 3,
            ReadWriteMode = "ReadWrite"
        }});
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.True(read[varId].AsBool()); // bit 3 = 1
    }

    [Fact]
    public async Task HoldingRegister_WithBitIndex_WriteSingleBit()
    {
        _server.DataStore.HoldingRegisters.WritePoints(20, new ushort[] { 0x0000 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:20",
            DataType = "uint16",
            BitIndex = 2,
            ReadWriteMode = "ReadWrite"
        }});
        await ConnectAsync();

        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.Bool(true) });
        Assert.True(write.Success, write.Error);

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.True(read[varId].AsBool()); // bit 2 now set
    }

    [Fact]
    public async Task Int32_Write_MultiRegister()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:30",
            DataType = "int32",
            ByteOrder = "BigEndian",
            ReadWriteMode = "ReadWrite"
        }});
        await ConnectAsync();

        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.Int32(123456) });
        Assert.True(write.Success, write.Error);

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Equal(123456, read[varId].As<int>());
    }

    [Fact]
    public async Task Read_WithScale_AndOffset()
    {
        _server.DataStore.HoldingRegisters.WritePoints(40, new ushort[] { 100 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:40",
            DataType = "uint16",
            Scale = 2.0,
            Offset = 10.0,
            ReadWriteMode = "Read"
        }});
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        // Scale applies only to Float/Double/Int32/UInt32, not UInt16, so raw 100
        Assert.Equal((ushort)100, read[varId].As<ushort>());
    }

    [Fact]
    public async Task Int32_Read_WithScale()
    {
        // 100 as int32 [0x0000, 0x0064], scale 2.0 -> 200
        _server.DataStore.HoldingRegisters.WritePoints(50, new ushort[] { 0x0000, 0x0064 });

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:50",
            DataType = "int32",
            ByteOrder = "BigEndian",
            Scale = 2.0,
            ReadWriteMode = "Read"
        }});
        await ConnectAsync();

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Equal(200, read[varId].As<int>());
    }

    [Fact]
    public async Task Read_WithoutConnection_ReturnsNoValue()
    {
        // No ConnectAsync called - should fail gracefully
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:60",
            DataType = "uint16",
            ReadWriteMode = "Read"
        }});

        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Empty(read);
    }

    [Fact]
    public async Task Write_ToReadOnlyArea_Fails()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "inputregister:70",
            DataType = "uint16",
            ReadWriteMode = "Read"
        }});
        await ConnectAsync();

        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [varId] = PlcValue.UInt16(1) });
        Assert.False(write.Success);
    }

    [Fact]
    public async Task Write_UnknownBinding_Fails()
    {
        await ConnectAsync();
        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue>
        {
            [Guid.NewGuid()] = PlcValue.Bool(true)
        });
        Assert.False(write.Success);
    }

    [Fact]
    public async Task Read_EmptyInputs_ReturnsEmpty()
    {
        await ConnectAsync();
        var read = await _driver.ReadInputsAsync(Array.Empty<Guid>());
        Assert.Empty(read);
    }

    [Fact]
    public async Task Write_EmptyOutputs_ReturnsZero()
    {
        await ConnectAsync();
        var write = await _driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue>());
        Assert.True(write.Success);
        Assert.Equal(0, write.WrittenCount);
    }

    [Fact]
    public async Task GetHealth_ReflectsState()
    {
        await ConnectAsync();
        var health = await _driver.GetHealthAsync();
        Assert.Equal(DriverState.Connected, health.State);
        Assert.Equal("modbus-tcp:" + _driver.DriverId.Split(':')[1], health.DriverId);

        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "holdingregister:80",
            DataType = "uint16",
            ReadWriteMode = "Read"
        }});
        await _driver.ReadInputsAsync(new[] { varId });

        var health2 = await _driver.GetHealthAsync();
        Assert.True(health2.TotalReads >= 1);
    }

    [Fact]
    public async Task Configure_NullBindings_Clears()
    {
        var varId = Guid.NewGuid();
        _driver.Configure(new[] { new TagBinding
        {
            VariableId = varId,
            Address = "coil:5",
            DataType = "bool",
            ReadWriteMode = "ReadWrite"
        }});

        _driver.Configure(null!); // clear

        await ConnectAsync();
        var read = await _driver.ReadInputsAsync(new[] { varId });
        Assert.Empty(read); // binding removed
    }

    [Fact]
    public async Task DriverId_And_Capabilities()
    {
        Assert.StartsWith("modbus-tcp:", _driver.DriverId);
        Assert.True(_driver.Capabilities.HasFlag(DriverCapabilities.CanRead));
        Assert.True(_driver.Capabilities.HasFlag(DriverCapabilities.CanWrite));
        Assert.True(_driver.Capabilities.HasFlag(DriverCapabilities.SupportsBulkRead));
        Assert.True(_driver.Capabilities.HasFlag(DriverCapabilities.SupportsBulkWrite));
    }

    [Fact]
    public async Task Connect_MissingHost_Fails()
    {
        var definition = new DeviceDefinition
        {
            Name = "no-host",
            Protocol = DeviceProtocol.ModbusTcp,
            Host = null,
            UnitId = 1
        };
        var driver = new ModbusTcpDriver(definition);
        var result = await driver.ConnectAsync();
        Assert.False(result.Success);
        Assert.Equal(DriverState.Faulted, result.State);
    }
}