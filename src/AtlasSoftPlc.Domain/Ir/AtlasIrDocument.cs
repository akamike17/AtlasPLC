using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Domain.Ir;

/// <summary>
/// Raíz canónica de la IR de Atlas (spec §2 "Atlas IR es la fuente de verdad", §39 FASE C).
///
/// Unifica de forma serializable y auditable lo que hoy vive disperso en
/// <c>Project</c> + <c>VariableDefinition</c> + <c>LogicProgram</c> +
/// <c>PlcProgramDefinition.Failsafe</c>. Es independiente de UI, protocolo y runtime,
/// y capaz de producir múltiples backends (spec §2).
///
/// FASE C mantiene la IR mínima:
///   Project, Variable (Input/Output), Boolean expression, Assignment,
///   Interlock, SafeState.
/// </summary>
public sealed class AtlasIrDocument
{
    /// <summary>Identificador estable de la IR (para versionado/migración).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Versión del esquema de IR (auditable, spec §28).</summary>
    public int SchemaVersion { get; init; } = 1;

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Variables del proyecto (inputs y outputs).</summary>
    public List<VariableDefinition> Variables { get; init; } = new();

    /// <summary>Lógica booleana (reglas → assignments).</summary>
    public LogicProgram Logic { get; init; } = new();

    /// <summary>Interlocks explícitos (§57 actual / spec §2.1 InterlockRule).</summary>
    public List<Interlock> Interlocks { get; init; } = new();

    /// <summary>Estados seguros por salida (§39 FASE C SafeState).</summary>
    public List<SafeState> SafeStates { get; init; } = new();

    // ── Accesores de conveniencia ──

    public IReadOnlyDictionary<Guid, VariableDefinition> VariablesById =>
        Variables.ToDictionary(v => v.Id);

    public bool IsValid()
    {
        // La IR mínima es válida si tiene nombre y todas las variables tienen Key única no vacía.
        if (string.IsNullOrWhiteSpace(Name)) return false;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Variables)
        {
            if (string.IsNullOrWhiteSpace(v.Key)) return false;
            if (!keys.Add(v.Key)) return false;
        }
        return true;
    }

    /// <summary>
    /// Convierte la IR canónica a la forma de despliegue actual
    /// (<c>PlcProgramDefinition</c>) sin perder SafeStates → Failsafe.
    /// </summary>
    public PlcProgramDefinition ToProgramDefinition() => new()
    {
        Name = Name,
        Version = SchemaVersion,
        Description = Description,
        Variables = Variables,
        Logic = Logic,
        Failsafe = SafeStates.ToDictionary(s => s.VariableId, s => s.Value),
    };
}