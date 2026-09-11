using AtlasSoftPlc.Domain.Audit;

namespace AtlasSoftPlc.Domain.Runtime;

/// <summary>
/// Sumidero de auditoría de seguridad operacional para el runtime. Definida en Domain
/// (igual que <see cref="IRuntimeNotifier"/>) para que el runtime pueda registrar eventos
/// de seguridad (force, clear, expiración, fault, shutdown) sin acoplarse a Application.
/// </summary>
public interface IRuntimeAuditSink
{
    Task AppendAsync(AuditEvent evt, CancellationToken ct = default);
}