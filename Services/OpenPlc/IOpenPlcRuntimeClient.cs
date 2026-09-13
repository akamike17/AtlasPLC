using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

public interface IOpenPlcRuntimeClient
{
    Task<OpenPlcProbeResult> ProbeAsync(TargetInstance instance, CancellationToken ct = default);
    Task<OpenPlcProbeResult> LoginAsync(TargetInstance instance, CancellationToken ct = default);
    Task<OpenPlcProbeResult> RuntimeLogsAsync(TargetInstance instance, CancellationToken ct = default);
    Task<OpenPlcProbeResult> StartAsync(TargetInstance instance, CancellationToken ct = default);
    Task<OpenPlcProbeResult> StopAsync(TargetInstance instance, CancellationToken ct = default);
}
