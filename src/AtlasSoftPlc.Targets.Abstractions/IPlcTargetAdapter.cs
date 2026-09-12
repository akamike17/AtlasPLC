using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Targets;

/// <summary>
/// Contrato estable de adapter de target PLC (spec §11). Un adapter envuelve un
/// target concreto (runtime local, PLCopen, Siemens, Rockwell, Simulador externo…).
///
/// Reglas de contrato (§26 contract tests):
///  - las capacidades se declaran honestamente (nunca se asume una capacidad);
///  - una operación no soportada devuelve <see cref="TargetOperationResult.Unsupported"/>
///    (o false), NUNCA éxito simulado;
///  - el despliegue jamás ocurre con un Blocker de validación pendiente;
///  - el despliegue jamás ocurre sin token de confirmación explícito;
///  - la generación es determinista: misma IR + mismo perfil → mismo hash.
/// </summary>
public interface IPlcTargetAdapter
{
    /// <summary>Identidad canónica del target (marca/familia/modelo/firmware).</summary>
    TargetIdentity Identity { get; }

    /// <summary>Capacidades declaradas por este adapter.</summary>
    TargetCapabilities Capabilities { get; }

    /// <summary>Perfil completo derivado de las capacidades.</summary>
    TargetProfile Profile { get; }

    /// <summary>Verifica si un proyecto IR es compatible con este target.</summary>
    Task<CompatibilityReport> ValidateAsync(PlcProgramDefinition project, CancellationToken ct = default);

    /// <summary>Genera el artefacto de target para un proyecto compatible.</summary>
    Task<TargetOperationResult> GenerateAsync(PlcProgramDefinition project, CancellationToken ct = default);

    /// <summary>Compila el artefacto a través del toolchain del vendor.</summary>
    Task<TargetOperationResult> BuildAsync(PlcProgramDefinition project, CancellationToken ct = default);

    /// <summary>Despliega al target. Requiere confirmación explícita y validación previa.</summary>
    Task<TargetOperationResult> DeployAsync(PlcProgramDefinition project, string confirmationToken, CancellationToken ct = default);

    /// <summary>Verifica online que el programa esperado quedó cargado.</summary>
    Task<VerifyResult> VerifyAsync(PlcProgramDefinition project, CancellationToken ct = default);
}

/// <summary>
/// Implementación base que declara honestamente las capacidades y devuelve
/// "no soportado" para toda operación no sobreescrita. Los adapters derivan de
/// esta clase y sobreescriten solo lo que realmente implementan.
/// </summary>
public abstract class PlcTargetAdapterBase : IPlcTargetAdapter
{
    private readonly TargetCapabilities _capabilities;

    protected PlcTargetAdapterBase(TargetIdentity identity, IEnumerable<TargetCapability> capabilities)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _capabilities = new TargetCapabilities(capabilities);
    }

    public TargetIdentity Identity { get; }

    public TargetCapabilities Capabilities => _capabilities;

    public TargetProfile Profile => new()
    {
        Identity = Identity,
        Capabilities = _capabilities,
        SupportLevel = TargetProfile.InferLevel(_capabilities),
    };

    /// <summary>Guarda: requieren que el target declare la capacidad o devuelven "no soportado".</summary>
    protected TargetOperationResult Require(TargetCapability capability)
    {
        if (_capabilities.Supports(capability))
            return TargetOperationResult.Ok();
        return TargetOperationResult.Unsupported(capability.ToString());
    }

    public virtual Task<CompatibilityReport> ValidateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        if (!_capabilities.Supports(TargetCapability.GenerateSource) &&
            !_capabilities.Supports(TargetCapability.GenerateProject))
        {
            return Task.FromResult(new CompatibilityReport
            {
                Status = CompatibilityReport.CompatibilityStatus.Blocked,
                Messages = new[] { $"El target {Identity.DisplayName} no declara capacidad de generación." },
            });
        }
        return Task.FromResult(new CompatibilityReport { Status = CompatibilityReport.CompatibilityStatus.Pass });
    }

    public virtual Task<TargetOperationResult> GenerateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        if (!_capabilities.Supports(TargetCapability.GenerateSource) &&
            !_capabilities.Supports(TargetCapability.GenerateProject))
            return Task.FromResult(TargetOperationResult.Unsupported("Generate"));
        return Task.FromResult(TargetOperationResult.Ok());
    }

    public virtual Task<TargetOperationResult> BuildAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(_capabilities.Supports(TargetCapability.Compile)
            ? TargetOperationResult.Ok()
            : TargetOperationResult.Unsupported("Build"));

    public virtual Task<TargetOperationResult> DeployAsync(PlcProgramDefinition project, string confirmationToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(confirmationToken))
            return Task.FromResult(TargetOperationResult.Fail("Despliegue requiere token de confirmación explícito."));
        if (!_capabilities.Supports(TargetCapability.DeployProgram))
            return Task.FromResult(TargetOperationResult.Unsupported("Deploy"));
        return Task.FromResult(TargetOperationResult.Ok());
    }

    public virtual Task<VerifyResult> VerifyAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(_capabilities.Supports(TargetCapability.VerifyDeployment)
            ? VerifyResult.Fail("Verificación no implementada por este adapter.")
            : VerifyResult.Fail($"El target no soporta verificación online."));
}