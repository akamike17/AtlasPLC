using System.Text.Json;
using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Web.Services;

public sealed record GraphApplicationResult(bool Succeeded, string Message, GraphValidationReport GraphReport, ValidationReport? ProgramReport = null);

/// <summary>Orquesta validate → lower → validate IR → persist/replace. No deja mutaciones parciales.</summary>
public sealed class GraphApplicationService(
    IGraphValidator graphValidator,
    IGraphLowerer lowerer,
    IProgramValidationPipeline pipeline,
    IGraphDocumentRepository documents,
    SimulationService simulation)
{
    public GraphApplicationResult Apply(GraphDocument graph)
    {
        var graphReport = graphValidator.Validate(graph);
        if (!graphReport.IsValid) return new(false, "El gráfico fue rechazado por validación.", graphReport);
        var candidate = lowerer.Lower(graph, graphReport);
        if (simulation.Active is null) return new(false, "No hay un proyecto activo.", graphReport);
        candidate.Id = simulation.Active.Id; candidate.Name = simulation.Active.Name;
        var programResult = pipeline.Validate(candidate.Logic, candidate.Variables.ToDictionary(x => x.Id), ValidationOperation.Simulation);
        if (!programResult.Allowed) return new(false, "La lógica traducida fue rechazada.", graphReport, programResult.Report);
        var previous = JsonSerializer.Serialize(simulation.Active);
        if (!simulation.RestoreProgramDefinition(JsonSerializer.Serialize(candidate)))
            return new(false, "El runtime rechazó el programa; no se aplicó el gráfico.", graphReport, programResult.Report);
        try
        {
            documents.SaveAsync(graph).GetAwaiter().GetResult();
            return new(true, "Gráfico aplicado y persistido.", graphReport, programResult.Report);
        }
        catch
        {
            simulation.RestoreProgramDefinition(previous);
            throw;
        }
    }
}
