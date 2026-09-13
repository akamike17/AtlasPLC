using System.Net.Sockets;

namespace AtlasSoftPlc.Targets;

public enum TargetCategory { InternalSimulation, ExternalSimulation, PlcRuntime, PhysicalPlc, EngineeringExport, OnlineIo, DiagnosticOnly, Gateway, FutureExtension }
public enum TargetImplementationState { Ready, Partial, Assisted, NotConfigured, NotImplemented, Unsupported }
public enum DeploymentMode { Automatic, Assisted, ExportOnly, MonitorOnly, Unsupported }

public sealed record TargetDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Manufacturer { get; init; } = "Atlas";
    public string Family { get; init; } = "";
    public string Model { get; init; } = "";
    public required TargetCategory Category { get; init; }
    public required string Description { get; init; }
    public TargetCapabilities Capabilities { get; init; } = TargetCapabilities.None;
    public IReadOnlyList<string> SupportedArtifactKinds { get; init; } = Array.Empty<string>();
    public TargetImplementationState ImplementationState { get; init; } = TargetImplementationState.NotImplemented;
    public DeploymentMode DeploymentMode { get; init; } = DeploymentMode.Unsupported;
    public string DocumentationHint { get; init; } = "";
}

public sealed record TargetRuntimeStatus(string State, string? Detail = null);

public interface ITargetRegistry
{
    IReadOnlyList<TargetDescriptor> GetAll();
    TargetDescriptor? Get(string id);
    Task<TargetRuntimeStatus> GetStatusAsync(string id, CancellationToken ct = default);
}

public sealed class TargetRegistry : ITargetRegistry
{
    private readonly IReadOnlyList<TargetDescriptor> _targets = CreateTargets();
    public IReadOnlyList<TargetDescriptor> GetAll() => _targets;
    public TargetDescriptor? Get(string id) => _targets.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    public async Task<TargetRuntimeStatus> GetStatusAsync(string id, CancellationToken ct = default)
    {
        if (Get(id) is null)
            return new TargetRuntimeStatus("Unsupported", "Target no registrado.");

        // Probe seguro: sólo verifica que el runtime local acepte TCP. No autentica,
        // no sube proyectos y no declara que el adapter de despliegue esté listo.
        if (string.Equals(id, "openplc", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 8443, ct);
                return new TargetRuntimeStatus("Detected", "OpenPLC Runtime detectado en 127.0.0.1:8443; API HTTPS requiere autenticación.");
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new TargetRuntimeStatus("NotConfigured", "OpenPLC Runtime no accesible en 127.0.0.1:8443.");
            }
        }

