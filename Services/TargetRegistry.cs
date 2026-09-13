using System.Net.Sockets;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Registro concreto de targets y probes locales; la capa de contratos no conoce red.</summary>
public sealed class TargetRegistry(ITargetConfigurationProvider configuration) : ITargetRegistry
{
    private readonly IReadOnlyList<TargetDescriptor> _targets = new[]
    {
        new TargetDescriptor { Id="atlas-simulation", DisplayName="Atlas Simulation Runtime", Category=TargetCategory.InternalSimulation, Description="Simulación determinista integrada de Atlas.", Capabilities=new TargetCapabilities(new[]{TargetCapability.Simulate}), SupportedArtifactKinds=new[]{"AtlasPackage"}, ImplementationState=TargetImplementationState.Ready, DeploymentMode=DeploymentMode.MonitorOnly },
        new TargetDescriptor { Id="iec-st", DisplayName="Generic IEC Structured Text", Category=TargetCategory.EngineeringExport, Description="Exportación IEC Structured Text.", Capabilities=new TargetCapabilities(new[]{TargetCapability.GenerateSource, TargetCapability.ExportProject}), SupportedArtifactKinds=new[]{"StructuredText"}, ImplementationState=TargetImplementationState.Ready, DeploymentMode=DeploymentMode.ExportOnly },
        new TargetDescriptor { Id="plcopen-xml", DisplayName="Generic PLCopen XML", Category=TargetCategory.EngineeringExport, Description="Intercambio PLCopen XML.", Capabilities=new TargetCapabilities(new[]{TargetCapability.GenerateProject, TargetCapability.ExportProject}), SupportedArtifactKinds=new[]{"PlcOpenXml"}, ImplementationState=TargetImplementationState.Partial, DeploymentMode=DeploymentMode.ExportOnly },
        new TargetDescriptor { Id="openplc", DisplayName="OpenPLC Runtime", Manufacturer="OpenPLC", Category=TargetCategory.PlcRuntime, Description="Runtime IEC externo configurado por el usuario.", ImplementationState=TargetImplementationState.NotConfigured, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="codesys", DisplayName="CODESYS", Manufacturer="CODESYS", Category=TargetCategory.PlcRuntime, Description="Runtime y entorno CODESYS.", ImplementationState=TargetImplementationState.NotImplemented },
        new TargetDescriptor { Id="siemens-s7", DisplayName="Siemens S7-1200 / S7-1500", Manufacturer="Siemens", Category=TargetCategory.PhysicalPlc, Description="PLC Siemens; requiere TIA Portal y adapter compatible.", ImplementationState=TargetImplementationState.NotImplemented, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="rockwell-logix", DisplayName="Rockwell Logix", Manufacturer="Rockwell", Category=TargetCategory.PhysicalPlc, Description="Canal EtherNet/IP CIP asistido.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="mitsubishi-melsec", DisplayName="Mitsubishi MELSEC", Manufacturer="Mitsubishi", Category=TargetCategory.PhysicalPlc, Description="Canal SLMP/MC 3E asistido.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="omron-sysmac", DisplayName="Omron Sysmac", Manufacturer="Omron", Category=TargetCategory.PhysicalPlc, Description="Canal FINS/UDP asistido.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="beckhoff-twincat", DisplayName="Beckhoff TwinCAT", Manufacturer="Beckhoff", Category=TargetCategory.PlcRuntime, Description="Canal ADS asistido.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="modbus-online", DisplayName="Modbus Online I/O", Category=TargetCategory.OnlineIo, Description="Lectura y escritura Modbus online.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.ReadDiagnostics}), ImplementationState=TargetImplementationState.Partial, DeploymentMode=DeploymentMode.MonitorOnly },
        new TargetDescriptor { Id="external-simulator", DisplayName="External Simulator", Category=TargetCategory.ExternalSimulation, Description="Simulador externo configurable.", ImplementationState=TargetImplementationState.NotImplemented }
    };
    public IReadOnlyList<TargetDescriptor> GetAll() => _targets;
    public TargetDescriptor? Get(string id) => _targets.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    public async Task<TargetRuntimeStatus> GetStatusAsync(string id, CancellationToken ct = default)
    {
        var descriptor = Get(id); if (descriptor is null) return new("Unsupported", "Target no registrado.");
        var effective = configuration.Get(id);
        var endpoint = effective?.Endpoint ?? "";
        var port = effective?.Port ?? 0;
        if (port == 0) return new(descriptor.ImplementationState.ToString(), "Requiere configuración.");
        if (string.IsNullOrWhiteSpace(endpoint)) return new("NotConfigured", "Endpoint vacío; configura el host del target.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Math.Clamp(effective?.TimeoutMs ?? 1500, 100, 60000));
        try { using var tcp = new TcpClient(); await tcp.ConnectAsync(endpoint, port, timeout.Token); return new("Detected", $"Servicio accesible en {endpoint}:{port}; sólo prueba de disponibilidad."); }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return new("NotConfigured", $"Servicio no accesible en {endpoint}:{port} dentro del timeout configurado."); }
    }
}
