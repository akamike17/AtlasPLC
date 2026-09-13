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
///  - declarar una capacidad NO sustituye a implementarla: una operación cuyo override
///    no existe devuelve Unsupported aunque el capability esté declarado;
///  - el despliegue jamás ocurre con un Blocker de validación pendiente;
///  - el despliegue jamás ocurre sin un <see cref="DeploymentRequest"/> con confirmación
///    verificable (atada a target + proyecto + hash);
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

    /// <summary>Instala/arranca una simulación local del proyecto (capacidad <c>Simulate</c>).</summary>
    Task<TargetOperationResult> SimulateAsync(PlcProgramDefinition project, CancellationToken ct = default);

    /// <summary>
    /// Despliega al target. Requiere un <see cref="DeploymentRequest"/> con token de
    /// confirmación verificable (atado a target + proyecto + hash) y validación previa.
    /// </summary>
    Task<TargetOperationResult> DeployAsync(PlcProgramDefinition project, DeploymentRequest request, CancellationToken ct = default);

    /// <summary>Verifica online que el programa esperado quedó cargado.</summary>
    Task<VerifyResult> VerifyAsync(PlcProgramDefinition project, CancellationToken ct = default);
}

/// <summary>
/// Solicitud de despliegue verificable (P0-4). Ata la confirmación del usuario a:
///  - la identidad del target (marca/familia/modelo);
///  - el proyecto (id + versión + hash);
///  - un nonce/expiración razonable para evitar reenvío de un token arbitrario.
/// </summary>
public sealed record DeploymentRequest
{
    /// <summary>Marca/familia/modelo a la que se debe desplegar (debe coincidir con el target).</summary>
    public required string TargetManufacturer { get; init; }

    public required string TargetFamily { get; init; }

    public required string TargetModel { get; init; }

    /// <summary>Id del proyecto que se despliega.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Versión del proyecto.</summary>
    public required int ProjectVersion { get; init; }

    /// <summary>Hash canónico del contenido a desplegar (integridad).</summary>
    public required string ProjectHash { get; init; }

    /// <summary>Token de confirmación emitido por un flujo válido de confirmación.</summary>
    public required string ConfirmationToken { get; init; }

    /// <summary>Nonce único del flujo (anti-replay).</summary>
    public Guid Nonce { get; init; } = Guid.NewGuid();

    /// <summary>Momento en que se emitió (UTC).</summary>
    public DateTimeOffset IssuedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True si la solicitud está completa (no aceptar token vacío/arbitrario).</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(TargetManufacturer) &&
        !string.IsNullOrWhiteSpace(TargetFamily) &&
        !string.IsNullOrWhiteSpace(TargetModel) &&
        ProjectId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(ProjectHash) &&
        !string.IsNullOrWhiteSpace(ConfirmationToken);
}

/// <summary>
/// Clase base con "default = unsupported". NO devuelve éxito para ninguna operación:
/// un adapter que declare un capability pero no implemente el override correspondiente
/// obtiene Unsupported, no un "éxito" vacío. Declarar capacidad ≠ implementar.
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

    /// <summary>
    /// True si el adapter declara la capacidad. La declaración NO sustituye la
    /// implementación; las operaciones de esta base son Unsupported por defecto.
    /// </summary>
    protected bool Supports(TargetCapability capability) => _capabilities.Supports(capability);

    public virtual Task<CompatibilityReport> ValidateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        // Regla: un target que no declara capacidad de generación no puede alojar un
        // proyecto de ingeniería; bloquea. (Sin capacidad de generación → L1/L2 a lo sumo.)
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

    /// <summary>Default = Unsupported. Un adapter REAL debe sobreescribir esto para generar.</summary>
    public virtual Task<TargetOperationResult> GenerateAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(TargetOperationResult.Unsupported($"Generate ({Identity.DisplayName})"));

    /// <summary>Default = Unsupported. Un adapter REAL debe sobreescribir esto para compilar.</summary>
    public virtual Task<TargetOperationResult> BuildAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(TargetOperationResult.Unsupported($"Build ({Identity.DisplayName})"));

    /// <summary>Default = Unsupported. Un adapter REAL debe sobreescribir esto para simular.</summary>
    public virtual Task<TargetOperationResult> SimulateAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(TargetOperationResult.Unsupported($"Simulate ({Identity.DisplayName})"));

    /// <summary>
    /// Default = Unsupported. Un adapter REAL debe sobreescribir esto para desplegar,
    /// validando el <see cref="DeploymentRequest"/> como fail-closed.
    /// </summary>
    public virtual Task<TargetOperationResult> DeployAsync(PlcProgramDefinition project, DeploymentRequest request, CancellationToken ct = default) =>
        Task.FromResult(TargetOperationResult.Unsupported($"Deploy ({Identity.DisplayName})"));

    public virtual Task<VerifyResult> VerifyAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(VerifyResult.Fail($"El target {Identity.DisplayName} no soporta verificación online."));
}