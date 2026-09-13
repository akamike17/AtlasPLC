using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Valores oficiales de conexión OpenPLC para una instancia concreta.</summary>
public sealed record OpenPlcConfiguration(
    string Scheme = "https",
    string Endpoint = "127.0.0.1",
    int Port = 8443,
    int TimeoutMs = 3000,
    bool AllowSelfSigned = false,
    string? BaseUrl = null)
{
    public static OpenPlcConfiguration From(TargetInstance instance)
    {
        var config = instance.Configuration;
        var endpoint = Get(config, "endpoint") ?? "127.0.0.1";
        var scheme = Get(config, "scheme") ?? "https";
        var port = ParseInt(config, "port", 8443, 1, 65535);
        var timeout = ParseInt(config, "timeoutMs", 3000, 100, 60000);
        var allowSelfSigned = bool.TryParse(Get(config, "allowSelfSigned"), out var parsed) && parsed;
        return new OpenPlcConfiguration(scheme, endpoint, port, timeout, allowSelfSigned, Get(config, "baseUrl"));
    }

    public bool TryGetBaseUri(out Uri? uri, out string? error)
    {
        uri = null;
        error = null;
        var configured = BaseUrl;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                error = "Configura endpoint o baseUrl para OpenPLC.";
                return false;
            }
            if (!string.Equals(Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                error = "OpenPLC exige HTTPS por defecto; usa baseUrl para una excepción explícita.";
                return false;
            }
            if (Endpoint.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var explicitEndpoint) ||
                    !string.Equals(explicitEndpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    error = "OpenPLC no permite degradar a HTTP; usa una baseUrl explícita sólo si el runtime lo requiere.";
                    return false;
                }
                configured = Endpoint;
            }
            else
            {
                configured = $"https://{Endpoint}:{Port}";
            }
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out uri) || uri.Scheme is not ("http" or "https"))
        {
            error = "La baseUrl de OpenPLC debe ser una URL http/https válida.";
            uri = null;
            return false;
        }
        uri = new Uri(uri.ToString().TrimEnd('/') + "/");
        return true;
    }

    private static string? Get(IReadOnlyDictionary<string, string> config, string key) =>
        config.TryGetValue(key, out var value) ? value : null;

    private static int ParseInt(IReadOnlyDictionary<string, string> config, string key, int fallback, int min, int max) =>
        int.TryParse(Get(config, key), out var value) && value >= min && value <= max ? value : fallback;
}
