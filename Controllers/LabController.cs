using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize]
public sealed class LabController(ITargetRegistry targets, PlcProgramService programs) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var catalog = await programs.GetAllAsync(ct);
        var statuses = new Dictionary<string, TargetRuntimeStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.GetAll())
            statuses[target.Id] = await targets.GetStatusAsync(target.Id, ct);

        return View(new LabViewModel(catalog, targets.GetAll(), statuses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string targetId, Guid programId, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(programId, ct);
        if (program is null) return NotFound();
        var result = await LabProtocolRunner.RunAsync(targetId, program, ct);
        TempData["LabResult"] = $"{program.Name}: {result}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAll(string targetId, CancellationToken ct)
    {
        var catalog = await programs.GetAllAsync(ct);
        var passed = 0;
        var failures = new List<string>();
        foreach (var program in catalog)
        {
            var result = await LabProtocolRunner.RunAsync(targetId, program, ct);
            if (result.StartsWith("PASS:", StringComparison.Ordinal)) passed++;
            else failures.Add($"{program.Name}: {result}");
        }
        TempData["LabResult"] = $"{targetId}: {passed}/{catalog.Count} casos PASS." +
            (failures.Count == 0 ? "" : " Fallos: " + string.Join(" | ", failures));
        return RedirectToAction(nameof(Index));
    }
}

public sealed record LabViewModel(
    IReadOnlyList<AtlasSoftPlc.Domain.Projects.PlcProgramDefinition> Programs,
    IReadOnlyList<TargetDescriptor> Targets,
    IReadOnlyDictionary<string, TargetRuntimeStatus> Statuses);

internal static class LabProtocolRunner
{
    public static async Task<string> RunAsync(string targetId, AtlasSoftPlc.Domain.Projects.PlcProgramDefinition program, CancellationToken ct)
    {
        var (file, args, workingDirectory) = targetId.ToLowerInvariant() switch
        {
            "siemens-s7" => ("wsl.exe", new[] { "-d", "AtlasUbuntu", "-u", "root", "--", "python3", "/mnt/c/Users/Admin/source/repos/AtlasPLC/.runtime-simulators/snap7_probe.py" }, (string?)null),
            "rockwell-logix" => ("wsl.exe", new[] { "-d", "AtlasUbuntu", "-u", "root", "--", "bash", "-lc", "python3 -m cpppo.server.enip.client -a 127.0.0.1:44818 -p 'Atlas_Start=(BOOL)1'" }, (string?)null),
            "mitsubishi-melsec" => ("dotnet", new[] { "run", "--project", ".runtime-simulators\\SLMP\\SLMP.Examples\\SLMP.Examples.csproj", "--no-restore", "--", "127.0.0.1", "2000" }, (string?)null),
            "omron-sysmac" => ("wsl.exe", new[] { "-d", "AtlasUbuntu", "-u", "root", "--", "bash", "-lc", "cd /mnt/c/Users/Admin/source/repos/AtlasPLC/.runtime-simulators/gofins && go run ./example" }, (string?)null),
            "beckhoff-twincat" => ("dotnet", new[] { "run", "--project", ".runtime-simulators\\AdsProbe\\AdsProbe.csproj", "--no-restore" }, (string?)null),
            _ => throw new ArgumentException("Target de laboratorio no soportado.", nameof(targetId))
        };

        var psi = new ProcessStartInfo(file) { WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar el runner de laboratorio.");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) return $"FAIL (exit {process.ExitCode}): {Trim(error)}";
        return $"PASS: {Trim(output)}";
    }

    private static string Trim(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
