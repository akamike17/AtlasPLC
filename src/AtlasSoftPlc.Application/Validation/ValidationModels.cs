using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Validation;

/// <summary>Severidad de un hallazgo de validación (spec §4).</summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    /// <summary>Bloquea el despliegue (spec §4/§42). Más grave que Error.</summary>
    Blocker,
}

/// <summary>Categoría del diagnóstico (spec §4).</summary>
public enum ValidationCategory
{
    Schema,
    Type,
    Reference,
    Logic,
    ControlFlow,
    Timing,
    Physical,
    Safety,
    TargetCompatibility,
    DeploymentReadiness,
}

public sealed class ValidationIssue
{
    /// <summary>Identificador estable de diagnóstico (p.ej. ATLAS-LOGIC-0001).</summary>
    public string DiagnosticId { get; set; } = string.Empty;

    public ValidationSeverity Severity { get; set; }

    /// <summary>Código corto de regla (p.ej. "REF", "DUPW", "SAFE").</summary>
    public string Code { get; set; } = string.Empty;

    public ValidationCategory Category { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>Por qué es un problema (razonamiento, no solo descripción).</summary>
    public string? Why { get; set; }

    /// <summary>Evidencia concreta (camino, ids, valores detectados).</summary>
    public string? Evidence { get; set; }

    /// <summary>Sugerencia accionable para corregir (spec §7 Hint Engine).</summary>
    public string? Hint { get; set; }

    public Guid? VariableId { get; set; }
    public Guid? RuleId { get; set; }
    public string? ElementId { get; set; }

    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

public sealed class ValidationReport
{
    public bool IsValid => !Issues.Any(i => i.Severity is ValidationSeverity.Error or ValidationSeverity.Blocker);
    public List<ValidationIssue> Issues { get; } = new();
    public DateTime GeneratedUtc { get; } = DateTime.UtcNow;

    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    public int BlockerCount => Issues.Count(i => i.Severity == ValidationSeverity.Blocker);
}

/// <summary>Regla de validación individual (sección 23 / spec §4).</summary>
public interface IValidationRule
{
    string Code { get; }
    IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context);
}

public sealed class ValidationContext
{
    public IReadOnlyDictionary<Guid, VariableDefinition> Variables { get; init; } = new Dictionary<Guid, VariableDefinition>();
    public IReadOnlyCollection<Domain.Devices.DeviceDefinition> Devices { get; init; } = new List<Domain.Devices.DeviceDefinition>();
    public IReadOnlyCollection<Domain.Devices.TagBinding> Bindings { get; init; } = new List<Domain.Devices.TagBinding>();
}