using AtlasSoftPlc.Targets;
using System.Net.Sockets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Bridge explícito entre un adapter operativo y el contrato de plugin.</summary>
public sealed class AdapterTargetPlugin(IPlcTargetAdapter adapter, ITargetConfigurationProvider configuration, string targetId, bool probeNetwork = false) : ITargetPlugin, ITargetAdapterProvider
{
    public TargetDescriptor Descriptor { get; } = new()
    {
        Id = targetId,
        DisplayName = targetId.Equals("atlas-simulation", StringComparison.OrdinalIgnoreCase) ? "Atlas Simulation Runtime" : "Modbus Online I/O",
        Manufacturer = adapter.Identity.Manufacturer,
        Family = adapter.Identity.Family,
        Model = adapter.Identity.Model,
        Category = probeNetwork ? TargetCategory.OnlineIo : TargetCategory.InternalSimulation,
        Description = "Plugin operativo compuesto desde un adapter registrado.",
        Capabilities = adapter.Capabilities,
        ImplementationState = probeNetwork ? TargetImplementationState.Partial : TargetImplementationState.Ready,
        DeploymentMode = DeploymentMode.MonitorOnly
    };

    public ITargetStatusProvider StatusProvider { get; } = new AdapterStatusProvider(adapter, configuration, probeNetwork);
    public IPlcTargetAdapter Adapter => adapter;
    public IReadOnlyList<TargetActionDescriptor> Actions { get; } = adapter.Capabilities.Supports(TargetCapability.Simulate)
        ? new[] { new TargetActionDescriptor("simulate", "Simular", "Instala el programa en el runtime local.") { RequiredCapabilities = new[] { TargetCapability.Simulate } } }
        : new[] { new TargetActionDescriptor("connect", "Conectar", "Verifica la conexión de I/O online.") { RequiredCapabilities = new[] { TargetCapability.ReadLiveData }, RequiredState = "Detected" }, new TargetActionDescriptor("monitor", "Monitorear", "Lee el estado online sin desplegar programas.") { RequiredCapabilities = new[] { TargetCapability.ReadLiveData }, RequiredState = "Detected" } };
    public ITargetActionProvider ActionProvider { get; } = new AdapterActionProvider(adapter, configuration, probeNetwork);
    public IReadOnlyList<TargetConfigurationField> ConfigurationSchema { get; } = probeNetwork
        ? new[] { new TargetConfigurationField("endpoint", "Endpoint / host", "text", true, "127.0.0.1"), new TargetConfigurationField("port", "Puerto", "number", true), new TargetConfigurationField("timeoutMs", "Timeout ms", "number", true, "1500") }
        : Array.Empty<TargetConfigurationField>();

    private sealed class AdapterStatusProvider(IPlcTargetAdapter adapter, ITargetConfigurationProvider configuration, bool probeNetwork) : ITargetStatusProvider
    {
        public async Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default)
        {
            if (!probeNetwork)
                return new TargetRuntimeStatus("Ready", $"Adapter {adapter.Identity.Manufacturer}/{adapter.Identity.Family}/{adapter.Identity.Model} cargado por DI.");
            var effective = configuration.Get(instance.Id);
            if (effective is null || effective.Port == 0)
                return new TargetRuntimeStatus("Partial", "Requiere configuración de endpoint, puerto y timeout.");
            if (string.IsNullOrWhiteSpace(effective.Endpoint))
                return new TargetRuntimeStatus("NotConfigured", "Endpoint vacío; configura el host del target.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Math.Clamp(effective.TimeoutMs, 100, 60000));
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(effective.Endpoint, effective.Port, timeout.Token);
                return new TargetRuntimeStatus("Detected", $"Servicio accesible en {effective.Endpoint}:{effective.Port}; sólo prueba de disponibilidad.");
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new TargetRuntimeStatus("NotConfigured", $"Servicio no accesible en {effective.Endpoint}:{effective.Port} dentro del timeout configurado.");
            }
        }
    }

    private sealed class AdapterActionProvider(IPlcTargetAdapter adapter, ITargetConfigurationProvider configuration, bool probeNetwork) : ITargetActionProvider
    {
        public async Task<TargetActionResult> ExecuteAsync(TargetInstance instance, string actionId, TargetActionRequest request, CancellationToken ct = default)
        {
            if (actionId.Equals("simulate", StringComparison.OrdinalIgnoreCase))
                return new TargetActionResult(false, "La simulación requiere que el programa se entregue por el flujo de simulación del Workbench.");
            if (!actionId.Equals("connect", StringComparison.OrdinalIgnoreCase) && !actionId.Equals("monitor", StringComparison.OrdinalIgnoreCase))
                return new TargetActionResult(false, $"Acción no soportada: {actionId}.");

            var status = await new AdapterStatusProvider(adapter, configuration, probeNetwork).GetStatusAsync(instance, ct);
            var success = status.State is "Detected" or "Ready";
            return new TargetActionResult(success, status.Detail ?? status.State, new Dictionary<string, string> { ["state"] = status.State });
        }
    }
}
