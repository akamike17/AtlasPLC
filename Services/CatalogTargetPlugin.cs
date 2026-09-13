using System.Net.Sockets;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Plugin descriptivo para targets que aún requieren toolchain del proveedor o un
/// adaptador adicional. Mantiene su estado honesto sin hacer que el registry conozca
/// vendors: cada target es una contribución DI independiente.
/// </summary>
public sealed class CatalogTargetPlugin(TargetDescriptor descriptor, ITargetConfigurationProvider configuration, bool probeNetwork) : ITargetPlugin
{
    public TargetDescriptor Descriptor { get; } = descriptor;
    public IReadOnlyList<TargetActionDescriptor> Actions { get; } = descriptor.DeploymentMode == DeploymentMode.ExportOnly
        ? new[] { new TargetActionDescriptor("validate", "Validar", "Ejecuta el gate de validación."), new TargetActionDescriptor("generate", "Generar", "Genera el artefacto compatible."), new TargetActionDescriptor("export", "Exportar", "Descarga el artefacto para el toolchain del proveedor.") }
        : new[] { new TargetActionDescriptor("connect", "Conectar", "Verifica la disponibilidad del servicio.") };
    public IReadOnlyList<TargetConfigurationField> ConfigurationSchema { get; } = probeNetwork
        ? new[] { new TargetConfigurationField("endpoint", "Endpoint / host", "text", true, "127.0.0.1"), new TargetConfigurationField("port", "Puerto", "number", true), new TargetConfigurationField("timeoutMs", "Timeout ms", "number", true, "1500") }
        : Array.Empty<TargetConfigurationField>();
    public ITargetStatusProvider StatusProvider { get; } = new CatalogStatusProvider(descriptor, configuration, probeNetwork);

    private sealed class CatalogStatusProvider(TargetDescriptor descriptor, ITargetConfigurationProvider configuration, bool probeNetwork) : ITargetStatusProvider
    {
        public async Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default)
        {
            if (!probeNetwork)
                return new TargetRuntimeStatus(descriptor.ImplementationState.ToString(), descriptor.Description);
            var effective = configuration.Get(instance.Id);
            if (effective is null || effective.Port == 0)
                return new TargetRuntimeStatus(descriptor.ImplementationState.ToString(), "Requiere configuración de endpoint, puerto y timeout.");
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
}

public static class TargetPluginCatalog
{
    public static void AddBuiltIns(IServiceCollection services)
    {
        Add(services, new TargetDescriptor { Id = "iec-st", DisplayName = "Generic IEC Structured Text", Category = TargetCategory.EngineeringExport, Description = "Exportación IEC Structured Text.", Capabilities = new TargetCapabilities(new[] { TargetCapability.GenerateSource, TargetCapability.ExportProject }), SupportedArtifactKinds = new[] { "StructuredText" }, ImplementationState = TargetImplementationState.Ready, DeploymentMode = DeploymentMode.ExportOnly });
        Add(services, new TargetDescriptor { Id = "plcopen-xml", DisplayName = "Generic PLCopen XML", Category = TargetCategory.EngineeringExport, Description = "Intercambio PLCopen XML.", Capabilities = new TargetCapabilities(new[] { TargetCapability.GenerateProject, TargetCapability.ExportProject }), SupportedArtifactKinds = new[] { "PlcOpenXml" }, ImplementationState = TargetImplementationState.Partial, DeploymentMode = DeploymentMode.ExportOnly });
        Add(services, new TargetDescriptor { Id = "codesys", DisplayName = "CODESYS", Manufacturer = "CODESYS", Category = TargetCategory.PlcRuntime, Description = "Runtime y entorno CODESYS; requiere toolchain del proveedor.", ImplementationState = TargetImplementationState.NotImplemented, DeploymentMode = DeploymentMode.Assisted });
        Add(services, new TargetDescriptor { Id = "siemens-s7", DisplayName = "Siemens S7-1200 / S7-1500", Manufacturer = "Siemens", Category = TargetCategory.PhysicalPlc, Description = "PLC Siemens; requiere TIA Portal y adapter compatible.", ImplementationState = TargetImplementationState.NotImplemented, DeploymentMode = DeploymentMode.Assisted }, true);
        Add(services, new TargetDescriptor { Id = "rockwell-logix", DisplayName = "Rockwell Logix", Manufacturer = "Rockwell", Category = TargetCategory.PhysicalPlc, Description = "Canal EtherNet/IP CIP asistido; no suplanta Studio 5000.", Capabilities = new TargetCapabilities(new[] { TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover }), ImplementationState = TargetImplementationState.Assisted, DeploymentMode = DeploymentMode.Assisted }, true);
        Add(services, new TargetDescriptor { Id = "mitsubishi-melsec", DisplayName = "Mitsubishi MELSEC", Manufacturer = "Mitsubishi", Category = TargetCategory.PhysicalPlc, Description = "Canal SLMP/MC 3E asistido; requiere adapter compatible.", Capabilities = new TargetCapabilities(new[] { TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover }), ImplementationState = TargetImplementationState.Assisted, DeploymentMode = DeploymentMode.Assisted }, true);
        Add(services, new TargetDescriptor { Id = "omron-sysmac", DisplayName = "Omron Sysmac", Manufacturer = "Omron", Category = TargetCategory.PhysicalPlc, Description = "Canal FINS/UDP asistido; requiere adapter compatible.", Capabilities = new TargetCapabilities(new[] { TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover }), ImplementationState = TargetImplementationState.Assisted, DeploymentMode = DeploymentMode.Assisted }, true);
        Add(services, new TargetDescriptor { Id = "beckhoff-twincat", DisplayName = "Beckhoff TwinCAT", Manufacturer = "Beckhoff", Category = TargetCategory.PlcRuntime, Description = "Canal ADS asistido; requiere router TwinCAT.", Capabilities = new TargetCapabilities(new[] { TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover }), ImplementationState = TargetImplementationState.Assisted, DeploymentMode = DeploymentMode.Assisted }, true);
        Add(services, new TargetDescriptor { Id = "external-simulator", DisplayName = "External Simulator", Category = TargetCategory.ExternalSimulation, Description = "Simulador externo configurable; sin adapter instalado no se declara integración.", ImplementationState = TargetImplementationState.NotImplemented });
    }

    private static void Add(IServiceCollection services, TargetDescriptor descriptor, bool probeNetwork = false)
        => services.AddSingleton<ITargetPlugin>(sp => new CatalogTargetPlugin(descriptor, sp.GetRequiredService<ITargetConfigurationProvider>(), probeNetwork));
}
