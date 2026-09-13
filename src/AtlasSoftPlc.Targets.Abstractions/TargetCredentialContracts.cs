namespace AtlasSoftPlc.Targets;

/// <summary>
/// Credenciales efímeras entregadas por el host. Nunca forman parte de la
/// configuración persistida de una instancia ni de un resultado de diagnóstico.
/// </summary>
public sealed record TargetCredentials(string Username, string Password);

public interface ITargetCredentialProvider
{
    TargetCredentials? Get(TargetInstance instance);
}
