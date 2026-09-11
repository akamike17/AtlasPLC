namespace AtlasSoftPlc.Protocols.Modbus;

/// <summary>Tipo de área Modbus referenciado por un binding (sección 34).</summary>
public enum ModbusArea
{
    Coil,
    DiscreteInput,
    InputRegister,
    HoldingRegister
}

/// <summary>
/// Representación parseada de una dirección Modbus con formato textual, p. ej.
/// <c>"coil:0"</c>, <c>"discreteinput:0"</c>, <c>"inputregister:0"</c> o
/// <c>"holdingregister:0"</c>. Admite alias cortos (co, di, ir, hr).
/// </summary>
public readonly struct ModbusAddress
{
    public ModbusArea Area { get; }
    public ushort Address { get; }

    public ModbusAddress(ModbusArea area, ushort address)
    {
        Area = area;
        Address = address;
    }

    /// <summary>Indica si el área es de lectura-escritura (coil/holding register).</summary>
    public bool IsWritable => Area is ModbusArea.Coil or ModbusArea.HoldingRegister;

    /// <summary>
    /// Parsea una dirección textual. Devuelve null si el formato no es reconocible.
    /// </summary>
    public static ModbusAddress? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0 || colon == trimmed.Length - 1)
            return null;

        var rawArea = trimmed[..colon].Trim().ToLowerInvariant();
        if (!ushort.TryParse(trimmed[(colon + 1)..].Trim(), out var address))
            return null;

        var area = rawArea switch
        {
            "coil" or "co" or "coilwrite" => ModbusArea.Coil,
            "discreteinput" or "di" or "input" => ModbusArea.DiscreteInput,
            "inputregister" or "ir" => ModbusArea.InputRegister,
            "holdingregister" or "hr" or "register" or "output" => ModbusArea.HoldingRegister,
            _ => (ModbusArea?)null
        };

        if (area is null)
            return null;

        return new ModbusAddress(area.Value, address);
    }
}