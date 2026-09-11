using System.Collections.Concurrent;

namespace AtlasSoftPlc.Web.Auth;

/// <summary>
/// Resultado de un intento de autenticación.
/// </summary>
public enum LoginResult
{
    Success,
    InvalidCredentials,
    LockedOut,
    RateLimited,
}

/// <summary>
/// Autenticación con lockout por usuario y rate limiting por IP (defensa anti fuerza bruta).
/// Estado en memoria: el lockout/rate-limit no persiste entre reinicios (aceptable para
/// un solo nodo; en despliegue multi-nodo debería respaldarse en un store compartido).
/// </summary>
public sealed class AuthService
{
    private readonly IUserStore _users;
    private readonly PasswordHasher _hasher;
    private readonly AuthOptions _options;

    // lockout por usuario
    private readonly ConcurrentDictionary<string, (int Fails, DateTimeOffset LockedUntil)> _failures = new(StringComparer.OrdinalIgnoreCase);
    // rate limit por IP (token bucket simple: ventana fija)
    private readonly ConcurrentDictionary<string, (DateTimeOffset WindowStart, int Count)> _rate = new(StringComparer.Ordinal);

    public AuthService(IUserStore users, PasswordHasher hasher, AuthOptions options)
    {
        _users = users;
        _hasher = hasher;
        _options = options;
    }

    public LoginResult Authenticate(string username, string password, string remoteIp)
    {
        // 1. Rate limiting por IP
        if (IsRateLimited(remoteIp))
            return LoginResult.RateLimited;

        // 2. Lockout por usuario
        if (_failures.TryGetValue(username, out var f) && f.LockedUntil > DateTimeOffset.UtcNow)
            return LoginResult.LockedOut;

        // 3. Verificación de credenciales
        var user = _users.FindByUsername(username);
        if (user is null || !_hasher.Verify(password, user.PasswordHash))
        {
            RegisterFailure(username);
            return LoginResult.InvalidCredentials;
        }

        // éxito: resetear contadores
        _failures.TryRemove(username, out _);
        return LoginResult.Success;
    }

    public UserAccount? FindUser(string username) => _users.FindByUsername(username);

    private bool IsRateLimited(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        var windowMs = _options.RateLimitWindow.TotalMilliseconds;
        var entry = _rate.AddOrUpdate(
            ip,
            _ => (now, 1),
            (_, existing) =>
            {
                if ((now - existing.WindowStart).TotalMilliseconds > windowMs)
                    return (now, 1); // nueva ventana
                return (existing.WindowStart, existing.Count + 1);
            });

        return entry.Count > _options.MaxAttemptsPerWindow;
    }

    private void RegisterFailure(string username)
    {
        var now = DateTimeOffset.UtcNow;
        _failures.AddOrUpdate(
            username,
            _ => (1, now.Add(_options.LockoutDuration)),
            (_, existing) =>
            {
                var fails = existing.Fails + 1;
                if (fails >= _options.MaxFailedAttempts)
                    return (fails, now.Add(_options.LockoutDuration));
                return (fails, now); // no locked yet
            });
    }
}

public sealed class AuthOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int MaxAttemptsPerWindow { get; set; } = 20;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
}