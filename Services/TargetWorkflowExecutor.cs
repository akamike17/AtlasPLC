using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Ejecutor de workflows declarados por plugins; los controllers sólo coordinan.</summary>
public sealed class TargetWorkflowExecutor(
    ITargetInstanceRepository instances,
    ITargetPluginRegistry plugins,
    ITargetRuntimeStatusService statuses) : ITargetWorkflowExecutor
{
    public async Task<TargetWorkflowExecutionResult> ExecuteAsync(string instanceId, string actionId, TargetActionRequest request, CancellationToken ct = default)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        if (instance is null) return new(false, "Unsupported", "La instancia de target no existe.");
        var plugin = plugins.Get(instance.TargetPluginId);
        if (plugin is null) return new(false, "Unsupported", $"El plugin '{instance.TargetPluginId}' no está registrado.");

        var step = plugin.WorkflowProvider.Workflow.Steps.FirstOrDefault(x => x.ActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (step is null) return new(false, "Unsupported", $"La acción '{actionId}' no forma parte del workflow de {plugin.Descriptor.DisplayName}.");

        var missing = step.RequiredCapabilities.Where(capability => !plugin.Descriptor.Capabilities.Supports(capability)).ToArray();
        if (missing.Length > 0) return new(false, "Blocked", "Faltan capacidades declaradas: " + string.Join(", ", missing));

        var status = await statuses.GetStatusAsync(instance.Id, ct);
        if (!string.IsNullOrWhiteSpace(step.RequiredState) && !step.RequiredState.Equals("Any", StringComparison.OrdinalIgnoreCase) && !status.State.Equals(step.RequiredState, StringComparison.OrdinalIgnoreCase))
            return new(false, "Blocked", $"La acción requiere estado {step.RequiredState}, pero la instancia está en {status.State}. {status.Detail}");

        if (step.RequiresConfirmation && (!request.Parameters.TryGetValue("confirmed", out var confirmed) || !bool.TryParse(confirmed, out var isConfirmed) || !isConfirmed))
            return new(false, "Blocked", "La acción requiere confirmación explícita.");

        if (plugin.ActionProvider is null)
            return new(false, "Unsupported", $"{plugin.Descriptor.DisplayName} no expone ejecutor para '{actionId}'.");

        var result = await plugin.ActionProvider.ExecuteAsync(instance, actionId, request, ct);
        return new(result.Succeeded, result.Succeeded ? "Completed" : "Failed", result.Message, result);
    }
}
