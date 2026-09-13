using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

public sealed class OpenPlcStatusProvider(IOpenPlcRuntimeClient client) : ITargetStatusProvider
{
    public async Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default)
    {
        var result = await client.StatusAsync(instance, ct).ConfigureAwait(false);
        return new TargetRuntimeStatus(result.State, result.Message);
    }
}

public sealed class OpenPlcTargetPlugin : ITargetPlugin
{
    private readonly IOpenPlcRuntimeClient _client;

    public OpenPlcTargetPlugin(IOpenPlcRuntimeClient client)
    {
        _client = client;
        StatusProvider = new OpenPlcStatusProvider(client);
    }

    public TargetDescriptor Descriptor { get; } = new()
    {
        Id = "openplc",
        DisplayName = "OpenPLC Runtime",
        Manufacturer = "OpenPLC",
        Family = "HTTP Runtime API",
        Model = "v4+",
        Category = TargetCategory.PlcRuntime,
        Description = "Conexión HTTP verificable al runtime; despliegue asistido pendiente de API de carga validada.",
        Capabilities = new TargetCapabilities(new[] { TargetCapability.ReadLiveData, TargetCapability.ReadDiagnostics, TargetCapability.StartController, TargetCapability.StopController }),
        ImplementationState = TargetImplementationState.Assisted,
        DeploymentMode = DeploymentMode.Assisted,
        DocumentationHint = "Configura HTTPS (127.0.0.1:8443 por defecto), timeout y allowSelfSigned sólo por instancia; usa CredentialReference para el proveedor externo."
    };

    public ITargetStatusProvider StatusProvider { get; }

    public IReadOnlyList<TargetConfigurationField> ConfigurationSchema { get; } = new[]
    {
        new TargetConfigurationField("endpoint", "Endpoint / host", "text", true, "127.0.0.1"),
        new TargetConfigurationField("port", "Puerto HTTPS", "number", true, "8443"),
        new TargetConfigurationField("baseUrl", "Base URL (opcional)", "text"),
        new TargetConfigurationField("timeoutMs", "Timeout ms", "number", true, "3000"),
        new TargetConfigurationField("allowSelfSigned", "Aceptar certificado autofirmado", "checkbox", false, "false")
    };

    public IReadOnlyList<TargetActionDescriptor> Actions { get; } = new[]
    {
        new TargetActionDescriptor("connect", "Verificar API", "Consulta version, capabilities y status por HTTPS.") { RequiredCapabilities = new[] { TargetCapability.ReadDiagnostics }, RequiredState = "Any" },
        new TargetActionDescriptor("login", "Autenticar", "Inicia sesión usando CredentialReference; no guarda el token.") { RequiredState = "Any" },
        new TargetActionDescriptor("status", "Consultar estado", "Consulta /api/status con Bearer si hay sesión.") { RequiredCapabilities = new[] { TargetCapability.ReadDiagnostics }, RequiredState = "Any" },
        new TargetActionDescriptor("logs", "Consultar logs", "Consulta /api/runtime-logs con Bearer si hay sesión.") { RequiredCapabilities = new[] { TargetCapability.ReadDiagnostics }, RequiredState = "Any" },
        new TargetActionDescriptor("start", "Arrancar runtime", "Solicita /api/start-plc al runtime OpenPLC.", true) { RequiredCapabilities = new[] { TargetCapability.StartController }, RequiredState = "Connected" },
        new TargetActionDescriptor("stop", "Detener runtime", "Solicita /api/stop-plc al runtime OpenPLC.", true) { RequiredCapabilities = new[] { TargetCapability.StopController }, RequiredState = "Connected" }
    };

    public ITargetActionProvider ActionProvider => new ActionProviderImpl(_client);

    private sealed class ActionProviderImpl(IOpenPlcRuntimeClient client) : ITargetActionProvider
    {
        public async Task<TargetActionResult> ExecuteAsync(TargetInstance instance, string actionId, TargetActionRequest request, CancellationToken ct = default)
        {
            var result = actionId.ToLowerInvariant() switch
            {
                "connect" => await client.ProbeAsync(instance, ct).ConfigureAwait(false),
                "login" => await client.LoginAsync(instance, ct).ConfigureAwait(false),
                "status" => await client.StatusAsync(instance, ct).ConfigureAwait(false),
                "logs" => await client.RuntimeLogsAsync(instance, ct).ConfigureAwait(false),
                "start" => await client.StartAsync(instance, ct).ConfigureAwait(false),
                "stop" => await client.StopAsync(instance, ct).ConfigureAwait(false),
                _ => new OpenPlcProbeResult(false, "Unsupported", $"Acción OpenPLC no soportada: {actionId}.")
            };
            return new TargetActionResult(result.Succeeded, result.Message, result.Data);
        }
    }
}
