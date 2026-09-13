using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Application.Services;

public sealed class AtlasIrSimulationResult
{
    public ValidationReport Validation { get; init; } = new();
    public PlcProgramDefinition? Program { get; init; }
    public TargetOperationResult? Operation { get; init; }
    public bool Installed => Operation?.Success == true;
}

/// <summary>Ruta canónica IR: validate → lower → adapter simulation.</summary>
public sealed class AtlasIrSimulationPipeline
{
    private readonly IReadOnlyList<IValidationRule> _rules = Array.Empty<IValidationRule>();
    private readonly IProgramValidationPipeline? _pipeline;
    public AtlasIrSimulationPipeline(IEnumerable<IValidationRule> rules) => _rules = rules.ToList();
    public AtlasIrSimulationPipeline(IProgramValidationPipeline pipeline) => _pipeline = pipeline;

    public async Task<AtlasIrSimulationResult> SimulateAsync(AtlasIrDocument ir, IPlcTargetAdapter target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(target);
        var variables = ir.Variables.ToDictionary(v => v.Id);
        var report = (_pipeline?.Validate(ir.Logic, variables, ValidationOperation.Simulation, new ValidationContext
        {
            Variables = variables,
            SafeStates = ir.SafeStates.ToDictionary(s => s.VariableId, s => s.Value),
            Interlocks = ir.Interlocks
        }).Report) ?? new ValidationService(_rules).Validate(ir.Logic, variables, new ValidationContext
        {
            Variables = variables,
            SafeStates = ir.SafeStates.ToDictionary(s => s.VariableId, s => s.Value),
            Interlocks = ir.Interlocks
        });
        if (!ir.IsValid() || !report.IsValid)
            return new AtlasIrSimulationResult { Validation = report };

        var program = ir.ToProgramDefinition();
        program.Hash = CanonicalProgramHasher.ComputeHash(program);
        var operation = await target.SimulateAsync(program, ct).ConfigureAwait(false);
        return new AtlasIrSimulationResult { Validation = report, Program = program, Operation = operation };
    }
}
