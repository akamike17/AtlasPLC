using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

public sealed record DeploymentWorkflowResult(bool Succeeded, string State, string Message, TargetOperationResult? Operation = null);

/// <summary>
/// Flujo único de deployment. La resolución se hace mediante la instancia persistida,
/// su plugin y el adapter que éste expone; no existe una tabla switch por fabricante.
/// </summary>
public sealed class TargetDeploymentWorkflow
{
    private readonly IReadOnlyList<IPlcTargetAdapter> _adapters;
    private readonly IProgramValidationPipeline _validation;
    private readonly ITargetPluginRegistry? _plugins;
    private readonly ITargetInstanceRepository? _instances;

    public TargetDeploymentWorkflow(
        IEnumerable<IPlcTargetAdapter> adapters,
        IProgramValidationPipeline validation,
        ITargetPluginRegistry? plugins = null,
        ITargetInstanceRepository? instances = null)
    {
        _adapters = adapters.ToList();
        _validation = validation;
        _plugins = plugins;
        _instances = instances;
    }

    public async Task<DeploymentWorkflowResult> DeployAsync(string targetId, PlcProgramDefinition program, DeploymentRequest request, CancellationToken ct = default)
    {
        var adapter = await ResolveAdapterAsync(targetId, ct);
        if (adapter is null)
            return new(false, "Unsupported", $"No existe adapter operativo registrado para la instancia '{targetId}'.");

        var gate = _validation.Validate(program.Logic, program.Variables.ToDictionary(v => v.Id), ValidationOperation.Deploy, new ValidationContext { SafeStates = program.Failsafe });
        if (!gate.Allowed)
            return new(false, "Blocked", "Deployment detenido por validación: " + string.Join(" ", gate.Report.Issues.Select(i => i.Message).Take(3)));
        if (!request.IsComplete)
            return new(false, "Blocked", "Deployment detenido: falta una confirmación verificable completa.");

        var compatibility = await adapter.ValidateAsync(program, ct);
        if (compatibility.Status == CompatibilityReport.CompatibilityStatus.Blocked)
            return new(false, "Blocked", string.Join(" ", compatibility.Messages));

        var operation = await adapter.DeployAsync(program, request, ct);
        return new(operation.Success, operation.Success ? "Deployed" : "Failed", operation.Detail ?? "Sin detalle.", operation);
    }

    private async Task<IPlcTargetAdapter?> ResolveAdapterAsync(string instanceId, CancellationToken ct)
    {
        if (_instances is null || _plugins is null)
            return null;

        var instance = await _instances.GetAsync(instanceId, ct);
        if (instance is null)
            return null;

        var plugin = _plugins.Get(instance.TargetPluginId);
        if (plugin is ITargetAdapterProvider provider)
            return provider.Adapter;

        // Legacy adapters can still participate when their declared identity matches
        // the plugin descriptor. This keeps the core extensible without target IDs.
        return _adapters.FirstOrDefault(adapter =>
            string.Equals(adapter.Identity.Manufacturer, plugin?.Descriptor.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(adapter.Identity.Family, plugin?.Descriptor.Family, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(adapter.Identity.Model, plugin?.Descriptor.Model, StringComparison.OrdinalIgnoreCase));
    }
}
