namespace AtlasSoftPlc.Targets;

public enum ToolchainMode { Automatic, Assisted, ExportOnly, Unavailable }

public sealed record ToolchainDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string TargetPluginId { get; init; }
    public string Vendor { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public ToolchainMode Mode { get; init; } = ToolchainMode.Unavailable;
    public bool Detected { get; init; }
    public bool Configured { get; init; }
    public bool CanCompile { get; init; }
    public bool CanDeploy { get; init; }
}

public sealed record ToolchainStatus(
    string State,
    string Detail,
    bool Detected,
    bool Configured,
    bool CanCompile,
    bool CanDeploy);

public interface IToolchainPlugin
{
    ToolchainDescriptor Descriptor { get; }
    Task<ToolchainStatus> GetStatusAsync(CancellationToken ct = default);
}

public interface IToolchainRegistry
{
    IReadOnlyList<IToolchainPlugin> GetAll();
    IToolchainPlugin? Get(string id);
}

public sealed class ToolchainRegistry(IEnumerable<IToolchainPlugin> plugins) : IToolchainRegistry
{
    private readonly IReadOnlyList<IToolchainPlugin> _plugins = plugins.ToList();
    public IReadOnlyList<IToolchainPlugin> GetAll() => _plugins;
    public IToolchainPlugin? Get(string id) => _plugins.FirstOrDefault(p => string.Equals(p.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));
}
