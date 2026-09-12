namespace AtlasSoftPlc.Targets;

/// <summary>
/// Resultado tipado de una operación de target (spec §39 FASE B). Nunca se
/// "simula éxito": cada operación devuelve éxito/fallo explícito con razón.
/// </summary>
public sealed class TargetOperationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ArtifactHash { get; init; }
    public string? Detail { get; init; }

    public static TargetOperationResult Ok(string? artifactHash = null, string? detail = null) =>
        new() { Success = true, ArtifactHash = artifactHash, Detail = detail };

    public static TargetOperationResult Fail(string error) =>
        new() { Success = false, Error = error };

    public static TargetOperationResult Unsupported(string operation) =>
        new() { Success = false, Error = $"Capacidad no soportada por este target: {operation}" };
}

/// <summary>
/// Resultado de una verificación online posterior al despliegue (spec §1/§16).
/// Indica si se pudo confirmar que el programa esperado quedó cargado.
/// </summary>
public sealed class VerifyResult
{
    public bool Verified { get; init; }
    public string? ExpectedHash { get; init; }
    public string? ObservedHash { get; init; }
    public string? Error { get; init; }

    public static VerifyResult Ok(string expectedHash, string observedHash) =>
        new() { Verified = true, ExpectedHash = expectedHash, ObservedHash = observedHash };

    public static VerifyResult Mismatch(string expectedHash, string observedHash) =>
        new() { Verified = false, ExpectedHash = expectedHash, ObservedHash = observedHash, Error = "Hash observado no coincide con el esperado." };

    public static VerifyResult Fail(string error) =>
        new() { Verified = false, Error = error };
}

/// <summary>
/// Reporte de compatibilidad de un proyecto IR contra un target (spec §34).
/// Un proyecto puede ser Bloqueado, pasar con warnings, o pasar limpio.
/// </summary>
public sealed class CompatibilityReport
{
    public enum CompatibilityStatus
    {
        Pass,
        PassWithWarnings,
        Blocked,
    }

    public CompatibilityStatus Status { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public bool IsBlocked => Status == CompatibilityStatus.Blocked;
}