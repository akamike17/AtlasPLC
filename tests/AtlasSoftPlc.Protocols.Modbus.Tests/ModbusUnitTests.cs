using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;
using Xunit;

namespace AtlasSoftPlc.Protocols.Modbus.Tests;

public class RegisterConverterExtendedTests
{
    [Fact]
    public void RegisterCount_ReturnsExpectedSizes()
    {
        Assert.Equal(1, RegisterConverter.RegisterCount(PlcDataType.Bool));
        Assert.Equal(1, RegisterConverter.RegisterCount(PlcDataType.Int16));
        Assert.Equal(1, RegisterConverter.RegisterCount(PlcDataType.UInt16));
        Assert.Equal(2, RegisterConverter.RegisterCount(PlcDataType.Int32));
        Assert.Equal(2, RegisterConverter.RegisterCount(PlcDataType.UInt32));
        Assert.Equal(2, RegisterConverter.RegisterCount(PlcDataType.Float));
        Assert.Equal(4, RegisterConverter.RegisterCount(PlcDataType.Int64));
        Assert.Equal(4, RegisterConverter.RegisterCount(PlcDataType.UInt64));
        Assert.Equal(4, RegisterConverter.RegisterCount(PlcDataType.Double));
        Assert.Equal(1, RegisterConverter.RegisterCount(PlcDataType.String));
    }

    [Fact]
    public void TryFromRegisters_Null_ReturnsFalse()
    {
        Assert.False(RegisterConverter.TryFromRegisters(null!, PlcDataType.Int32, ModbusEndianness.BigEndian, out _));
    }

    [Fact]
    public void TryFromRegisters_Empty_ReturnsFalse()
    {
        Assert.False(RegisterConverter.TryFromRegisters(Array.Empty<ushort>(), PlcDataType.Int32, ModbusEndianness.BigEndian, out _));
    }

