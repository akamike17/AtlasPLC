using System.Diagnostics;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize]
public sealed class LabController(
    ITargetRegistry targets,
    PlcProgramService programs,
    IProgramValidationPipeline pipeline,
    ILabProfileRegistry profiles) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var catalog = await programs.GetAllAsync(ct);
        var statuses = new Dictionary<string, TargetRuntimeStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles.GetAll())
            statuses[profile.TargetPluginId] = await targets.GetStatusAsync(profile.TargetPluginId, ct);
        return View(new LabViewModel(catalog, targets.GetAll(), statuses, profiles.GetAll()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string targetId, Guid programId, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(programId, ct);
        if (program is null) return NotFound();
        var profile = profiles.Get(targetId);
        if (profile is null)
        {
            TempData["LabResult"] = $"{targetId}: BLOQUEADO; no existe un perfil de laboratorio configurado.";
            return RedirectToAction(nameof(Index));
        }
        var validation = pipeline.Validate(program.Logic, program.Variables.ToDictionary(v => v.Id), ValidationOperation.Simulation, new ValidationContext { SafeStates = program.Failsafe });
        if (!validation.Allowed)
        {
            TempData["LabResult"] = $"{profile.DisplayName}: BLOQUEADO antes del probe. " + string.Join(" ", validation.Report.Issues.Select(i => i.Message).Take(3));
            return RedirectToAction(nameof(Index));
        }
        TempData["LabResult"] = $"{profile.DisplayName}: {await LabProtocolRunner.RunAsync(profile, ct)}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAll(string targetId, CancellationToken ct)
    {
        var profile = profiles.Get(targetId);
        if (profile is null)
        {
            TempData["LabResult"] = $"{targetId}: BLOQUEADO; no existe un perfil de laboratorio configurado.";
            return RedirectToAction(nameof(Index));
        }
        // Un probe por target/perfil; jamás se multiplica en falsos PASS de programa.
        TempData["LabResult"] = $"{profile.DisplayName}: {await LabProtocolRunner.RunAsync(profile, ct)}";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record LabViewModel(
    IReadOnlyList<AtlasSoftPlc.Domain.Projects.PlcProgramDefinition> Programs,
    IReadOnlyList<TargetDescriptor> Targets,
    IReadOnlyDictionary<string, TargetRuntimeStatus> Statuses,
    IReadOnlyList<LabProfile> Profiles);

public sealed record LabProfile
{
    public required string Id { get; init; }
    public required string TargetPluginId { get; init; }
    public required string DisplayName { get; init; }
    public required string Executable { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? WorkingDirectory { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

public interface ILabProfileRegistry
{
    IReadOnlyList<LabProfile> GetAll();
    LabProfile? Get(string id);
}

public sealed class ConfigurationLabProfileRegistry(IConfiguration configuration) : ILabProfileRegistry
{
    private readonly IReadOnlyList<LabProfile> _profiles = Load(configuration);
    public IReadOnlyList<LabProfile> GetAll() => _profiles;
    public LabProfile? Get(string id) => _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(p.TargetPluginId, id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<LabProfile> Load(IConfiguration configuration)
    {
        return configuration.GetSection("LabProfiles").GetChildren().Select(child => new LabProfile
        {
            Id = child["Id"] ?? child.Key,
            TargetPluginId = child["TargetPluginId"] ?? child.Key,
            DisplayName = child["DisplayName"] ?? child.Key,
            Executable = child["Executable"] ?? string.Empty,
            Arguments = child.GetSection("Arguments").GetChildren().Select(x => x.Value ?? string.Empty).ToArray(),
            WorkingDirectory = child["WorkingDirectory"],
            TimeoutSeconds = int.TryParse(child["TimeoutSeconds"], out var seconds) && seconds > 0 ? seconds : 30
        }).Where(p => !string.IsNullOrWhiteSpace(p.Executable)).ToArray();
    }
}

public static class LabProtocolRunner
{
    public static async Task<string> RunAsync(LabProfile profile, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(profile.Executable)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory) ? Directory.GetCurrentDirectory() : profile.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        foreach (var arg in profile.Arguments) psi.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar el probe de laboratorio.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.TimeoutSeconds, 1, 600)));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            return process.ExitCode == 0 ? $"PROTOCOL PROBE PASS: {Trim(output)}" : $"PROTOCOL PROBE FAIL (exit {process.ExitCode}): {Trim(error)}";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "PROTOCOL PROBE FAIL: timeout del perfil.";
        }
        catch (Exception ex)
        {
            return $"PROTOCOL PROBE FAIL: {ex.Message}";
        }
    }

    private static string Trim(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
