using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Domain.Tests;

public class PlcValueTests
{
    private static System.Globalization.CultureInfo _inv =
        System.Globalization.CultureInfo.InvariantCulture;

    [Fact]
    public void Bool_RoundTrips()
    {
        var v = PlcValue.Bool(true);
        Assert.True(v.HasValue);
        Assert.True(v.AsBool());
        Assert.Equal(PlcDataType.Bool, v.DataType);
    }

    [Fact]
    public void Int32_ConvertsBetweenNumericTypes()
    {
        var v = PlcValue.Int32(42);
        Assert.Equal(42, v.As<int>());
        Assert.Equal(42m, v.AsDecimal());
        Assert.Equal(42d, v.AsDouble());
    }

    [Fact]
    public void Float_IsFloatType()
    {
        var v = PlcValue.Float(1.5f);
        Assert.True(v.IsFloatType);
        Assert.True(v.IsNumeric);
        Assert.False(v.IsIntegralType);
    }

    [Fact]
    public void Equality_ComparesValueAndType()
    {
        Assert.Equal(PlcValue.Int32(5), PlcValue.Int32(5));
        Assert.NotEqual(PlcValue.Int32(5), PlcValue.Int32(6));
        Assert.NotEqual(PlcValue.Int32(5), PlcValue.UInt32(5));
    }

    [Fact]
    public void Null_HasNoValue()
    {
        var v = PlcValue.Null(PlcDataType.Bool);
        Assert.False(v.HasValue);
        Assert.False(v.AsBool());
    }

    [Fact]
    public void String_IsConvertedToString()
    {
        var v = PlcValue.String("hola");
        Assert.Equal("hola", v.AsString());
    }

    [Fact]
    public void Decimal_PreservesPrecision()
    {
        var v = PlcValue.Decimal(123.456m);
        Assert.Equal(123.456m, v.As<decimal>());
    }

    [Fact]
    public void Float_Equality_ComparesViaDouble()
    {
        var a = PlcValue.Float(0.1f);
        var b = PlcValue.Float(0.1f);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Bool_FromNumericString_Converts()
    {
        var v = new PlcValue(PlcDataType.Bool, "true");
        Assert.True(v.AsBool());
    }

    [Fact]
    public void Overflow_Throws_ForWrongConversion()
    {
        var v = PlcValue.Int64(long.MaxValue);
        Assert.ThrowsAny<System.OverflowException>(() => v.As<short>());
    }

    [Fact]
    public void GetHashCode_ConsistentWithEquals()
    {
        var a = PlcValue.Int32(7);
        var b = PlcValue.Int32(7);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TimeSpan_FromMilliseconds()
    {
        var v = PlcValue.TimeSpan(System.TimeSpan.FromSeconds(2));
        Assert.Equal(2000d, v.AsDouble());
    }
}