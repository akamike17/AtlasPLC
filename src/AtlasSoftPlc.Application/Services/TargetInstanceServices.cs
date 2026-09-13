using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Application.Services;

/// <summary>Persistencia de instancias configuradas, separada del catálogo de plugins.</summary>
public interface ITargetInstanceRepository
{
    Task<IReadOnlyList<TargetInstance>> GetAllAsync(CancellationToken ct = default);
    Task<TargetInstance?> GetAsync(string instanceId, CancellationToken ct = default);
    Task SaveAsync(TargetInstance instance, CancellationToken ct = default);
    Task DeleteAsync(string instanceId, CancellationToken ct = default);
}
