using System.Collections.Concurrent;

namespace AtlasSoftPlc.Web.Services;

public sealed record OpenPlcSession(string InstanceId, string AccessToken, DateTimeOffset? ExpiresUtc);

public interface IOpenPlcSessionStore
{
    bool TryGet(string instanceId, out OpenPlcSession session);
    void Set(string instanceId, string accessToken, DateTimeOffset? expiresUtc = null);
    void Remove(string instanceId);
}

/// <summary>
/// Sesiones JWT por instancia. Es deliberadamente in-memory: nunca se escribe
/// el token en SQLite, logs, cookies ni configuración de TargetInstance.
/// </summary>
public sealed class OpenPlcSessionStore : IOpenPlcSessionStore
{
    private readonly ConcurrentDictionary<string, OpenPlcSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string instanceId, out OpenPlcSession session)
    {
        if (_sessions.TryGetValue(instanceId, out session!) && (session.ExpiresUtc is null || session.ExpiresUtc > DateTimeOffset.UtcNow))
            return true;
        _sessions.TryRemove(instanceId, out _);
        session = null!;
        return false;
    }

    public void Set(string instanceId, string accessToken, DateTimeOffset? expiresUtc = null)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(accessToken)) return;
        _sessions[instanceId] = new OpenPlcSession(instanceId, accessToken, expiresUtc);
    }

    public void Remove(string instanceId) => _sessions.TryRemove(instanceId, out _);
}
