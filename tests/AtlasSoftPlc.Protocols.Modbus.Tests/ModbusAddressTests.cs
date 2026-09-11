using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Protocols.Modbus.Tests;

public class ModbusAddressTests
{
    [Theory]
    [InlineData("coil:0", ModbusArea.Coil, 0)]
    [InlineData("discreteinput:10", ModbusArea.DiscreteInput, 10)]
    [InlineData("inputregister:100", ModbusArea.InputRegister, 100)]
    [InlineData("holdingregister:200", ModbusArea.HoldingRegister, 200)]
    [InlineData("co:5", ModbusArea.Coil, 5)]
    [InlineData("hr:7", ModbusArea.HoldingRegister, 7)]
    public void Parse_ValidAddresses_YieldExpected(string text, ModbusArea area, ushort address)
    {
        var parsed = ModbusAddress.TryParse(text);
        Assert.NotNull(parsed);
        Assert.Equal(area, parsed.Value.Area);
        Assert.Equal(address, parsed.Value.Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("crap")]
    [InlineData("coil:")]
    [InlineData("unknown:12")]
    public void Parse_InvalidAddresses_ReturnNull(string? text)
    {
        Assert.Null(ModbusAddress.TryParse(text));
    }
}

public class RegisterConverterTests
{
    [Fact]
    public void Int32_BigEndian_RoundTrips()
    {
        // 100 = 0x00000064 → words [0x0000, 0x0064] en big-endian.
        var words = new ushort[] { 0x0000, 0x0064 };
        var ok = RegisterConverter.TryFromRegisters(words, PlcDataType.Int32, ModbusEndianness.BigEndian, out var value);
        Assert.True(ok);
        Assert.Equal(100, value.As<int>());
    }

    [Fact]
    public void Int32_LittleEndian_RoundTrips()
    {
        // 100 little-endian → words [0x0064, 0x0000].
        var words = new ushort[] { 0x0064, 0x0000 };
        var ok = RegisterConverter.TryFromRegisters(words, PlcDataType.Int32, ModbusEndianness.LittleEndian, out var value);
        Assert.True(ok);
        Assert.Equal(100, value.As<int>());
    }

    [Fact]
    public void ToRegisters_Int32_BigEndian_ProducesExpectedWords()
    {
        var ok = RegisterConverter.TryToRegisters(PlcValue.Int32(100), ModbusEndianness.BigEndian, out var words);
        Assert.True(ok);
        Assert.Equal(new ushort[] { 0x0000, 0x0064 }, words);
    }
}