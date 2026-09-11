using AtlasSoftPlc.Domain.Audit;

namespace AtlasSoftPlc.Application.Services;

public interface IAuditRepository
{
    Task AppendAsync(AuditEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default);
}

public interface IHistorianRepository
{
    Task AppendAsync(HistorianSample sample, CancellationToken ct = default);
    Task<IReadOnlyList<HistorianSample>> GetAsync(Guid variableId, DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>Poda muestras anteriores a <paramref name="cutoff"/> (retención por antigüedad).</summary>
    Task<int> PruneOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}

/// <summary>Servicio de auditoría (sección 30/43).</summary>
public sealed class AuditService
{
    private readonly IAuditRepository _repo;

    public AuditService(IAuditRepository repo) => _repo = repo;

    public Task AppendAsync(AuditEvent evt, CancellationToken ct = default) => _repo.AppendAsync(evt, ct);
    public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => _repo.GetRecentAsync(count, ct);

    public Task RecordAsync(string user, AuditEventType action, string entityType, Guid? entityId,
        string? oldValue = null, string? newValue = null, string result = "Success", CancellationToken ct = default)
    {
        return _repo.AppendAsync(new AuditEvent
        {
            User = user,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            Result = result
        }, ct);
    }
}

/// <summary>Servicio historian (sección 33/43) con política de muestreo.</summary>
public sealed class HistorianService
{
    private readonly IHistorianRepository _repo;

    public HistorianService(IHistorianRepository repo) => _repo = repo;

    public Task AppendAsync(HistorianSample sample, CancellationToken ct = default) => _repo.AppendAsync(sample, ct);
    public Task<IReadOnlyList<HistorianSample>> GetAsync(Guid variableId, DateTime from, DateTime to, CancellationToken ct = default)
        => _repo.GetAsync(variableId, from, to, ct);

    /// <summary>Retención por antigüedad: poda las muestras más viejas que <paramref name="retentionDays"/> días.</summary>
    public Task<int> PruneAsync(int retentionDays, DateTime? now = null, CancellationToken ct = default)
        => _repo.PruneOlderThanAsync((now ?? DateTime.UtcNow).AddDays(-retentionDays), ct);
}