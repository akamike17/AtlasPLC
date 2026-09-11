using AtlasSoftPlc.Domain.Alarms;

namespace AtlasSoftPlc.Application.Services;

public interface IAlarmRepository
{
    Task<IReadOnlyList<AlarmDefinition>> GetDefinitionsAsync(CancellationToken ct = default);
    Task SaveDefinitionAsync(AlarmDefinition def, CancellationToken ct = default);
    Task<IReadOnlyList<AlarmInstance>> GetActiveInstancesAsync(CancellationToken ct = default);
    Task SaveInstanceAsync(AlarmInstance instance, CancellationToken ct = default);
}

/// <summary>Servicio de alarmas (secciones 32, 43).</summary>
public sealed class AlarmService
{
    private readonly IAlarmRepository _repo;

    public AlarmService(IAlarmRepository repo) => _repo = repo;

    public Task<IReadOnlyList<AlarmDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
        => _repo.GetDefinitionsAsync(ct);

    public Task SaveDefinitionAsync(AlarmDefinition def, CancellationToken ct = default)
        => _repo.SaveDefinitionAsync(def, ct);

    public Task<IReadOnlyList<AlarmInstance>> GetActiveInstancesAsync(CancellationToken ct = default)
        => _repo.GetActiveInstancesAsync(ct);

    /// <summary>Reconoce (ack) una alarma activa.</summary>
    public async Task AcknowledgeAsync(Guid instanceId, string user, CancellationToken ct = default)
    {
        var items = await _repo.GetActiveInstancesAsync(ct);
        var instance = items.FirstOrDefault(i => i.Id == instanceId);
        if (instance is null) return;

        instance.State = instance.State == AlarmState.ActiveUnacknowledged
            ? AlarmState.ActiveAcknowledged
            : AlarmState.ClearedUnacknowledged;
        instance.AcknowledgedBy = user;
        instance.AcknowledgedUtc = DateTime.UtcNow;
        await _repo.SaveInstanceAsync(instance, ct);
    }
}