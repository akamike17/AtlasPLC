using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using System.Collections.Generic;

namespace AtlasSoftPlc.Domain.Runtime;

/// <summary>
/// DTO ligero para snapshot del runtime (evita referencia circular).
/// </summary>
public sealed class RuntimeSnapshotDto
{
    public RuntimeState State { get; set; }
    public RuntimeMode Mode { get; set; }
    public string? ActiveProgramName { get; set; }
    public string? ActiveProgramHash { get; set; }
    public int ActiveProgramVersion { get; set; }
    public long ScanNumber { get; set; }
    public double LastScanMs { get; set; }
    public double AverageScanMs { get; set; }
    public double MaxScanMs { get; set; }
    public double MinScanMs { get; set; }
    public long Overruns { get; set; }
    public long TotalScans { get; set; }
    public DateTime? LastCompletedUtc { get; set; }
    public Dictionary<Guid, RuntimeValue> Inputs { get; set; } = new();
    public Dictionary<Guid, RuntimeValue> Outputs { get; set; } = new();
}

/// <summary>
/// Interfaz para notificar cambios de estado del runtime a suscriptores (UI, SignalR, etc.).
/// Definida en Domain para evitar referencias circulares (Runtime → Web → Runtime).
/// </summary>
public interface IRuntimeNotifier
{
    Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot);
    Task NotifyOutputChangedAsync(Guid variableId, PlcValue value);
    Task NotifyInputChangedAsync(Guid variableId, PlcValue value);
    Task NotifyStateChangedAsync(RuntimeState state);
}