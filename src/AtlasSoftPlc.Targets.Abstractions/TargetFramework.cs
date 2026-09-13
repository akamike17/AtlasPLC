namespace AtlasSoftPlc.Targets;

public enum TargetCategory { InternalSimulation, ExternalSimulation, PlcRuntime, PhysicalPlc, EngineeringExport, OnlineIo, DiagnosticOnly, Gateway, FutureExtension }
public enum TargetImplementationState { Ready, Partial, Assisted, NotConfigured, NotImplemented, Unsupported }
public enum DeploymentMode { Automatic, Assisted, ExportOnly, MonitorOnly, Unsupported }

/// <summary>Metadatos declarativos de un plugin. No contiene conocimiento de red o vendor.</summary>
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
