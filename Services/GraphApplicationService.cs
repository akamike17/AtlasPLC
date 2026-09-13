using System.Text.Json;
using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Graph;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Web.Services;

public sealed record GraphApplicationResult(
    bool Succeeded,
    string Message,
    GraphValidationReport GraphReport,
    ValidationReport? ProgramReport = null,
    string Code = "OK");

/// <summary>
/// Orquesta validate → lower → runtime replace → commit SQLite único.
/// No usa writes compensatorios para simular atomicidad: la persistencia se confirma
/// como gráfico + programa + versión en una sola transacción.
/// </summary>
public sealed class GraphApplicationService
{
    private readonly IGraphValidator _graphValidator;
    private readonly IGraphLowerer _lowerer;
    private readonly IProgramValidationPipeline _pipeline;
    private readonly IGraphApplyUnitOfWork _unitOfWork;
    private readonly IProgramVersionRepository _versions;
    private readonly SimulationService _simulation;

    public GraphApplicationService(
        IGraphValidator graphValidator,
        IGraphLowerer lowerer,
        IProgramValidationPipeline pipeline,
        IGraphApplyUnitOfWork unitOfWork,
        IProgramVersionRepository versions,
        SimulationService simulation)
    {
        _graphValidator = graphValidator;
        _lowerer = lowerer;
        _pipeline = pipeline;
        _unitOfWork = unitOfWork;
        _versions = versions;
        _simulation = simulation;
    }

    public GraphApplicationResult Apply(GraphDocument graph)
        => ApplyAsync(graph).GetAwaiter().GetResult();

    public async Task<GraphApplicationResult> ApplyAsync(GraphDocument graph, CancellationToken ct = default)
    {
        var graphReport = _graphValidator.Validate(graph);
        if (!graphReport.IsValid)
            return new(false, "El gráfico fue rechazado por validación.", graphReport, Code: "GRAPH_INVALID");
        if (_simulation.Active is null)
            return new(false, "No hay un proyecto activo.", graphReport, Code: "NO_ACTIVE_PROGRAM");

        PlcProgramDefinition candidate;
        try
        {
            candidate = _lowerer.Lower(graph, graphReport);
        }
        catch (InvalidOperationException ex)
        {
            return new(false, "El gráfico no se pudo traducir: " + ex.Message, graphReport, Code: "LOWERING_FAILED");
        }

        var previous = _simulation.Active;
        candidate.Id = previous.Id;
        candidate.Name = previous.Name;
        candidate.Description = previous.Description;
        candidate.Version = Math.Max(previous.Version + 1, 1);
        IReadOnlyDictionary<Guid, AtlasSoftPlc.Domain.Variables.VariableDefinition> candidateVariables;
        try
        {
            candidateVariables = candidate.Variables.ToDictionary(x => x.Id);
        }
        catch (ArgumentException ex)
        {
            return new(false, "El runtime rechazó la definición traducida: " + ex.Message, graphReport, Code: "RUNTIME_REJECTED");
        }
        var programResult = _pipeline.Validate(candidate.Logic, candidateVariables, ValidationOperation.Simulation, new ValidationContext { SafeStates = candidate.Failsafe });
        if (!programResult.Allowed)
            return new(false, "La lógica traducida fue rechazada.", graphReport, programResult.Report, "PROGRAM_INVALID");

        var history = await _versions.GetByProgramAsync(previous.Id, ct);
        var version = new ProgramVersion
        {
            ProgramId = previous.Id,
            VersionNumber = history.Count == 0 ? 1 : history.Max(x => x.VersionNumber) + 1,
            DefinitionJson = JsonSerializer.Serialize(candidate),
            CreatedBy = "GraphEditor",
            Reason = "Diseño gráfico aplicado",
            CreatedUtc = DateTime.UtcNow
        };
        version.Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(version.DefinitionJson))).ToLowerInvariant();

        // El runtime es el gate operativo final. Aún no se ha escrito ninguna fila.
        if (!_simulation.TryApplyRuntimeCandidate(candidate))
            return new(false, "El runtime rechazó el programa; la persistencia no fue modificada.", graphReport, programResult.Report, "RUNTIME_REJECTED");

        try
        {
            await _unitOfWork.CommitAsync(graph, candidate, version, ct);
            return new(true, "Gráfico aplicado y persistido de forma atómica.", graphReport, programResult.Report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La transacción SQLite ya hizo rollback. Sólo resta restaurar el runtime;
            // no se intenta “compensar” escribiendo filas desde aquí.
            var restored = _simulation.TryApplyRuntimeCandidate(previous);
            var suffix = restored ? " El runtime anterior fue restaurado." : " No fue posible restaurar el runtime automáticamente; mantén el proceso detenido.";
            return new(false, "No se pudo confirmar la persistencia del gráfico: " + ex.Message + suffix, graphReport, programResult.Report, "PERSISTENCE_FAILED");
        }
    }
}
