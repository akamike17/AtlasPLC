using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Validation;

/// <summary>Severidad de un hallazgo de validación.</summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? VariableId { get; set; }
    public Guid? RuleId { get; set; }

    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

public sealed class ValidationReport
{
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);
    public List<ValidationIssue> Issues { get; } = new();
    public DateTime GeneratedUtc { get; } = DateTime.UtcNow;

    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
}

/// <summary>Regla de validación individual (sección 23).</summary>
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