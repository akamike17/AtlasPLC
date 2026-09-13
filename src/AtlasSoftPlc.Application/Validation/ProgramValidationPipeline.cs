using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Validation;

public enum ValidationOperation { Import, Simulation, Generate, Deploy, Restore }

public sealed record ValidationPolicy(ValidationOperation Operation, ValidationSeverity MinimumBlockingSeverity)
{
    public bool Blocks(ValidationIssue issue) => issue.Severity >= MinimumBlockingSeverity;
    public static ValidationPolicy For(ValidationOperation operation) => operation switch
    {
        ValidationOperation.Simulation => new(operation, ValidationSeverity.Blocker),
        ValidationOperation.Generate => new(operation, ValidationSeverity.Error),
        ValidationOperation.Deploy => new(operation, ValidationSeverity.Warning),
        _ => new(operation, ValidationSeverity.Error)
    };
}

public sealed record PipelineResult(ValidationReport Report, ValidationPolicy Policy)
{
    public bool Allowed => !Report.Issues.Any(Policy.Blocks);
}

public interface IProgramValidationPipeline
{
    PipelineResult Validate(LogicProgram program, IReadOnlyDictionary<Guid, VariableDefinition> variables, ValidationOperation operation, ValidationContext? context = null);
}

public sealed class ProgramValidationPipeline(ValidationService validator) : IProgramValidationPipeline
{
    public PipelineResult Validate(LogicProgram program, IReadOnlyDictionary<Guid, VariableDefinition> variables, ValidationOperation operation, ValidationContext? context = null)
        => new(validator.Validate(program, variables, context), ValidationPolicy.For(operation));
}