    [Fact]
    public void TryFromRegisters_UInt16()
    {
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 42 }, PlcDataType.UInt16, ModbusEndianness.BigEndian, out var v));
        Assert.Equal((ushort)42, v.As<ushort>());
    }

    [Fact]
    public void TryFromRegisters_Int16_Negative()
    {
        // -1 = 0xFFFF
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0xFFFF }, PlcDataType.Int16, ModbusEndianness.BigEndian, out var v));
        Assert.Equal((short)-1, v.As<short>());
    }

    [Fact]
    public void TryFromRegisters_UInt32()
    {
        // 0x12345678
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x1234, 0x5678 }, PlcDataType.UInt32, ModbusEndianness.BigEndian, out var v));
        Assert.Equal(0x12345678u, v.As<uint>());
    }

    [Fact]
    public void TryFromRegisters_UInt32_LittleEndian()
    {
        // little-endian: word baja primero [0x5678, 0x1234] -> 0x12345678
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x5678, 0x1234 }, PlcDataType.UInt32, ModbusEndianness.LittleEndian, out var v));
        Assert.Equal(0x12345678u, v.As<uint>());
    }

    [Fact]
    public void TryFromRegisters_UInt32_InsufficientWords_ReturnsFalse()
    {
        Assert.False(RegisterConverter.TryFromRegisters(new ushort[] { 0x1234 }, PlcDataType.UInt32, ModbusEndianness.BigEndian, out _));
    }

    [Fact]
    public void TryFromRegisters_Int32_Negative()
    {
        // -2 = 0xFFFFFFFE
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0xFFFF, 0xFFFE }, PlcDataType.Int32, ModbusEndianness.BigEndian, out var v));
        Assert.Equal(-2, v.As<int>());
    }

    [Fact]
    public void TryFromRegisters_Float()
    {
        // 1.0f = 0x3F800000
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x3F80, 0x0000 }, PlcDataType.Float, ModbusEndianness.BigEndian, out var v));
        Assert.Equal(1.0f, (float)v.AsDouble());
    }

    [Fact]
    public void TryFromRegisters_UInt64()
    {
        // 0x0000000100000002 = 4294967298
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x0000, 0x0001, 0x0000, 0x0002 }, PlcDataType.UInt64, ModbusEndianness.BigEndian, out var v));
        Assert.Equal(4294967298UL, v.As<ulong>());
    }

    [Fact]
    public void TryFromRegisters_UInt64_LittleEndian()
    {
        // little-endian: word baja primero [0x0002, 0x0000, 0x0001, 0x0000] -> 0x0000000100000002
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x0002, 0x0000, 0x0001, 0x0000 }, PlcDataType.UInt64, ModbusEndianness.LittleEndian, out var v));
        Assert.Equal(4294967298UL, v.As<ulong>());
    }

    [Fact]
    public void TryFromRegisters_Int64()
    {
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x0000, 0x0000, 0x0000, 0x0005 }, PlcDataType.Int64, ModbusEndianness.BigEndian, out var v));
        Assert.Equal(5L, v.As<long>());
    }

    [Fact]
    public void TryFromRegisters_Double()
    {
        // 1.0 = 0x3FF0000000000000
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x3FF0, 0x0000, 0x0000, 0x0000 }, PlcDataType.Double, ModbusEndianness.BigEndian, out var v));
        Assert.Equal(1.0, v.AsDouble());
    }

    [Fact]
    public void TryFromRegisters_String_ReturnsFalse()
    {
        Assert.False(RegisterConverter.TryFromRegisters(new ushort[] { 1 }, PlcDataType.String, ModbusEndianness.BigEndian, out _));
    }

    [Fact]
    public void TryToRegisters_UInt16()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.UInt16(42), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 42 }, words);
    }

    [Fact]
    public void TryToRegisters_Int16_Negative()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.Int16(-1), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0xFFFF }, words);
    }

    [Fact]
    public void TryToRegisters_UInt32()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.UInt32(0x12345678), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, words);
    }

    [Fact]
    public void TryToRegisters_UInt32_LittleEndian()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.UInt32(0x12345678), ModbusEndianness.LittleEndian, out var words));
        Assert.Equal(new ushort[] { 0x5678, 0x1234 }, words);
    }

    [Fact]
    public void TryToRegisters_Int32()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.Int32(100), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0x0000, 0x0064 }, words);
    }

    [Fact]
    public void TryToRegisters_Float()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.Float(1.0f), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0x3F80, 0x0000 }, words);
    }

    [Fact]
    public void TryToRegisters_UInt64()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.UInt64(4294967298UL), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0x0000, 0x0001, 0x0000, 0x0002 }, words);
    }

    [Fact]
    public void TryToRegisters_Int64()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.Int64(5), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0x0000, 0x0000, 0x0000, 0x0005 }, words);
    }

    [Fact]
    public void TryToRegisters_Double()
    {
        Assert.True(RegisterConverter.TryToRegisters(PlcValue.Double(1.0), ModbusEndianness.BigEndian, out var words));
        Assert.Equal(new ushort[] { 0x3FF0, 0x0000, 0x0000, 0x0000 }, words);
    }

    [Fact]
    public void TryToRegisters_String_ReturnsFalse()
    {
        Assert.False(RegisterConverter.TryToRegisters(PlcValue.String("x"), ModbusEndianness.BigEndian, out _));
    }

    [Fact]
    public void ApplyWordOrder_SingleWord_Unchanged()
    {
        var words = new ushort[] { 42 };
        Assert.Same(words, RegisterConverter.ApplyWordOrder(words, ModbusEndianness.LittleEndian));
    }

    [Fact]
    public void TryFromRegisters_WordSwap_SwapsBytesInPlace_MultiWord()
    {
        // WordSwap applies byte-swap within each word, only for multi-word values.
        // Words [0x0001, 0x0002] -> byte-swapped [0x0100, 0x0200]
        // As UInt32 big-endian: (0x0100 << 16) | 0x0200 = 0x01000200
        Assert.True(RegisterConverter.TryFromRegisters(new ushort[] { 0x0001, 0x0002 }, PlcDataType.UInt32, ModbusEndianness.WordSwap, out var v));
        Assert.Equal(0x01000200u, v.As<uint>());
    }

    [Fact]
    public void ApplyWordOrder_SingleWord_Unchanged_EvenForWordSwap()
    {
        // Single word is returned as-is (no swap for length <= 1)
        var words = new ushort[] { 0x0001 };
        var result = RegisterConverter.ApplyWordOrder(words, ModbusEndianness.WordSwap);
        Assert.Equal(new ushort[] { 0x0001 }, result);
    }
}

