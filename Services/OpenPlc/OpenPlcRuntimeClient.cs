using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Cliente HTTP del runtime OpenPLC. La sesión JWT es por instancia y sólo
/// existe en memoria; la credencial llega de un proveedor externo.
/// </summary>
public sealed class OpenPlcRuntimeClient : IOpenPlcRuntimeClient
{
    private static readonly string[] ProbeEndpoints = { "/api/version", "/api/capabilities", "/api/status" };
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly IOpenPlcSessionStore _sessions;
    private readonly ITargetCredentialProvider _credentials;

    public OpenPlcRuntimeClient(
        Func<HttpMessageHandler>? handlerFactory = null,
        IOpenPlcSessionStore? sessions = null,
        ITargetCredentialProvider? credentials = null)
    {
        _handlerFactory = handlerFactory ?? (() => new HttpClientHandler());
        _sessions = sessions ?? new OpenPlcSessionStore();
        _credentials = credentials ?? new EnvironmentTargetCredentialProvider();
    }

    public async Task<OpenPlcProbeResult> ProbeAsync(TargetInstance instance, CancellationToken ct = default)
    {
        if (!TryBuildBaseUri(instance, out var baseUri, out var error))
            return new(false, "NotConfigured", error!);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in ProbeEndpoints)
        {
            var response = await SendAsync(instance, baseUri!, HttpMethod.Get, endpoint, null, ct).ConfigureAwait(false);
            if (!response.Result.Succeeded) return response.Result;
            data[endpoint] = response.Result.Message;
        }
        return new(true, "Connected", $"API OpenPLC verificada en {baseUri}.", data);
    }

    public Task<OpenPlcProbeResult> StatusAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/status", HttpMethod.Get, ct);

    public Task<OpenPlcProbeResult> RuntimeLogsAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/runtime-logs", HttpMethod.Get, ct);

    public Task<OpenPlcProbeResult> StartAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/start-plc", HttpMethod.Get, ct);

    public Task<OpenPlcProbeResult> StopAsync(TargetInstance instance, CancellationToken ct = default) =>
        SendActionAsync(instance, "/api/stop-plc", HttpMethod.Get, ct);

    public async Task<OpenPlcProbeResult> LoginAsync(TargetInstance instance, CancellationToken ct = default)
    {
        if (!TryBuildBaseUri(instance, out var baseUri, out var error))
            return new(false, "NotConfigured", error!);

        var credentials = _credentials.Get(instance);
        if (credentials is null)
            return new(false, "LOGIN_FAILED", "No hay credenciales para la referencia configurada. Define el proveedor externo sin guardar la contraseña en el proyecto.");

        var json = JsonSerializer.Serialize(new { username = credentials.Username, password = credentials.Password });
        var response = await SendAsync(instance, baseUri!, HttpMethod.Post, "/api/login", json, ct, includeAuthorization: false, includeBodyInMessage: false).ConfigureAwait(false);
        if (!response.Result.Succeeded)
            return response.Result.State == "Unauthorized"
                ? new(false, "LOGIN_FAILED", "OpenPLC rechazó las credenciales.")
                : response.Result;

        var accessToken = ExtractAccessToken(response.Body);
        if (string.IsNullOrWhiteSpace(accessToken))
            return new(false, "LOGIN_FAILED", "OpenPLC respondió correctamente pero no entregó access_token ni token.");

        _sessions.Set(instance.Id, accessToken);
        return new(true, "Authenticated", "Autenticación OpenPLC confirmada; la sesión quedó sólo en memoria.");
    }

    private async Task<OpenPlcProbeResult> SendActionAsync(TargetInstance instance, string endpoint, HttpMethod method, CancellationToken ct)
    {
        if (!TryBuildBaseUri(instance, out var baseUri, out var error))
            return new(false, "NotConfigured", error!);
        var response = await SendAsync(instance, baseUri!, method, endpoint, null, ct).ConfigureAwait(false);
        return response.Result;
    }

    private async Task<(OpenPlcProbeResult Result, string Body)> SendAsync(
        TargetInstance instance,
        Uri baseUri,
        HttpMethod method,
        string endpoint,
        string? body,
        CancellationToken ct,
        bool includeAuthorization = true,
        bool includeBodyInMessage = true)
    {
        var configuration = OpenPlcConfiguration.From(instance);
        try
        {
            using var handler = _handlerFactory();
            if (configuration.AllowSelfSigned && handler is HttpClientHandler httpHandler)
                httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var client = new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromMilliseconds(configuration.TimeoutMs)
            };
            using var request = new HttpRequestMessage(method, endpoint.TrimStart('/'));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (includeAuthorization && _sessions.TryGet(instance.Id, out var session))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && includeAuthorization)
                    _sessions.Remove(instance.Id);
                var state = response.StatusCode == HttpStatusCode.Unauthorized ? "Unauthorized" : "Unavailable";
                var message = includeBodyInMessage
                    ? $"OpenPLC respondió {(int)response.StatusCode} en {endpoint}: {Trim(text)}"
                    : $"OpenPLC respondió {(int)response.StatusCode} en {endpoint}.";
                return (new(false, state, message), text);
            }

            var successMessage = includeBodyInMessage
                ? $"{endpoint} respondió {(int)response.StatusCode}: {Trim(text)}"
                : $"{endpoint} respondió {(int)response.StatusCode}.";
            return (new(true, "Connected", successMessage), text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (new(false, "Timeout", $"Timeout consultando OpenPLC en {endpoint}."), string.Empty);
        }
        catch (HttpRequestException ex)
        {
            return (new(false, "Unavailable", $"OpenPLC no accesible en {endpoint}: {ex.Message}"), string.Empty);
        }
    }

    private static bool TryBuildBaseUri(TargetInstance instance, out Uri? uri, out string? error)
    {
        var configuration = OpenPlcConfiguration.From(instance);
        return configuration.TryGetBaseUri(out uri, out error);
    }

    private static string? ExtractAccessToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var propertyName in new[] { "access_token", "token" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }
        catch (JsonException)
        {
            // El contrato exige JSON con token; la respuesta se convierte en LOGIN_FAILED.
        }
        return null;
    }

    private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? "sin cuerpo" : value.Length > 240 ? value[..240] : value;
}
