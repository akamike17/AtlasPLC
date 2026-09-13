using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize]
public sealed class TargetsController(
    ITargetPluginRegistry registry,
    ITargetInstanceRepository instanceRepository,
    ITargetRuntimeStatusService statusService,
    IProgramTargetSelectionRepository selections) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var catalog = registry.GetAll().Select(x => x.Descriptor).ToArray();
        var catalogStatuses = catalog.ToDictionary(
            target => target.Id,
            target => new TargetRuntimeStatus(target.ImplementationState.ToString(), target.Description),
            StringComparer.OrdinalIgnoreCase);
        var configured = new List<ConfiguredTargetViewModel>();
        foreach (var instance in await instanceRepository.GetAllAsync(ct))
        {
            var plugin = registry.Get(instance.TargetPluginId);
            var status = await statusService.GetStatusAsync(instance.Id, ct);
            configured.Add(new ConfiguredTargetViewModel(instance, plugin?.Descriptor, plugin?.ConfigurationSchema ?? Array.Empty<TargetConfigurationField>(), status));
        }

        return View(new TargetsViewModel(catalog, catalogStatuses, configured));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInstance(
        string targetPluginId,
        string displayName,
        string? endpoint,
        int port,
        int timeoutMs,
        string? baseUrl,
        bool allowSelfSigned,
        string? credentialReference,
        CancellationToken ct)
    {
        var plugin = registry.Get(targetPluginId);
        if (plugin is null) return BadRequest("El plugin seleccionado no existe.");
        if (string.IsNullOrWhiteSpace(displayName)) return BadRequest("La instancia requiere un nombre.");
        if (!TryBuildConfiguration(plugin, endpoint, port, timeoutMs, baseUrl, allowSelfSigned, out var configuration, out var error)) return BadRequest(error);

        var baseId = Slug(displayName, plugin.Descriptor.Id);
        var instanceId = baseId;
        var suffix = 2;
        while (await instanceRepository.GetAsync(instanceId, ct) is not null)
            instanceId = $"{baseId}-{suffix++}";

        await instanceRepository.SaveAsync(new TargetInstance
        {
            Id = instanceId,
            TargetPluginId = plugin.Descriptor.Id,
            TargetType = plugin.Descriptor.Id,
            DisplayName = displayName.Trim(),
            Configuration = configuration,
            CredentialReference = string.IsNullOrWhiteSpace(credentialReference) ? null : credentialReference.Trim()
        }, ct);
        TempData["TargetMessage"] = $"Instancia '{displayName.Trim()}' creada como {instanceId}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveConfiguration(
        string instanceId,
        string endpoint,
        int port,
        int timeoutMs,
        string? baseUrl,
        bool allowSelfSigned,
        CancellationToken ct)
    {
        var instance = await instanceRepository.GetAsync(instanceId, ct);
        if (instance is null) return NotFound("La instancia no existe.");
        var plugin = registry.Get(instance.TargetPluginId);
        if (plugin is null) return BadRequest("El plugin de la instancia ya no está registrado.");
        if (!TryBuildConfiguration(plugin, endpoint, port, timeoutMs, baseUrl, allowSelfSigned, out var configuration, out var error)) return BadRequest(error);

        await instanceRepository.SaveAsync(instance with { Configuration = configuration }, ct);
        TempData["TargetMessage"] = $"Configuración guardada para {instance.DisplayName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> DeleteInstance(string instanceId, CancellationToken ct)
    {
        var instance = await instanceRepository.GetAsync(instanceId, ct);
        if (instance is null) return NotFound("La instancia no existe.");
        await instanceRepository.DeleteAsync(instanceId, ct);
        TempData["TargetMessage"] = $"Instancia '{instance.DisplayName}' eliminada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(string instanceId, CancellationToken ct)
    {
        var instance = await instanceRepository.GetAsync(instanceId, ct);
        if (instance is null) return NotFound("La instancia no existe.");
        var status = await statusService.GetStatusAsync(instanceId, ct);
        TempData["TargetMessage"] = $"{instance.DisplayName}: {status.State} — {status.Detail}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectForProgram(Guid programId, string targetId, CancellationToken ct)
    {
        if (programId == Guid.Empty || string.IsNullOrWhiteSpace(targetId)) return BadRequest("Programa o instancia inválidos.");
        var instance = await instanceRepository.GetAsync(targetId, ct);
        if (instance is null || registry.Get(instance.TargetPluginId) is null) return BadRequest("La instancia seleccionada no existe o no tiene plugin.");
        await selections.SaveAsync(programId, instance.Id, ct);
        TempData["TargetMessage"] = $"Instancia '{instance.DisplayName}' seleccionada para el programa.";
        return RedirectToAction(nameof(Index));
    }

    private static bool TryBuildConfiguration(ITargetPlugin plugin, string? endpoint, int port, int timeoutMs, string? baseUrl, bool allowSelfSigned, out IReadOnlyDictionary<string, string> configuration, out string error)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (plugin.ConfigurationSchema.Count == 0)
        {
            configuration = result;
            error = string.Empty;
            return true;
        }
        if (string.IsNullOrWhiteSpace(endpoint) || port is < 1 or > 65535 || timeoutMs is < 100 or > 60000)
        {
            configuration = result;
            error = "Endpoint, puerto o timeout inválido.";
            return false;
        }
        result["endpoint"] = endpoint.Trim();
        result["port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result["timeoutMs"] = timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (plugin.ConfigurationSchema.Any(x => x.Key.Equals("baseUrl", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(baseUrl))
            result["baseUrl"] = baseUrl.Trim();
        if (plugin.ConfigurationSchema.Any(x => x.Key.Equals("allowSelfSigned", StringComparison.OrdinalIgnoreCase)))
            result["allowSelfSigned"] = allowSelfSigned.ToString();
        configuration = result;
        error = string.Empty;
        return true;
    }

    private static string Slug(string value, string fallback)
    {
        var chars = value.Normalize(NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray();
        var slug = new string(chars).Trim('-','_').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(slug) ? $"{fallback}-instance" : slug;
    }
}

public sealed record TargetsViewModel(
    IReadOnlyList<TargetDescriptor> Targets,
    IReadOnlyDictionary<string, TargetRuntimeStatus> Statuses,
    IReadOnlyList<ConfiguredTargetViewModel> ConfiguredInstances);

public sealed record ConfiguredTargetViewModel(
    TargetInstance Instance,
    TargetDescriptor? Plugin,
    IReadOnlyList<TargetConfigurationField> ConfigurationSchema,
    TargetRuntimeStatus Status);
