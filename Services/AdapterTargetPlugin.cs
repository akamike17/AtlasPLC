using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Bridge explícito entre un adapter operativo y el contrato de plugin.</summary>
public sealed class AdapterTargetPlugin(IPlcTargetAdapter adapter) : ITargetPlugin
{
    public TargetDescriptor Descriptor { get; } = new()
    {
        Id = adapter.Identity.Model,
        DisplayName = $"{adapter.Identity.Manufacturer} {adapter.Identity.Family}",
        Manufacturer = adapter.Identity.Manufacturer,
        Family = adapter.Identity.Family,
        Model = adapter.Identity.Model,
        Category = adapter.Identity.Manufacturer.Equals("Generic", StringComparison.OrdinalIgnoreCase) ? TargetCategory.OnlineIo : TargetCategory.InternalSimulation,
        Description = "Plugin operativo compuesto desde un adapter registrado.",
        Capabilities = adapter.Capabilities,
        ImplementationState = TargetImplementationState.Ready,
        DeploymentMode = adapter.Capabilities.Supports(TargetCapability.Simulate) ? DeploymentMode.MonitorOnly : DeploymentMode.Unsupported
    };

    public ITargetStatusProvider StatusProvider { get; } = new AdapterStatusProvider(adapter);
    public IReadOnlyList<TargetActionDescriptor> Actions { get; } = Array.Empty<TargetActionDescriptor>();
    public IReadOnlyList<TargetConfigurationField> ConfigurationSchema { get; } = Array.Empty<TargetConfigurationField>();

    private sealed class AdapterStatusProvider(IPlcTargetAdapter adapter) : ITargetStatusProvider
    {
        public Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default)
            => Task.FromResult(new TargetRuntimeStatus("Ready", $"Adapter {adapter.Identity.Manufacturer}/{adapter.Identity.Family}/{adapter.Identity.Model} cargado por DI."));
    }
}
