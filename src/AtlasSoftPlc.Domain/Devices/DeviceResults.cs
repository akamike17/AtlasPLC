using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Domain.Devices;

/// <summary>Resultado de una operación de escritura en un driver.</summary>
public sealed class WriteResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int WrittenCount { get; set; }

    public static WriteResult Ok(int count = 1) => new() { Success = true, WrittenCount = count };
    public static WriteResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed class DeviceConnectionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DriverState State { get; set; }
}

public sealed class DeviceHealth
{
    public string DriverId { get; set; } = string.Empty;
    public DriverState State { get; set; } = DriverState.Disconnected;
    public DateTime LastSuccessfulIoUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int TotalReads { get; set; }
    public int TotalWrites { get; set; }
    public int TotalFailures { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Dispositivo descubierto (sección 38). Discovery es siempre READ-ONLY.</summary>
public sealed class FoundDevice
{
    public string Ip { get; set; } = string.Empty;
    public List<string> ProtocolCandidates { get; set; } = new();
    public string? Vendor { get; set; }
    public string? Product { get; set; }
    public double Confidence { get; set; }
}