        if (string.Equals(id, "siemens-s7", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 1102, ct);
                return new TargetRuntimeStatus("Detected", "Servidor S7 de laboratorio detectado en 127.0.0.1:1102.");
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new TargetRuntimeStatus("NotConfigured", "Servidor S7 no accesible en 127.0.0.1:1102.");
            }
        }

        if (string.Equals(id, "rockwell-logix", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 44818, ct);
                return new TargetRuntimeStatus("Detected", "Servidor EtherNet/IP CIP de laboratorio detectado en 127.0.0.1:44818.");
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new TargetRuntimeStatus("NotConfigured", "Servidor EtherNet/IP no accesible en 127.0.0.1:44818.");
            }
        }

        if (string.Equals(id, "beckhoff-twincat", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 48898, ct);
                return new TargetRuntimeStatus("Detected", "Router ADS de laboratorio detectado en 127.0.0.1:48898.");
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new TargetRuntimeStatus("NotConfigured", "Router ADS no accesible en 127.0.0.1:48898.");
            }
        }

        if (string.Equals(id, "omron-sysmac", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetRuntimeStatus("Assisted", "Emulador FINS/UDP reproducible en 127.0.0.1:9601; requiere iniciar el perfil de laboratorio.");
        }

        if (string.Equals(id, "mitsubishi-melsec", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 2000, ct);
                return new TargetRuntimeStatus("Detected", "Servidor SLMP/MC 3E de laboratorio detectado en 127.0.0.1:2000.");
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new TargetRuntimeStatus("NotConfigured", "Servidor SLMP/MC no accesible en 127.0.0.1:2000.");
            }
        }

        return new TargetRuntimeStatus("NotConfigured", "Requiere configuración.");
    }

    private static IReadOnlyList<TargetDescriptor> CreateTargets() => new[]
    {
        new TargetDescriptor { Id="atlas-simulation", DisplayName="Atlas Simulation Runtime", Category=TargetCategory.InternalSimulation, Description="Simulación determinista integrada de Atlas.", Capabilities=new TargetCapabilities(new[]{TargetCapability.Simulate}), SupportedArtifactKinds=new[]{"AtlasPackage"}, ImplementationState=TargetImplementationState.Ready, DeploymentMode=DeploymentMode.MonitorOnly },
        new TargetDescriptor { Id="iec-st", DisplayName="Generic IEC Structured Text", Category=TargetCategory.EngineeringExport, Description="Exportación de código IEC Structured Text.", Capabilities=new TargetCapabilities(new[]{TargetCapability.GenerateSource, TargetCapability.ExportProject}), SupportedArtifactKinds=new[]{"StructuredText"}, ImplementationState=TargetImplementationState.Ready, DeploymentMode=DeploymentMode.ExportOnly },
        new TargetDescriptor { Id="plcopen-xml", DisplayName="Generic PLCopen XML", Category=TargetCategory.EngineeringExport, Description="Intercambio de proyectos mediante PLCopen XML.", Capabilities=new TargetCapabilities(new[]{TargetCapability.GenerateProject, TargetCapability.ExportProject}), SupportedArtifactKinds=new[]{"PlcOpenXml"}, ImplementationState=TargetImplementationState.Partial, DeploymentMode=DeploymentMode.ExportOnly },
        new TargetDescriptor { Id="openplc", DisplayName="OpenPLC Runtime", Manufacturer="OpenPLC", Family="Runtime", Category=TargetCategory.PlcRuntime, Description="Runtime IEC externo; la conexión se habilita sólo cuando el probe real esté configurado.", Capabilities=TargetCapabilities.None, ImplementationState=TargetImplementationState.NotConfigured, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="codesys", DisplayName="CODESYS", Manufacturer="CODESYS", Category=TargetCategory.PlcRuntime, Description="Runtime y entorno de ingeniería CODESYS.", ImplementationState=TargetImplementationState.NotImplemented },
        new TargetDescriptor { Id="siemens-s7", DisplayName="Siemens S7-1200 / S7-1500", Manufacturer="Siemens", Category=TargetCategory.PhysicalPlc, Description="PLC físico Siemens; requiere TIA Portal y adapter compatible.", ImplementationState=TargetImplementationState.NotImplemented, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="rockwell-logix", DisplayName="Rockwell Logix", Manufacturer="Rockwell", Category=TargetCategory.PhysicalPlc, Description="Canal EtherNet/IP CIP online de laboratorio; el downloader Studio 5000 no se suplanta.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="mitsubishi-melsec", DisplayName="Mitsubishi MELSEC", Manufacturer="Mitsubishi", Category=TargetCategory.PhysicalPlc, Description="Canal SLMP/MC 3E online de laboratorio; el proyecto GX Works se mantiene como exportación asistida.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="omron-sysmac", DisplayName="Omron Sysmac", Manufacturer="Omron", Category=TargetCategory.PhysicalPlc, Description="Canal FINS/UDP online de laboratorio; el proyecto Sysmac Studio se mantiene como exportación asistida.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="beckhoff-twincat", DisplayName="Beckhoff TwinCAT", Manufacturer="Beckhoff", Category=TargetCategory.PlcRuntime, Description="Canal ADS online de laboratorio; el proyecto TwinCAT se mantiene como exportación asistida.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.Discover}), ImplementationState=TargetImplementationState.Assisted, DeploymentMode=DeploymentMode.Assisted },
        new TargetDescriptor { Id="modbus-online", DisplayName="Modbus Online I/O", Category=TargetCategory.OnlineIo, Description="Lectura y escritura de coils y registros online; no es compilador ni downloader.", Capabilities=new TargetCapabilities(new[]{TargetCapability.ReadLiveData, TargetCapability.WriteLiveData, TargetCapability.ReadDiagnostics}), ImplementationState=TargetImplementationState.Partial, DeploymentMode=DeploymentMode.MonitorOnly },
        new TargetDescriptor { Id="external-simulator", DisplayName="External Simulator", Category=TargetCategory.ExternalSimulation, Description="Simulador externo configurable; sin adapter instalado no se declara integración.", ImplementationState=TargetImplementationState.NotImplemented }
    };
}
