using AtlasSoftPlc.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AtlasSoftPlc.Infrastructure.Persistence;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize]
public sealed class TargetsController(ITargetRegistry registry, SqliteStore store) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var targets = registry.GetAll();
        var statuses = new Dictionary<string, TargetRuntimeStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
            statuses[target.Id] = await registry.GetStatusAsync(target.Id, ct);
        var configs = new Dictionary<string, TargetConfiguration>(StringComparer.OrdinalIgnoreCase);
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TargetId, Endpoint, Port, TimeoutMs FROM TargetConfigurations";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) configs[reader.GetString(0)] = new TargetConfiguration(reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
        return View(new TargetsViewModel(targets, statuses, configs));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveConfiguration(string targetId, string endpoint, int port, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(endpoint) || port is < 1 or > 65535 || timeoutMs is < 100 or > 60000)
            return BadRequest("Endpoint, puerto o timeout inválido.");
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO TargetConfigurations(TargetId,Endpoint,Port,TimeoutMs,UpdatedUtc) VALUES($id,$e,$p,$t,$u) ON CONFLICT(TargetId) DO UPDATE SET Endpoint=$e,Port=$p,TimeoutMs=$t,UpdatedUtc=$u";
        cmd.Parameters.AddWithValue("$id", targetId); cmd.Parameters.AddWithValue("$e", endpoint.Trim()); cmd.Parameters.AddWithValue("$p", port); cmd.Parameters.AddWithValue("$t", timeoutMs); cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
        TempData["TargetMessage"] = $"Configuración guardada para {targetId}.";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record TargetsViewModel(
    IReadOnlyList<TargetDescriptor> Targets,
    IReadOnlyDictionary<string, TargetRuntimeStatus> Statuses,
    IReadOnlyDictionary<string, TargetConfiguration> Configurations);

public sealed record TargetConfiguration(string Endpoint, int Port, int TimeoutMs);
