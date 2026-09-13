namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Configuración de la integración Modbus TCP (leída de la sección "Modbus" de appsettings).
/// Modbus es una modalidad de I/O explícita: si <see cref="Enabled"/> es false, el puente
/// no se registra y el runtime funciona únicamente en Simulation.
/// </summary>
public sealed class ModbusOptions
{
    public const string SectionName = "Modbus";

    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;

    /// <summary>Timeout por operación individual (connect/read/write) en ms.</summary>
    public int TimeoutMs { get; set; } = 1000;

    /// <summary>Reintentos por operación (los añade NModbus a nivel de transporte).</summary>
    public int Retries { get; set; } = 2;

    /// <summary>
    /// Intervalo de ciclo del puente en ms (leer inputs → dejar escanear → escribir outputs).
    /// Debe ser mayor que el periodo del scan del runtime para no saturar la red.
    /// </summary>
    public int CycleMs { get; set; } = 100;

    /// <summary>Reintentos de conexión consecutivos antes de entrar en backoff.</summary>
    public int MaxConnectAttempts { get; set; } = 3;
}
