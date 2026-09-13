using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Cliente HTTP del runtime OpenPLC. Los endpoints son explícitos y el certificado
/// sólo se ignora cuando la instancia lo configura expresamente.
/// </summary>
public sealed class OpenPlcRuntimeClient : IOpenPlcRuntimeClient
{
    private static readonly string[] ProbeEndpoints = { "/api/version", "/api/capabilities", "/api/status" };
    private readonly Func<HttpMessageHandler> _handlerFactory;

    public OpenPlcRuntimeClient(Func<HttpMessageHandler>? handlerFactory = null) => _handlerFactory = handlerFactory ?? (() => new HttpClientHandler());

    public async Task<OpenPlcProbeResult> ProbeAsync(TargetInstance instance, CancellationToken ct = default)
    {
        if (!TryBuildBaseUri(instance, out var baseUri, out var error))
            return new(false, "NotConfigured", error!);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in ProbeEndpoints)
        {
            var response = await SendAsync(instance, baseUri!, HttpMethod.Get, endpoint, null, ct).ConfigureAwait(false);
            if (!response.Succeeded)
                return response;
            data[endpoint] = response.Message;
        }
        return new(true, "Connected", $"API OpenPLC verificada en {baseUri}.", data);
    }

    public Task<OpenPlcProbeResult> StartAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/start-plc", HttpMethod.Get, ct);

    public Task<OpenPlcProbeResult> StopAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/stop-plc", HttpMethod.Get, ct);

    public Task<OpenPlcProbeResult> RuntimeLogsAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/runtime-logs", HttpMethod.Get, ct);

    public async Task<OpenPlcProbeResult> LoginAsync(TargetInstance instance, CancellationToken ct = default)
    {
        if (!TryBuildBaseUri(instance, out var baseUri, out var error))
            return new(false, "NotConfigured", error!);
        var username = instance.Configuration.TryGetValue("username", out var configuredUser) ? configuredUser : string.Empty;
        var password = instance.Configuration.TryGetValue("password", out var configuredPassword) ? configuredPassword : string.Empty;
        var json = JsonSerializer.Serialize(new { username, password });
        return await SendAsync(instance, baseUri!, HttpMethod.Post, "/api/login", json, ct).ConfigureAwait(false);
    }

    private async Task<OpenPlcProbeResult> SendActionAsync(TargetInstance instance, string endpoint, HttpMethod method, CancellationToken ct)
    {
        if (!TryBuildBaseUri(instance, out var baseUri, out var error))
            return new(false, "NotConfigured", error!);
        return await SendAsync(instance, baseUri!, method, endpoint, method == HttpMethod.Post ? "{}" : null, ct).ConfigureAwait(false);
    }

    private async Task<OpenPlcProbeResult> SendAsync(TargetInstance instance, Uri baseUri, HttpMethod method, string endpoint, string? body, CancellationToken ct)
    {
        try
        {
            using var handler = _handlerFactory();
            if (AllowSelfSigned(instance) && handler is HttpClientHandler httpHandler)
                httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromMilliseconds(Timeout(instance)) };
            using var request = new HttpRequestMessage(method, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, response.StatusCode == HttpStatusCode.Unauthorized ? "Unauthorized" : "Unavailable", $"OpenPLC respondió {(int)response.StatusCode} en {endpoint}: {Trim(text)}");
            return new(true, "Connected", $"{endpoint} respondió {(int)response.StatusCode}: {Trim(text)}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "Timeout", $"Timeout consultando OpenPLC en {endpoint}.");
        }
        catch (HttpRequestException ex)
        {
            return new(false, "Unavailable", $"OpenPLC no accesible en {endpoint}: {ex.Message}");
        }
    }

    private static bool TryBuildBaseUri(TargetInstance instance, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;
        var config = instance.Configuration;
        var configured = Get(config, "baseUrl");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var endpoint = Get(config, "endpoint");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                error = "Configura endpoint o baseUrl para OpenPLC.";
                return false;
            }
            var scheme = Get(config, "scheme") is { Length: > 0 } s ? s : "http";
            var port = int.TryParse(Get(config, "port"), out var parsedPort) && parsedPort is > 0 and <= 65535 ? parsedPort : 8080;
            configured = endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : $"{scheme}://{endpoint}:{port}";
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

    private static int Timeout(TargetInstance instance) => int.TryParse(Get(instance.Configuration, "timeoutMs"), out var value) && value is >= 100 and <= 60000 ? value : 3000;
    private static bool AllowSelfSigned(TargetInstance instance) => string.Equals(Get(instance.Configuration, "allowSelfSigned"), "true", StringComparison.OrdinalIgnoreCase);
    private static string? Get(IReadOnlyDictionary<string, string> config, string key) => config.TryGetValue(key, out var value) ? value : null;
    private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? "sin cuerpo" : value.Length > 240 ? value[..240] : value;
}
