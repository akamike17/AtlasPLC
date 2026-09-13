using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Resuelve el estado desde una instancia persistida, nunca desde el catálogo.</summary>
public interface ITargetRuntimeStatusService
{
    Task<TargetRuntimeStatus> GetStatusAsync(string instanceId, CancellationToken ct = default);
}

public sealed class TargetRuntimeStatusService(
    ITargetInstanceRepository instances,
    ITargetPluginRegistry plugins) : ITargetRuntimeStatusService
{
    public async Task<TargetRuntimeStatus> GetStatusAsync(string instanceId, CancellationToken ct = default)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        if (instance is null) return new TargetRuntimeStatus("NotConfigured", "La instancia no existe en la persistencia.");
        var plugin = plugins.Get(instance.TargetPluginId);
        if (plugin is null) return new TargetRuntimeStatus("Unsupported", $"El plugin '{instance.TargetPluginId}' no está registrado.");
        return await plugin.StatusProvider.GetStatusAsync(instance, ct);
    }
}
