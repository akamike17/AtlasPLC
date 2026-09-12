namespace AtlasSoftPlc.Targets;

/// <summary>
/// Capacidad individual de un target PLC (spec §12). Cada capacidad se declara
/// explícitamente; un adapter NUNCA asume una capacidad que no declara.
/// </summary>
public enum TargetCapability
{
    // ── Discovery / identidad ──
    Discover,

    // ── Online data / monitor ──
    ReadLiveData,
    WriteLiveData,
    ReadSymbols,
    ReadDiagnostics,

    // ── Import/Export fuente ──
    ImportProject,
    ExportProject,
    UploadSource,
    RecoverComments,
    RecoverHardwareConfig,
    RecoverFullProject,

    // ── Generación / compilación ──
    GenerateSource,
    GenerateProject,
    Compile,

    // ── Simulación ──
    Simulate,

    // ── Despliegue ──
    DeployProgram,
    DeployHardware,
    StartController,
    StopController,
    VerifyDeployment,
    Force,
    Rollback,
}

/// <summary>
/// Conjunto inmutable de capacidades declaradas por un target (spec §12/§27).
/// La UI es capability-driven: nunca muestra una acción que el target no soporta.
/// </summary>
public sealed class TargetCapabilities
{
    private readonly IReadOnlySet<TargetCapability> _capabilities;

    public TargetCapabilities(IEnumerable<TargetCapability> capabilities)
    {
        _capabilities = new HashSet<TargetCapability>(capabilities ?? Array.Empty<TargetCapability>());
    }

    public static TargetCapabilities None { get; } = new(Array.Empty<TargetCapability>());

    public bool Supports(TargetCapability capability) => _capabilities.Contains(capability);

    /// <summary>True si el target no declara ninguna capacidad.</summary>
    public bool IsEmpty => _capabilities.Count == 0;

    public IReadOnlySet<TargetCapability> All => _capabilities;
}