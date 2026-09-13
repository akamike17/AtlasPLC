namespace AtlasSoftPlc.Web.Services;

public sealed record OpenPlcProbeResult(
    bool Succeeded,
    string State,
    string Message,
    IReadOnlyDictionary<string, string>? Data = null);
