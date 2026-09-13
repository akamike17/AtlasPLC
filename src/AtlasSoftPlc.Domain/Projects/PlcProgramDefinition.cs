using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Domain.Projects;

/// <summary>
/// Unidad cargable de programa PLC (biblioteca de programas). Agrupa todo lo necesario
/// para instalar y ejecutar un programa en el runtime: variables, lógica (IR), valores
/// failsafe y el mapa Modbus asociado. Es la abstracción que sustenta la persistencia de
/// una biblioteca de programas dentro del MISMO Atlas SoftPLC.
/// </summary>
public sealed class PlcProgramDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nombre legible del programa ("Tanque de agua", "Riego automático", …).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Versión del programa.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Descripción corta para la biblioteca.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Variables/tags del programa (inputs + outputs).</summary>
    public List<VariableDefinition> Variables { get; set; } = new();

    /// <summary>Lógica (IR) del programa.</summary>
    public LogicProgram Logic { get; set; } = new();

    /// <summary>Failsafe por salida (VariableId → valor).</summary>
    public Dictionary<Guid, PlcValue> Failsafe { get; set; } = new();

    public PlantModel Plant { get; set; } = new();

    /// <summary>Mapa Modbus (VariableKey → dirección textual, p.ej. "coil:0").</summary>
    public Dictionary<string, string> ModbusMap { get; set; } = new();

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Hash determinista del contenido lógico (para identificar el programa activo).</summary>
    public string Hash { get; set; } = string.Empty;
}