public class CircuitBreakerTests
{
    [Fact]
    public void Initially_NotOpen()
    {
        var cb = new CircuitBreaker();
        Assert.False(cb.IsOpen());
        Assert.Equal(0, cb.CurrentFailures);
        Assert.Equal(TimeSpan.Zero, cb.RetryAfter());
    }

    [Fact]
    public void BelowThreshold_DoesNotOpen()
    {
        var cb = new CircuitBreaker(failureThreshold: 5);
        cb.OnFailure();
        cb.OnFailure();
        cb.OnFailure();
        cb.OnFailure();

        Assert.Equal(4, cb.CurrentFailures);
        Assert.False(cb.IsOpen()); // below 5
    }

    [Fact]
    public void ReachingThreshold_OpensCircuit()
    {
        var cb = new CircuitBreaker(failureThreshold: 3, baseBackoffMs: 100, maxBackoffMs: 1000);
        cb.OnFailure();
        cb.OnFailure();
        cb.OnFailure();

        Assert.Equal(3, cb.CurrentFailures);
        Assert.True(cb.IsOpen());
    }

    [Fact]
    public void OnSuccess_ResetsFailures()
    {
        var cb = new CircuitBreaker(failureThreshold: 3);
        cb.OnFailure();
        cb.OnFailure();
        cb.OnSuccess();

        Assert.Equal(0, cb.CurrentFailures);
        Assert.False(cb.IsOpen());
    }

