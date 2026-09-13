namespace AtlasSoftPlc.Targets;

/// <summary>
/// Identidad canónica de un target (spec §13). Nunca se genera un proyecto
/// para "marca genérica" sin conocer familia/modelo/firmware.
/// </summary>
public sealed class TargetIdentity
{
    public string Manufacturer { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? FirmwareRange { get; init; }
    public string? OrderNumber { get; init; }

    public string DisplayName =>
        string.Join(" / ", new[] { Manufacturer, Family, Model }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool IsFullySpecified =>
        !string.IsNullOrWhiteSpace(Manufacturer) &&
        !string.IsNullOrWhiteSpace(Family) &&
        !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// Nivel de soporte por target (spec §12). Un modelo puede tener distinto nivel
/// que otro de la misma marca.
/// </summary>
public enum TargetSupportLevel
{
    L0_Unsupported = 0,
    L1_Monitor = 1,
    L2_ImportExportSource = 2,
    L3_GenerateCompatibleArtifact = 3,
    L4_BuildThroughVendorTool = 4,
    L5_DirectDeployment = 5,
    L6_OnlineVerify = 6,
}

/// <summary>
/// Perfil de target versionado y actualizable (spec §13). Describe capacidades,
/// límites y el toolchain de ingeniería requerido. La base de perfiles es un
/// catálogo separado; aquí se define la forma del perfil.
/// </summary>
public sealed class TargetProfile
{
    public TargetIdentity Identity { get; init; } = new();
    public TargetCapabilities Capabilities { get; init; } = TargetCapabilities.None;
    public TargetSupportLevel SupportLevel { get; init; } = TargetSupportLevel.L0_Unsupported;
    public string EngineeringTool { get; init; } = string.Empty;
    public string[] ExportFormats { get; init; } = Array.Empty<string>();
    public int ProfileVersion { get; init; } = 1;
    public TargetConnectionProfile? DefaultConnection { get; init; }

    /// <summary>Determina el nivel de soporte a partir de las capacidades declaradas.</summary>
    public static TargetSupportLevel InferLevel(TargetCapabilities caps)
    {
        if (caps.Supports(TargetCapability.VerifyDeployment)) return TargetSupportLevel.L6_OnlineVerify;
        if (caps.Supports(TargetCapability.DeployProgram) || caps.Supports(TargetCapability.DeployHardware)) return TargetSupportLevel.L5_DirectDeployment;
        if (caps.Supports(TargetCapability.Compile)) return TargetSupportLevel.L4_BuildThroughVendorTool;
        if (caps.Supports(TargetCapability.GenerateSource) || caps.Supports(TargetCapability.GenerateProject)) return TargetSupportLevel.L3_GenerateCompatibleArtifact;
        if (caps.Supports(TargetCapability.ExportProject)) return TargetSupportLevel.L2_ImportExportSource;
        if (caps.Supports(TargetCapability.ReadLiveData)) return TargetSupportLevel.L1_Monitor;
        return TargetSupportLevel.L0_Unsupported;
    }
}
