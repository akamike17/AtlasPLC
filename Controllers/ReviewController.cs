using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Web.Services;
using AtlasSoftPlc.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize]
public sealed class ReviewController(SimulationService simulation, ProgramVersionService versions, IProgramValidationPipeline pipeline, IProgramTargetSelectionRepository selections, ITargetRegistry targets) : Controller
{
    public IActionResult Index()
    {
        simulation.EnsureLibrary();
        var program = simulation.Active;
        if (program is null) return RedirectToAction("Simulation", "Home");
        var variables = program.Variables.ToDictionary(v => v.Id);
        var report = pipeline.Validate(program.Logic, variables, ValidationOperation.Generate, new ValidationContext { Variables = variables, SafeStates = program.Failsafe }).Report;
        var history = versions.GetByProgramAsync(program.Id).GetAwaiter().GetResult();
        if (history.Count == 0)
        {
            var json = JsonSerializer.Serialize(program);
            versions.CreateAsync(program.Id, 1, json, User.Identity?.Name ?? "local", "Versión base recuperada").GetAwaiter().GetResult();
            history = versions.GetByProgramAsync(program.Id).GetAwaiter().GetResult();
        }
        return View(new ReviewViewModel(program, report, history));
    }

    [HttpGet]
    public IActionResult Compare(Guid leftId, Guid rightId)
    {
        var left = versions.GetAsync(leftId).GetAwaiter().GetResult();
        var right = versions.GetAsync(rightId).GetAwaiter().GetResult();
        if (left is null || right is null || left.ProgramId != right.ProgramId)
            return BadRequest("Las versiones seleccionadas no pertenecen al mismo proyecto.");
        using var leftDoc = JsonDocument.Parse(left.DefinitionJson);
        using var rightDoc = JsonDocument.Parse(right.DefinitionJson);
        var changes = new List<string>();
        CompareNode(leftDoc.RootElement, rightDoc.RootElement, "Proyecto", changes);
        return View(new VersionCompareViewModel(left, right, changes));
    }

    private static void CompareNode(JsonElement left, JsonElement right, string path, List<string> changes)
    {
        if (left.ValueKind != right.ValueKind) { changes.Add($"{path}: cambió el tipo de dato."); return; }
        if (left.ValueKind == JsonValueKind.Object)
        {
            var names = left.EnumerateObject().Select(p => p.Name).Concat(right.EnumerateObject().Select(p => p.Name)).Distinct();
            foreach (var name in names)
            {
                var hasLeft = left.TryGetProperty(name, out var l); var hasRight = right.TryGetProperty(name, out var r);
                if (!hasLeft || !hasRight) { changes.Add($"{path}.{name}: {(hasLeft ? "eliminado" : "agregado")}."); continue; }
                CompareNode(l, r, $"{path}.{name}", changes);
            }
        }
        else if (left.ValueKind == JsonValueKind.Array)
        {
            if (left.GetRawText() != right.GetRawText()) changes.Add($"{path}: cambió de {left.GetArrayLength()} a {right.GetArrayLength()} elementos.");
        }
        else if (left.GetRawText() != right.GetRawText()) changes.Add($"{path}: cambió de {left.GetRawText()} a {right.GetRawText()}.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Send()
    {
        simulation.EnsureLibrary();
        var program = simulation.Active;
        if (program is null) return RedirectToAction("Simulation", "Home");
        var variables = program.Variables.ToDictionary(v => v.Id);
        var result = pipeline.Validate(program.Logic, variables, ValidationOperation.Deploy, new ValidationContext { Variables = variables, SafeStates = program.Failsafe });
        if (!result.Allowed)
        {
            TempData["ReviewMessage"] = "Envío detenido: corrige todos los hallazgos antes de continuar.";
            return RedirectToAction(nameof(Index));
        }
        var targetId = selections.GetAsync(program.Id).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(targetId) || targets.Get(targetId) is null)
        {
            TempData["ReviewMessage"] = "Envío detenido: selecciona un target válido para este programa antes de desplegar.";
            return RedirectToAction(nameof(Index));
        }
        TempData["ReviewMessage"] = $"Proyecto '{program.Name}' validado. Regresando al simulador para observar la ejecución.";
        return RedirectToAction("Simulation", "Home");
    }
}

public sealed record ReviewViewModel(AtlasSoftPlc.Domain.Projects.PlcProgramDefinition Program, ValidationReport Report, IReadOnlyList<AtlasSoftPlc.Domain.Projects.ProgramVersion> History);
public sealed record VersionCompareViewModel(AtlasSoftPlc.Domain.Projects.ProgramVersion Left, AtlasSoftPlc.Domain.Projects.ProgramVersion Right, IReadOnlyList<string> Changes);
