using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using Microsoft.AspNetCore.SignalR;

namespace AtlasSoftPlc.Web.Hubs;

/// <summary>
/// Hub SignalR para valores live, estado runtime y métricas (sección 41).
/// </summary>
public sealed class RuntimeHub : Hub
{
    private readonly RuntimeStateStore _store;
    private readonly PlcRuntimeService _runtime;

    public RuntimeHub(RuntimeStateStore store, PlcRuntimeService runtime)
    {
        _store = store;
        _runtime = runtime;
    }

    /// <summary>Envía el snapshot completo a un cliente recién conectado.</summary>
    public async Task SendFullSnapshot()
    {
        await Clients.Caller.SendAsync("RuntimeSnapshot", _store.Snapshot);
    }

    /// <summary>El cliente pide suscripción a cambios (no-op, el hub hace broadcast automático).</summary>
    public Task Subscribe() => Task.CompletedTask;
}

/// <summary>
/// Implementación que usa IHubContext para hacer broadcast.
/// </summary>
public sealed class SignalRRuntimeNotifier : IRuntimeNotifier
{
    private readonly IHubContext<RuntimeHub> _hub;

    public SignalRRuntimeNotifier(IHubContext<RuntimeHub> hub) => _hub = hub;

    public async Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot)
    {
        await _hub.Clients.All.SendAsync("RuntimeSnapshot", snapshot);
    }

    public async Task NotifyOutputChangedAsync(Guid variableId, PlcValue value)
    {
        await _hub.Clients.All.SendAsync("OutputChanged", new { id = variableId.ToString(), value });
    }

    public async Task NotifyInputChangedAsync(Guid variableId, PlcValue value)
    {
        await _hub.Clients.All.SendAsync("InputChanged", new { id = variableId.ToString(), value });
    }

    public async Task NotifyStateChangedAsync(RuntimeState state)
    {
        await _hub.Clients.All.SendAsync("RuntimeStateChanged", new { state = (int)state });
    }
}