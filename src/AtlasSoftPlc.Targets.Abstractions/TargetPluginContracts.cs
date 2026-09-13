namespace AtlasSoftPlc.Targets;

/// <summary>Contrato puro del ecosistema de targets. No conoce sockets, fabricantes ni UI.</summary>
public interface ITargetPlugin
{
    TargetDescriptor Descriptor { get; }
    ITargetStatusProvider StatusProvider { get; }
    IReadOnlyList<TargetActionDescriptor> Actions { get; }
    IReadOnlyList<TargetConfigurationField> ConfigurationSchema { get; }
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
    public required string TargetType { get; init; }
    public string DisplayName { get; init; } = "";
    public IReadOnlyDictionary<string, string> Configuration { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record TargetActionDescriptor(string Id, string DisplayName, string Description, bool MutatesExternalState = false);
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
