namespace AtlasSoftPlc.Targets;

/// <summary>Contrato puro del ecosistema de targets. No conoce sockets, fabricantes ni UI.</summary>
public interface ITargetDescriptorProvider
{
    TargetDescriptor Descriptor { get; }
}

public interface ITargetConfigurationSchemaProvider
{
    IReadOnlyList<TargetConfigurationField> ConfigurationSchema { get; }
}

public interface ITargetPlugin : ITargetDescriptorProvider, ITargetConfigurationSchemaProvider
{
    ITargetStatusProvider StatusProvider { get; }
    IReadOnlyList<TargetActionDescriptor> Actions { get; }
    ITargetWorkflowProvider WorkflowProvider => new DefaultTargetWorkflowProvider(this);
}

public interface ITargetStatusProvider
{
    Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default);
}

public interface ITargetActionProvider
{
    Task<TargetActionResult> ExecuteAsync(TargetInstance instance, string actionId, TargetActionRequest request, CancellationToken ct = default);
}

public sealed record TargetInstance
{
    public required string Id { get; init; }
    /// <summary>Id del plugin que define el protocolo y sus acciones.</summary>
    public string TargetPluginId { get; init; } = "";
    /// <summary>Alias de compatibilidad para consumidores antiguos.</summary>
    public string TargetType { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IReadOnlyDictionary<string, string> Configuration { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? CredentialReference { get; init; }
}

public sealed record TargetActionDescriptor(string Id, string DisplayName, string Description, bool MutatesExternalState = false)
{
    public bool RequiresConfirmation { get; init; } = MutatesExternalState;
    public IReadOnlyList<TargetCapability> RequiredCapabilities { get; init; } = Array.Empty<TargetCapability>();
    public string RequiredState { get; init; } = "Ready";
    public int Order { get; init; }
}

public sealed record TargetWorkflowStep(string ActionId, string DisplayName, bool Optional = false);
public sealed record TargetWorkflowDefinition(string Id, string DisplayName, IReadOnlyList<TargetWorkflowStep> Steps);

/// <summary>Flujo declarado por un plugin; la UI puede iterar pasos sin conocer el vendor.</summary>
public interface ITargetWorkflowProvider
{
    TargetWorkflowDefinition Workflow { get; }
}

public sealed class DefaultTargetWorkflowProvider(ITargetPlugin plugin) : ITargetWorkflowProvider
{
    public TargetWorkflowDefinition Workflow { get; } = new(plugin.Descriptor.Id, plugin.Descriptor.DisplayName, plugin.Actions.Select(a => new TargetWorkflowStep(a.Id, a.DisplayName)).ToList());
}
public sealed record TargetConfigurationField(string Key, string DisplayName, string Type, bool Required = false, string? DefaultValue = null, bool Secret = false);
public sealed record TargetActionRequest(IReadOnlyDictionary<string, string> Parameters);
public sealed record TargetActionResult(bool Succeeded, string Message, IReadOnlyDictionary<string, string>? Data = null);

/// <summary>Registro compuesto: los plugins se agregan por DI, no por una lista vendor-céntrica.</summary>
public interface ITargetPluginRegistry
{
    IReadOnlyList<ITargetPlugin> GetAll();
    ITargetPlugin? Get(string targetType);
}

public sealed class TargetPluginRegistry(IEnumerable<ITargetPlugin> plugins) : ITargetPluginRegistry
{
    private readonly IReadOnlyList<ITargetPlugin> _plugins = plugins.ToList();
    public IReadOnlyList<ITargetPlugin> GetAll() => _plugins;
    public ITargetPlugin? Get(string targetType) => _plugins.FirstOrDefault(p => string.Equals(p.Descriptor.Id, targetType, StringComparison.OrdinalIgnoreCase));
}
