using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Proveedor de credenciales para instalaciones locales. La referencia de la
/// instancia sólo apunta a nombres de variables; los valores viven en el
/// entorno del usuario o en user-secrets y se mantienen en memoria durante la
/// llamada HTTP.
/// </summary>
public sealed class EnvironmentTargetCredentialProvider : ITargetCredentialProvider
{
    public TargetCredentials? Get(TargetInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.CredentialReference)) return null;
        var prefix = "ATLASPLC_CREDENTIAL_" + Normalize(instance.CredentialReference);
        var username = Environment.GetEnvironmentVariable(prefix + "_USERNAME");
        var password = Environment.GetEnvironmentVariable(prefix + "_PASSWORD");
        return string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)
            ? null
            : new TargetCredentials(username, password);
    }

    private static string Normalize(string value)
    {
        var chars = value.Trim().ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_');
    }
}

/// <summary>Proveedor sólo para pruebas; no se registra en el host productivo.</summary>
public sealed class InMemoryTargetCredentialProvider : ITargetCredentialProvider
{
    private readonly Dictionary<string, TargetCredentials> _credentials = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string reference, TargetCredentials credentials) => _credentials[reference] = credentials;

    public TargetCredentials? Get(TargetInstance instance) =>
        instance.CredentialReference is { Length: > 0 } reference && _credentials.TryGetValue(reference, out var credentials)
            ? credentials
            : null;
}
