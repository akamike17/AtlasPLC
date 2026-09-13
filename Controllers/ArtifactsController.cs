using System.Text;
using AtlasSoftPlc.Application.Packages;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Web.Services;
using AtlasSoftPlc.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize]
public sealed class ArtifactsController(
    SimulationService simulation,
    ArtifactPipeline pipeline,
    IArtifactStore artifacts,
    IProgramTargetSelectionRepository selections,
    ITargetInstanceRepository instances,
    ITargetPluginRegistry plugins) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Generate(Guid id, string kind = "StructuredText", CancellationToken ct = default)
    {
        simulation.EnsureLibrary();
        var program = simulation.Catalog.FirstOrDefault(p => p.Id == id);
        if (program is null) return NotFound();

        var targetId = await selections.GetAsync(program.Id, ct);
        var target = string.IsNullOrWhiteSpace(targetId) ? null : await instances.GetAsync(targetId, ct);
        var result = target is null
            ? new ArtifactResult(kind, Array.Empty<byte>(), string.Empty, new[] { "Selecciona una instancia de target configurada antes de generar el artefacto." })
            : pipeline.Generate(program, target, kind);
        var stored = new GeneratedArtifact(
            Guid.NewGuid(), program.Id, CanonicalProgramHasher.ComputeHash(program), targetId,
            result.Kind, FileName(program, result.Kind), result.Content, result.Hash,
            DateTimeOffset.UtcNow, result.Succeeded ? "Generated" : "Rejected", result.Diagnostics);
        await artifacts.SaveAsync(stored, ct);

        var targetName = target is null
            ? "Sin target seleccionado"
            : plugins.Get(target.TargetPluginId)?.Descriptor.DisplayName ?? target.DisplayName;
        return View(new ArtifactGenerateViewModel(program, targetId, targetName, stored));
    }

    [HttpGet]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct = default)
    {
        var artifact = await artifacts.GetAsync(id, ct);
        if (artifact is null || !string.Equals(artifact.Status, "Generated", StringComparison.OrdinalIgnoreCase)) return NotFound();
        var contentType = artifact.Kind.Equals("PlcOpenXml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : "text/plain; charset=utf-8";
        return File(artifact.Content, contentType, artifact.FileName);
    }

    private static string FileName(PlcProgramDefinition program, string kind)
    {
        var safe = string.Concat(program.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        var extension = kind.Equals("PlcOpenXml", StringComparison.OrdinalIgnoreCase) ? "xml" : "st";
        return $"atlas-{safe}.{extension}";
    }
}

public sealed record ArtifactGenerateViewModel(
    PlcProgramDefinition Program,
    string? TargetId,
    string TargetName,
    GeneratedArtifact Artifact);
