namespace AtlasSoftPlc.Targets;

/// <summary>Contrato de laboratorio externo. No declara éxito cuando el proceso no está disponible.</summary>
public interface IExternalSimulatorAdapter
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<TargetOperationResult> ProbeAsync(CancellationToken ct = default);
    Task<TargetOperationResult> ImportArtifactAsync(string artifactPath, CancellationToken ct = default);
    Task<TargetOperationResult> LaunchAsync(CancellationToken ct = default);
    Task<TargetOperationResult> ConnectAsync(CancellationToken ct = default);
    Task<TargetOperationResult> StartAsync(CancellationToken ct = default);
    Task<TargetOperationResult> StopAsync(CancellationToken ct = default);
    Task<TargetOperationResult> SetInputAsync(string name, object value, CancellationToken ct = default);
    Task<(TargetOperationResult Result, object? Value)> ReadOutputAsync(string name, CancellationToken ct = default);
    Task<TargetOperationResult> ResetAsync(CancellationToken ct = default);
    Task<(TargetOperationResult Result, IReadOnlyList<string> Trace)> ReadTraceAsync(CancellationToken ct = default);
}
