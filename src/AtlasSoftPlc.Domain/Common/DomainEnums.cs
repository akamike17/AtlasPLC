namespace AtlasSoftPlc.Domain.Common;

/// <summary>Tipos de dato soportados por el PLC (sección 7).</summary>
public enum PlcDataType
{
    Bool,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    Decimal,
    String,
    DateTime,
    TimeSpan
}

/// <summary>Dirección de una variable dentro del modelo.</summary>
public enum VariableDirection
{
    Input,
    Output,
    Memory,
    Internal
}

/// <summary>Calidad de un valor en runtime (sección 7).</summary>
public enum Quality
{
    Good,
    Uncertain,
    Bad,
    Disconnected,
    Stale,
    Forced,
    Simulated
}

/// <summary>Origen del valor en runtime.</summary>
public enum ValueSource
{
    None,
    Device,
    Logic,
    Manual,
    Forced,
    Simulation,
    Default,
    Retentive
}