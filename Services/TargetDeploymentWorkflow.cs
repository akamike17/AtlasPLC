using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

public sealed record DeploymentWorkflowResult(bool Succeeded, string State, string Message, TargetOperationResult? Operation = null);

/// <summary>Workflow único de deployment: gate → adapter → compatibilidad → operación. Nunca suplanta un vendor toolchain.</summary>
public sealed class TargetDeploymentWorkflow(IEnumerable<IPlcTargetAdapter> adapters, IProgramValidationPipeline validation)
{
    private readonly IReadOnlyList<IPlcTargetAdapter> _adapters = adapters.ToList();

    public async Task<DeploymentWorkflowResult> DeployAsync(string targetId, PlcProgramDefinition program, DeploymentRequest request, CancellationToken ct = default)
    {
        var adapter = Resolve(targetId);
        if (adapter is null) return new(false, "Unsupported", $"No existe adapter operativo registrado para '{targetId}'.");
        var gate = validation.Validate(program.Logic, program.Variables.ToDictionary(v => v.Id), ValidationOperation.Deploy, new ValidationContext { SafeStates = program.Failsafe });
        if (!gate.Allowed) return new(false, "Blocked", "Deployment detenido por validación: " + string.Join(" ", gate.Report.Issues.Select(i => i.Message).Take(3)));
        if (!request.IsComplete) return new(false, "Blocked", "Deployment detenido: falta una confirmación verificable completa.");
        var compatibility = await adapter.ValidateAsync(program, ct);
        if (compatibility.Status == CompatibilityReport.CompatibilityStatus.Blocked) return new(false, "Blocked", string.Join(" ", compatibility.Messages));
        var operation = await adapter.DeployAsync(program, request, ct);
        return new(operation.Success, operation.Success ? "Deployed" : "Failed", operation.Detail ?? "Sin detalle.", operation);
    }

    private IPlcTargetAdapter? Resolve(string targetId) => targetId.ToLowerInvariant() switch
    {
        "atlas-simulation" or "atlasruntime" => _adapters.FirstOrDefault(a => a.Identity.Manufacturer.Equals("AtlasSoftPlc", StringComparison.OrdinalIgnoreCase)),
        "modbus-online" or "modbus" => _adapters.FirstOrDefault(a => a.Identity.Family.Equals("Modbus", StringComparison.OrdinalIgnoreCase)),
        _ => null
    };
}
