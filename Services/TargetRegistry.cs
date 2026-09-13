using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Registry universal: sus entradas llegan por plugins DI. No contiene catálogo
/// vendor-céntrico ni conoce protocolos; delega el estado al plugin de cada target.
/// </summary>
public sealed class TargetRegistry(IEnumerable<ITargetPlugin> plugins) : ITargetRegistry
{
    private readonly IReadOnlyList<ITargetPlugin> _plugins = plugins
        .GroupBy(p => p.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();

    public IReadOnlyList<TargetDescriptor> GetAll() => _plugins.Select(p => p.Descriptor).ToList();

    public TargetDescriptor? Get(string id) => _plugins
        .FirstOrDefault(p => string.Equals(p.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase))?.Descriptor;

    public Task<TargetRuntimeStatus> GetStatusAsync(string id, CancellationToken ct = default)
    {
        var plugin = _plugins.FirstOrDefault(p => string.Equals(p.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));
        if (plugin is null) return Task.FromResult(new TargetRuntimeStatus("Unsupported", "Target no registrado."));
        var instance = new TargetInstance { Id = id, TargetPluginId = plugin.Descriptor.Id, TargetType = plugin.Descriptor.Id, DisplayName = plugin.Descriptor.DisplayName };
        return plugin.StatusProvider.GetStatusAsync(instance, ct);
    }
}
