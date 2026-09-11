using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Protocols.Modbus;

/// <summary>
/// Conversión entre registros Modbus de 16 bits y valores tipados <see cref="PlcValue"/>,
/// con orden de bytes configurable (sección 34).
///
/// Convención: cada word Modbus se transmite con el byte alto primero (big-endian de byte).
/// El <see cref="ModbusEndianness"/> rige el orden de las *words* (y, en WordSwap, el orden
/// de los bytes dentro de cada word) para tipos de 32/64 bits:
///   - BigEndian   : word más significativa primero (orden natural de red).
///   - LittleEndian: word menos significativa primero.
/// </summary>
public static class RegisterConverter
{
    /// <summary>Reordena las words (16 bits) según el endianness configurado.</summary>
    public static ushort[] ApplyWordOrder(ushort[] words, ModbusEndianness endianness)
    {
        if (words.Length <= 1)
            return words;

        return endianness switch
        {
            ModbusEndianness.LittleEndian => words.Reverse().ToArray(),
            ModbusEndianness.WordSwap => WordSwap(words),
            _ => words
        };
    }

    /// <summary>
    /// Invierte los bytes dentro de cada word sin cambiar el orden de las words,
    /// cubriendo dispositivos que publican little-endian a nivel de byte.
    /// </summary>
    private static ushort[] WordSwap(ushort[] words)
    {
        var result = new ushort[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            result[i] = (ushort)((w >> 8) | (w << 8));
        }
        return result;
    }

    /// <summary>Devuelve la cantidad de registros que ocupa un tipo de dato.</summary>
    public static int RegisterCount(PlcDataType dataType) => dataType switch
    {
        PlcDataType.Bool => 1,
        PlcDataType.Int16 or PlcDataType.UInt16 => 1,
        PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float => 2,
        PlcDataType.Int64 or PlcDataType.UInt64 or PlcDataType.Double => 4,
        _ => 1
    };

    /// <summary>
    /// Convierte un conjunto de registros (words) en un <see cref="PlcValue"/> según el tipo
    /// y endianness indicados. Devuelve false si la cantidad de registros es insuficiente.
    /// </summary>
    public static bool TryFromRegisters(ushort[] words, PlcDataType dataType, ModbusEndianness endianness, out PlcValue value)
    {
        value = default;
        if (words is null || words.Length == 0)
            return false;

        var ordered = ApplyWordOrder(words, endianness);

        switch (dataType)
        {
            case PlcDataType.UInt16:
                value = PlcValue.UInt16(ordered[0]);
                return true;
            case PlcDataType.Int16:
                value = PlcValue.Int16(unchecked((short)ordered[0]));
                return true;
            case PlcDataType.UInt32:
                if (ordered.Length < 2) return false;
                value = PlcValue.UInt32(ToUInt32(ordered));
                return true;
            case PlcDataType.Int32:
                if (ordered.Length < 2) return false;
                value = PlcValue.Int32(unchecked((int)ToUInt32(ordered)));
                return true;
            case PlcDataType.Float:
                if (ordered.Length < 2) return false;
                value = PlcValue.Float(BitConverter.Int32BitsToSingle(unchecked((int)ToUInt32(ordered))));
                return true;
            case PlcDataType.UInt64:
                if (ordered.Length < 4) return false;
                value = PlcValue.UInt64(ToUInt64(ordered));
                return true;
            case PlcDataType.Int64:
                if (ordered.Length < 4) return false;
                value = PlcValue.Int64(unchecked((long)ToUInt64(ordered)));
                return true;
            case PlcDataType.Double:
                if (ordered.Length < 4) return false;
                value = PlcValue.Double(BitConverter.Int64BitsToDouble(unchecked((long)ToUInt64(ordered))));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Convierte un <see cref="PlcValue"/> en registros Modbus según el endianness indicado.
    /// </summary>
    public static bool TryToRegisters(PlcValue value, ModbusEndianness endianness, out ushort[]? words)
    {
        words = null;

        switch (value.DataType)
        {
            case PlcDataType.UInt16:
                words = new[] { value.As<ushort>() ?? 0 };
                return true;
            case PlcDataType.Int16:
                words = new[] { unchecked((ushort)(value.As<short>() ?? 0)) };
                return true;
            case PlcDataType.UInt32:
                words = FromUInt32(value.As<uint>() ?? 0u);
                break;
            case PlcDataType.Int32:
                words = FromUInt32(unchecked((uint)(value.As<int>() ?? 0)));
                break;
            case PlcDataType.Float:
                words = FromUInt32(unchecked((uint)BitConverter.SingleToInt32Bits((float)value.AsDouble())));
                break;
            case PlcDataType.UInt64:
                words = FromUInt64(value.As<ulong>() ?? 0UL);
                break;
            case PlcDataType.Int64:
                words = FromUInt64(unchecked((ulong)(value.As<long>() ?? 0L)));
                break;
            case PlcDataType.Double:
                words = FromUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value.AsDouble())));
                break;
            default:
                return false;
        }

        // words ya están en orden big-endian natural; si es LittleEndian se invierte.
        if (endianness == ModbusEndianness.LittleEndian)
            Array.Reverse(words);

        return true;
    }

    // --- Ensamblado big-endian manual (independiente de la endianness de la máquina) ---

    private static uint ToUInt32(ushort[] words)
    {
        // words[0] es la word más significativa.
        return ((uint)words[0] << 16) | words[1];
    }

    private static ulong ToUInt64(ushort[] words)
    {
        ulong result = 0;
        for (var i = 0; i < words.Length && i < 4; i++)
        {
            result = (result << 16) | words[i];
        }
        return result;
    }

    private static ushort[] FromUInt32(uint value)
        => new ushort[] { (ushort)(value >> 16), (ushort)(value & 0xFFFF) };

    private static ushort[] FromUInt64(ulong value)
        => new ushort[]
        {
            (ushort)(value >> 48),
            (ushort)((value >> 32) & 0xFFFF),
            (ushort)((value >> 16) & 0xFFFF),
            (ushort)(value & 0xFFFF)
        };
}