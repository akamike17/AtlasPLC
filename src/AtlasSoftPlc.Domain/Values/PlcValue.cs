using AtlasSoftPlc.Domain.Common;

namespace AtlasSoftPlc.Domain.Values;

/// <summary>
/// Unión tipada de valores PLC. Evita el uso indiscriminado de <c>object</c>.
/// Internamente almacena el valor en su tipo CLR natural y expone conversiones
/// seguras y la comparación tipada entre valores.
/// </summary>
public readonly struct PlcValue : IEquatable<PlcValue>
{
    private readonly object? _value;

    public PlcDataType DataType { get; }
    public bool HasValue => _value is not null;

    public PlcValue(PlcDataType dataType, object? value)
    {
        DataType = dataType;
        _value = ValidateAndNormalize(dataType, value);
    }

    public static PlcValue Null(PlcDataType dataType) => new(dataType, null);

    // --- Constructores de conveniencia ---
    public static PlcValue Bool(bool v) => new(PlcDataType.Bool, v);
    public static PlcValue Int16(short v) => new(PlcDataType.Int16, v);
    public static PlcValue UInt16(ushort v) => new(PlcDataType.UInt16, v);
    public static PlcValue Int32(int v) => new(PlcDataType.Int32, v);
    public static PlcValue UInt32(uint v) => new(PlcDataType.UInt32, v);
    public static PlcValue Int64(long v) => new(PlcDataType.Int64, v);
    public static PlcValue UInt64(ulong v) => new(PlcDataType.UInt64, v);
    public static PlcValue Float(float v) => new(PlcDataType.Float, v);
    public static PlcValue Double(double v) => new(PlcDataType.Double, v);
    public static PlcValue Decimal(decimal v) => new(PlcDataType.Decimal, v);
    public static PlcValue String(string? v) => new(PlcDataType.String, v);
    public static PlcValue DateTime(DateTime v) => new(PlcDataType.DateTime, v);
    public static PlcValue TimeSpan(TimeSpan v) => new(PlcDataType.TimeSpan, v);

    /// <summary>Valor tipado o null si no hay valor.</summary>
    public object? Raw => _value;

    public bool AsBool()
    {
        if (!HasValue) return false;
        return DataType == PlcDataType.Bool
            ? (bool)_value!
            : Convert.ToBoolean(_value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public decimal AsDecimal()
    {
        if (!HasValue) return 0m;
        return _value switch
        {
            System.TimeSpan ts => (decimal)ts.TotalMilliseconds,
            System.DateTime dt => dt.Ticks,
            _ => Convert.ToDecimal(_value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public double AsDouble()
    {
        if (!HasValue) return 0d;
        return _value switch
        {
            System.TimeSpan ts => ts.TotalMilliseconds,
            System.DateTime dt => dt.Ticks,
            _ => Convert.ToDouble(_value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public string AsString()
    {
        if (!HasValue) return string.Empty;
        return _value is IFormattable f
            ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
            : _value!.ToString() ?? string.Empty;
    }

    /// <summary>Devuelve el valor en su tipo CLR natural.</summary>
    public T? As<T>() where T : struct
    {
        if (!HasValue) return null;
        if (_value is T t) return t;
        if (_value is null) return null;
        var converted = Convert.ChangeType(_value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        return converted is T typed ? typed : null;
    }

    private static object? ValidateAndNormalize(PlcDataType type, object? value)
    {
        if (value is null) return null;

        return type switch
        {
            PlcDataType.Bool => Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.Int16 => Convert.ToInt16(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.UInt16 => Convert.ToUInt16(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.Int32 => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.UInt32 => Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.Int64 => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.UInt64 => Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.Float => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.Double => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.Decimal => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
            PlcDataType.String => value as string ?? value.ToString()!,
            PlcDataType.DateTime => value switch
            {
                DateTime dt => dt,
                DateTimeOffset dto => dto.UtcDateTime,
                _ => Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture)
            },
            PlcDataType.TimeSpan => value switch
            {
                System.TimeSpan ts => ts,
                _ => System.TimeSpan.FromMilliseconds(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
            },
            _ => value
        };
    }

    public bool Equals(PlcValue other)
    {
        if (HasValue != other.HasValue) return false;
        if (!HasValue) return true;
        if (DataType != other.DataType) return false;
        if (IsFloatType) return AsDouble().Equals(other.AsDouble());
        return _value!.Equals(other._value);
    }

    public override bool Equals(object? obj) => obj is PlcValue pv && Equals(pv);

    public override int GetHashCode() => HasValue ? _value!.GetHashCode() : 0;

    public bool IsFloatType => DataType is PlcDataType.Float or PlcDataType.Double or PlcDataType.Decimal;

    public bool IsIntegralType => DataType is PlcDataType.Bool or PlcDataType.Int16 or PlcDataType.UInt16
        or PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Int64 or PlcDataType.UInt64;

    public bool IsNumeric => IsFloatType || IsIntegralType;

    public override string ToString() => HasValue ? AsString() : "<null>";

    public static bool operator ==(PlcValue a, PlcValue b) => a.Equals(b);
    public static bool operator !=(PlcValue a, PlcValue b) => !a.Equals(b);
}