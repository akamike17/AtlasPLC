namespace AtlasSoftPlc.Protocols.Modbus;

/// <summary>Orden de bytes para tipos multi-registro (sección 34).</summary>
public enum ModbusEndianness
{
    /// <summary>Registro más significativo primero (orden de red).</summary>
    BigEndian,

    /// <summary>Registro menos significativo primero.</summary>
    LittleEndian,

    /// <summary>Alterna el orden de los registros (word swap) para modos mixtos.</summary>
    WordSwap
}

public static class ModbusEndiannessExtensions
{
    /// <summary>Traduce la etiqueta textual de un binding a un <see cref="ModbusEndianness"/>.</summary>
    public static ModbusEndianness Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ModbusEndianness.BigEndian;

        return text.Trim().ToLowerInvariant() switch
        {
            "littleendian" or "little" or "le" => ModbusEndianness.LittleEndian,
            "wordswap" or "word" or "ws" or "biglittle" or "littlebig" => ModbusEndianness.WordSwap,
            _ => ModbusEndianness.BigEndian
        };
    }
}