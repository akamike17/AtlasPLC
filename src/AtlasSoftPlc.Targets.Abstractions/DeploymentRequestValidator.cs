using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Targets;

public sealed class DeploymentValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
    public static DeploymentValidationResult Pass() => new() { IsValid = true };
    public static DeploymentValidationResult Fail(string code, string error) => new() { ErrorCode = code, Error = error };
}

public interface IDeploymentNonceStore
{
    bool TryConsume(Guid nonce);
}

public sealed class InMemoryDeploymentNonceStore : IDeploymentNonceStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _seen = new();
    public bool TryConsume(Guid nonce) => nonce != Guid.Empty && _seen.TryAdd(nonce, 0);
}

public interface IDeploymentRequestValidator
{
    DeploymentValidationResult Validate(PlcProgramDefinition project, TargetIdentity target, DeploymentRequest request, DateTimeOffset now);
}

public sealed class DeploymentRequestValidator : IDeploymentRequestValidator
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private readonly IDeploymentNonceStore _nonces;
    private readonly TimeSpan _ttl;

    public DeploymentRequestValidator(IDeploymentNonceStore nonces, TimeSpan? ttl = null)
    {
        _nonces = nonces ?? throw new ArgumentNullException(nameof(nonces));
        _ttl = ttl ?? DefaultTtl;
    }

    public DeploymentValidationResult Validate(PlcProgramDefinition project, TargetIdentity target, DeploymentRequest request, DateTimeOffset now)
    {
        if (!string.Equals(request.TargetManufacturer, target.Manufacturer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.TargetFamily, target.Family, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.TargetModel, target.Model, StringComparison.OrdinalIgnoreCase))
            return DeploymentValidationResult.Fail("DEPLOY-TARGET-MISMATCH", "La solicitud no corresponde al target.");
        if (request.ProjectId != project.Id || request.ProjectVersion != project.Version)
            return DeploymentValidationResult.Fail("DEPLOY-PROJECT-MISMATCH", "La solicitud no corresponde al proyecto.");
        if (!string.Equals(request.ProjectHash, CanonicalProgramHasher.ComputeHash(project), StringComparison.OrdinalIgnoreCase))
            return DeploymentValidationResult.Fail("DEPLOY-HASH-MISMATCH", "El hash del proyecto no coincide.");
        if (string.IsNullOrWhiteSpace(request.ConfirmationToken))
            return DeploymentValidationResult.Fail("DEPLOY-TOKEN-MISSING", "Falta confirmación de despliegue.");
        if (request.IssuedUtc > now || now - request.IssuedUtc > _ttl)
            return DeploymentValidationResult.Fail("DEPLOY-EXPIRED", "La solicitud de despliegue expiró.");
        if (!_nonces.TryConsume(request.Nonce))
            return DeploymentValidationResult.Fail("DEPLOY-REPLAY", "El nonce ya fue utilizado.");
        return DeploymentValidationResult.Pass();
    }
}
