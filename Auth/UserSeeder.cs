using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtlasSoftPlc.Web.Auth;

/// <summary>
/// Siembra cuentas de usuario iniciales desde configuración.
/// Crea los usuarios declarados en "Auth:Seed" (formato: usuario:rol) que aún
/// no existan, usando la contraseña de "Auth:SeedPassword". No modifica las
/// cuentas existentes.
/// En ningún caso persiste credenciales en el código fuente.
/// </summary>
public sealed class UserSeeder
{
    private readonly IUserStore _store;
    private readonly PasswordHasher _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<UserSeeder> _logger;

    public UserSeeder(IUserStore store, PasswordHasher hasher, IConfiguration config, ILogger<UserSeeder> logger)
    {
        _store = store;
        _hasher = hasher;
        _config = config;
        _logger = logger;
    }

    public void SeedIfEmpty()
    {
        var seed = _config.GetSection("Auth:Seed").Get<Dictionary<string, string>>();
        var password = _config["Auth:SeedPassword"];

        if (seed is null || seed.Count == 0)
        {
            _logger.LogWarning("No se declararon usuarios de semilla en 'Auth:Seed'. Sin usuarios, nadie podrá iniciar sesión.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogError("'Auth:SeedPassword' no configurado; no se puede sembrar usuarios sin contraseña.");
            return;
        }

        var existingUsers = _store.All()
            .Select(user => user.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (username, role) in seed)
        {
            if (existingUsers.Contains(username))
                continue;

            _store.Upsert(new UserAccount(username, _hasher.Hash(password), role, username));
            _logger.LogInformation("Usuario sembrado: {User} (rol {Role})", username, role);
        }
    }
}
