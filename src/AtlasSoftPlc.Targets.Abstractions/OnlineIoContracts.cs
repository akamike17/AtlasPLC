namespace AtlasSoftPlc.Targets;

/// <summary>Contrato explícito para targets de I/O online; no representa despliegue PLC.</summary>
public interface IOnlineIoProvider
{
    Task<OnlineIoResult> ConnectAsync(TargetInstance instance, CancellationToken ct = default);
    Task<OnlineIoResult> ReadAsync(TargetInstance instance, IReadOnlyCollection<string> addresses, CancellationToken ct = default);
    Task<OnlineIoResult> WriteAsync(TargetInstance instance, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);
    Task<OnlineIoResult> DiagnosticsAsync(TargetInstance instance, CancellationToken ct = default);
}

public sealed record OnlineIoResult(
    bool Succeeded,
    string State,
    string Message,
    IReadOnlyDictionary<string, string>? Values = null)
{
    public static OnlineIoResult Unsupported(string operation) =>
        new(false, "Unsupported", $"La operación de I/O online '{operation}' no está soportada.");
}