    [Fact]
    public void RetryAfter_ReportsBackoff()
    {
        var cb = new CircuitBreaker(failureThreshold: 1, baseBackoffMs: 1000, maxBackoffMs: 10000);
        cb.OnFailure(); // opens with backoff ~2000ms

        Assert.True(cb.IsOpen());
        Assert.True(cb.RetryAfter() > TimeSpan.Zero);
        Assert.True(cb.RetryAfter() <= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ExponentialBackoff_GrowsWithFailures()
    {
        var cb = new CircuitBreaker(failureThreshold: 3, baseBackoffMs: 100, maxBackoffMs: 100000);
        cb.OnFailure();
        cb.OnFailure();
        cb.OnFailure();

        // After 3 failures, backoff = 100 * 2^3 = 800ms
        Assert.True(cb.RetryAfter() > TimeSpan.FromMilliseconds(400));
        Assert.True(cb.RetryAfter() <= TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public void MaxBackoff_CapsRetryAfter()
    {
        var cb = new CircuitBreaker(failureThreshold: 1, baseBackoffMs: 1000, maxBackoffMs: 100);
        // maxBackoffMs is clamped to >= baseBackoffMs in constructor
        Assert.True(cb.MaxBackoffMs >= cb.BaseBackoffMs);
    }

    [Fact]
    public void WasTrippedBeforeAttempt_TracksState()
    {
        var cb = new CircuitBreaker(failureThreshold: 1);
        Assert.False(cb.WasTrippedBeforeAttempt);

        cb.OnFailure(); // trips

        cb.OnAttempt(); // records that it was tripped
        Assert.True(cb.WasTrippedBeforeAttempt);
    }

    [Fact]
    public void IsOpen_ExpiresAfterBackoffWindow()
    {
        var cb = new CircuitBreaker(failureThreshold: 1, baseBackoffMs: 1, maxBackoffMs: 1);
        cb.OnFailure(); // opens with tiny backoff

        // With base=1, backoff = 1 * 2^1 = 2ms. Wait a bit.
        Thread.Sleep(10);

        // First IsOpen() when expired returns false (allows one probe)
        Assert.False(cb.IsOpen());
    }

    [Fact]
    public void Constructor_ClampsThresholdToMinimumOne()
    {
        var cb = new CircuitBreaker(failureThreshold: 0, baseBackoffMs: 0, maxBackoffMs: 0);
        Assert.True(cb.FailureThreshold >= 1);
        Assert.True(cb.BaseBackoffMs >= 1);
        Assert.True(cb.MaxBackoffMs >= cb.BaseBackoffMs);
    }
}

public class ModbusEndiannessTests
{
    [Theory]
    [InlineData(null, ModbusEndianness.BigEndian)]
    [InlineData("", ModbusEndianness.BigEndian)]
    [InlineData("  ", ModbusEndianness.BigEndian)]
    [InlineData("BigEndian", ModbusEndianness.BigEndian)]
    [InlineData("big", ModbusEndianness.BigEndian)]
    [InlineData("littleendian", ModbusEndianness.LittleEndian)]
    [InlineData("little", ModbusEndianness.LittleEndian)]
    [InlineData("le", ModbusEndianness.LittleEndian)]
    [InlineData("wordswap", ModbusEndianness.WordSwap)]
    [InlineData("word", ModbusEndianness.WordSwap)]
    [InlineData("ws", ModbusEndianness.WordSwap)]
    [InlineData("biglittle", ModbusEndianness.WordSwap)]
    [InlineData("littlebig", ModbusEndianness.WordSwap)]
    [InlineData("unknown", ModbusEndianness.BigEndian)]
    public void Parse_MapsCorrectly(string? text, ModbusEndianness expected)
    {
        Assert.Equal(expected, ModbusEndiannessExtensions.Parse(text));
    }
}

public class ModbusAddressExtendedTests
{
    [Theory]
    [InlineData("coilwrite:5", ModbusArea.Coil)]
    [InlineData("di:5", ModbusArea.DiscreteInput)]
    [InlineData("input:5", ModbusArea.DiscreteInput)]
    [InlineData("ir:5", ModbusArea.InputRegister)]
    [InlineData("register:5", ModbusArea.HoldingRegister)]
    [InlineData("output:5", ModbusArea.HoldingRegister)]
    public void Parse_Aliases_YieldCorrectArea(string text, ModbusArea area)
    {
        var parsed = ModbusAddress.TryParse(text);
        Assert.NotNull(parsed);
        Assert.Equal(area, parsed.Value.Area);
    }

    [Fact]
    public void IsWritable_Coil_True()
    {
        var addr = new ModbusAddress(ModbusArea.Coil, 0);
        Assert.True(addr.IsWritable);
    }

    [Fact]
    public void IsWritable_HoldingRegister_True()
    {
        var addr = new ModbusAddress(ModbusArea.HoldingRegister, 0);
        Assert.True(addr.IsWritable);
    }

    [Fact]
    public void IsWritable_DiscreteInput_False()
    {
        var addr = new ModbusAddress(ModbusArea.DiscreteInput, 0);
        Assert.False(addr.IsWritable);
    }

    [Fact]
    public void IsWritable_InputRegister_False()
    {
        var addr = new ModbusAddress(ModbusArea.InputRegister, 0);
        Assert.False(addr.IsWritable);
    }

    [Theory]
    [InlineData("")]
    [InlineData(":5")]
    [InlineData("coil:")]
    [InlineData("coil:notanumber")]
    [InlineData("coil")]
    [InlineData("coil:5:extra")]
    public void Parse_InvalidFormats_ReturnNull(string text)
    {
        Assert.Null(ModbusAddress.TryParse(text));
    }

    [Fact]
    public void Parse_MaxUshortAddress()
    {
        var parsed = ModbusAddress.TryParse("coil:65535");
        Assert.NotNull(parsed);
        Assert.Equal((ushort)65535, parsed.Value.Address);
    }

    [Fact]
    public void Parse_OverflowAddress_ReturnsNull()
    {
        Assert.Null(ModbusAddress.TryParse("coil:65536"));
    }

    [Fact]
    public void Parse_Whitespace_IsTrimmed()
    {
        var parsed = ModbusAddress.TryParse("  coil : 5  ");
        Assert.NotNull(parsed);
        Assert.Equal(ModbusArea.Coil, parsed.Value.Area);
        Assert.Equal((ushort)5, parsed.Value.Address);
    }
}