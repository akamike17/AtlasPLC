using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Application.Packages;
using AtlasSoftPlc.Domain.Graph;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Models;
using AtlasSoftPlc.Web.Services;
using AtlasSoftPlc.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

public class HomeController : Controller
{
    private readonly RuntimeStateStore _store;
    private readonly PlcRuntimeService _runtime;
    private readonly SimulationService _sim;
    private readonly ModbusIoService _modbus;
    private readonly ITargetRegistry _targets;
    private readonly ProgramVersionService _versions;
    private readonly GraphApplicationService _graphApplication;
    private readonly IProgramTargetSelectionRepository _selections;
    private readonly ArtifactPipeline _artifacts;
    private readonly ITargetPluginRegistry _pluginRegistry;
    private readonly IGraphDocumentRepository _graphDocuments;

    public HomeController(RuntimeStateStore store, PlcRuntimeService runtime, SimulationService sim, ModbusIoService modbus, ITargetRegistry targets, ProgramVersionService versions, GraphApplicationService graphApplication, ArtifactPipeline artifacts, IProgramTargetSelectionRepository selections, ITargetPluginRegistry pluginRegistry, IGraphDocumentRepository graphDocuments)
    {
        _store = store;
        _runtime = runtime;
        _sim = sim;
        _modbus = modbus;
        _targets = targets;
        _versions = versions;
        _graphApplication = graphApplication;
        _artifacts = artifacts;
        _selections = selections;
        _pluginRegistry = pluginRegistry;
        _graphDocuments = graphDocuments;
    }

    private void EnsureDemo()
    {
        _sim.EnsureLibrary();
        _sim.BootstrapTankDemo();
    }

    public IActionResult Index()
    {
        EnsureDemo();
        return View(BuildModel());
    }

    public IActionResult Simulation()
    {
        EnsureDemo();
        return View(BuildModel());
    }

    public IActionResult Diagnostics()
    {
        EnsureDemo();
        return View(BuildModel());
    }

    /// <summary>Biblioteca de programas: listar y seleccionar.</summary>
    public IActionResult Programs()
    {
        EnsureDemo();
        return View(new ProgramsViewModel
        {
            Catalog = _sim.GetLibrary(),
            ActiveProgramId = _sim.Active?.Id,
            Runtime = _store.Snapshot
        });
    }

    /// <summary>Carga un programa de la biblioteca (flujo seguro: Stop -> load -> Start).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult LoadProgram(Guid id)
    {
        EnsureDemo();

        // No permitir cambio silencioso con el runtime ejecutando: se detiene antes.
        if (_store.Snapshot.State == RuntimeState.Running)
        {
            // El propio LoadProgram hace Stop interno; aquí solo confirmamos el flujo seguro.
        }

        var ok = _sim.LoadProgram(id);
        if (!ok)
            return NotFound();

        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SendTemplateToSimulator(Guid id)
    {
        EnsureDemo();
        if (!_sim.LoadProgram(id))
            return NotFound();

        TempData["BuilderMessage"] = "Plantilla cargada directamente en el simulador.";
        return RedirectToAction(nameof(Simulation), new { view = "editor" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DuplicateProject(Guid id, string? name)
    {
        EnsureDemo();
        var copy = _sim.DuplicateProgram(id, name);
        TempData["BuilderMessage"] = copy is null ? "No se pudo duplicar el proyecto." : $"Proyecto duplicado: '{copy.Name}'.";
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrator")]
    public IActionResult DeleteProject(Guid id)
    {
        EnsureDemo();
        var deleted = _sim.DeleteProgram(id);
        TempData["BuilderMessage"] = deleted ? "Proyecto eliminado." : "No se puede eliminar una plantilla ni el proyecto activo. Duplica o carga otro proyecto antes.";
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveProject()
    {
        EnsureDemo();
        SaveVersion("Guardado manual del proyecto");
        TempData["BuilderMessage"] = _sim.Active is null
            ? "No hay un proyecto activo para guardar."
            : $"Proyecto guardado: '{_sim.Active.Name}'.";
        return RedirectToAction(nameof(Simulation));
    }

    [HttpGet]
    public IActionResult ExportProject(Guid id)
    {
        EnsureDemo();
        var project = _sim.Catalog.FirstOrDefault(p => p.Id == id);
        if (project is null) return NotFound();
        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"atlas-{project.Name.Replace(' ', '-')}.json");
    }

    [HttpGet]
    public IActionResult GenerateArtifact(Guid id, string kind = "StructuredText")
    {
        EnsureDemo();
        var project = _sim.Catalog.FirstOrDefault(p => p.Id == id);
        if (project is null) return NotFound();
        var artifact = _artifacts.Generate(project, kind);
        if (!artifact.Succeeded) return BadRequest(new { artifact.Kind, artifact.Diagnostics });
        var extension = artifact.Kind == "PlcOpenXml" ? "xml" : "st";
        return File(artifact.Content, artifact.Kind == "PlcOpenXml" ? "application/xml" : "text/plain", $"atlas-{project.Name.Replace(' ', '-')}.{extension}");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrator")]
    public IActionResult RestoreVersion(Guid versionId)
    {
        EnsureDemo();
        var version = _versions.GetAsync(versionId).GetAwaiter().GetResult();
        var restored = version is not null && _sim.RestoreProgramDefinition(version.DefinitionJson);
        TempData["BuilderMessage"] = restored ? $"Versión {version!.VersionNumber} restaurada y cargada en modo seguro." : "No se pudo restaurar la versión seleccionada.";
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportProject(IFormFile? file, string? name)
    {
        EnsureDemo();
        if (file is null || file.Length == 0 || file.Length > 2_000_000)
        {
            TempData["BuilderMessage"] = "Selecciona un archivo JSON de proyecto válido (máximo 2 MB).";
            return RedirectToAction(nameof(Simulation));
        }
        using var reader = new StreamReader(file.OpenReadStream());
        var json = await reader.ReadToEndAsync();
        TempData["BuilderMessage"] = _sim.ImportProgramDefinition(json, name ?? string.Empty)
            ? "Proyecto importado, validado y cargado en el simulador en estado seguro."
            : "Importación rechazada: el archivo no contiene una definición AtlasPLC válida.";
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult NewProject(string name)
    {
        EnsureDemo();
        _sim.CreateFromZero(name);
        SaveVersion("Proyecto creado desde cero");
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ApplyGraph(string graphJson)
    {
        EnsureDemo();
        if (_sim.Active is null || string.IsNullOrWhiteSpace(graphJson))
        {
            TempData["BuilderMessage"] = "Crea o carga un proyecto antes de aplicar el gráfico.";
            return RedirectToAction(nameof(Simulation));
        }
        try
        {
            using var doc = JsonDocument.Parse(graphJson);
            var graph = new GraphDocument { ProjectId = _sim.Active.Id, ProgramId = _sim.Active.Id };
            var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in doc.RootElement.GetProperty("nodes").EnumerateArray())
            {
                var rawId = node.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                var id = Guid.TryParse(rawId, out var parsed) ? parsed : Guid.NewGuid(); ids[rawId] = id;
                var kindText = node.GetProperty("kind").GetString() ?? "";
                var kind = kindText.ToLowerInvariant() switch { "input" or "sensor" => GraphNodeKind.Input, "output" or "pump" => GraphNodeKind.Output, "logic" or "and" => GraphNodeKind.And, "or" => GraphNodeKind.Or, "not" => GraphNodeKind.Not, "ton" => GraphNodeKind.Ton, "tof" => GraphNodeKind.Tof, "stop" or "emergencystop" => GraphNodeKind.EmergencyStop, "memory" => GraphNodeKind.Memory, _ => GraphNodeKind.Memory };
                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (node.TryGetProperty("properties", out var propertyElement) && propertyElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in propertyElement.EnumerateObject()) properties[property.Name] = property.Value.GetString() ?? property.Value.ToString();
                var position = new GraphPosition();
                if (node.TryGetProperty("position", out var positionElement) && positionElement.ValueKind == JsonValueKind.Object)
                {
                    if (positionElement.TryGetProperty("x", out var x) && x.TryGetDouble(out var px)) position.X = px;
                    if (positionElement.TryGetProperty("y", out var y) && y.TryGetDouble(out var py)) position.Y = py;
                }
                graph.Nodes.Add(new GraphNode { Id = id, Kind = kind, Name = node.GetProperty("name").GetString() ?? rawId, Properties = properties, Position = position });
            }
            foreach (var edge in doc.RootElement.GetProperty("edges").EnumerateArray())
            {
                var from = edge.GetProperty("from").GetString() ?? ""; var to = edge.GetProperty("to").GetString() ?? "";
                if (!ids.TryGetValue(from, out var fromId) || !ids.TryGetValue(to, out var toId)) { TempData["BuilderMessage"] = "Conexión inválida: el nodo origen o destino no existe."; return RedirectToAction(nameof(Simulation)); }
                graph.Edges.Add(new GraphEdge { FromNodeId = fromId, ToNodeId = toId });
            }
            var applied = _graphApplication.Apply(graph);
            if (!applied.Succeeded) { TempData["BuilderMessage"] = applied.Message + " " + string.Join(" ", (applied.ProgramReport?.Issues.Select(x => x.Message) ?? applied.GraphReport.Diagnostics.Select(x => x.Message)).Take(3)); return RedirectToAction(nameof(Simulation)); }
            SaveVersion("Diseño gráfico aplicado al proyecto");
            TempData["BuilderMessage"] = "Diseño gráfico validado y aplicado al simulador en estado seguro.";
        }
        catch (JsonException) { TempData["BuilderMessage"] = "El diseño gráfico no es válido y no se aplicó."; }
        catch (KeyNotFoundException) { TempData["BuilderMessage"] = "El diseño gráfico está incompleto: cada bloque requiere id, nombre y tipo."; }
        catch (InvalidOperationException ex) { TempData["BuilderMessage"] = "El diseño gráfico no se aplicó: " + ex.Message; }
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddComponent(string key, string direction)
    {
        EnsureDemo();
        var dir = Enum.TryParse<AtlasSoftPlc.Domain.Common.VariableDirection>(direction, true, out var parsed) ? parsed : AtlasSoftPlc.Domain.Common.VariableDirection.Input;
        TempData["BuilderMessage"] = _sim.AddBooleanComponent(key, dir) ? $"Componente '{key}' agregado." : "No se pudo agregar: la Key está vacía o duplicada.";
        if (TempData["BuilderMessage"] is string message && message.StartsWith("Componente", StringComparison.Ordinal)) SaveVersion("Componente agregado");
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveComponent(Guid id)
    {
        EnsureDemo();
        var removed = _sim.RemoveComponent(id);
        TempData["BuilderMessage"] = removed ? "Componente eliminado y referencias invalidadas limpiadas." : "No se pudo eliminar el componente.";
        if (removed) SaveVersion("Componente eliminado");
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ConnectComponents(string inputKey, string outputKey)
    {
        EnsureDemo();
        TempData["BuilderMessage"] = _sim.ConnectInputToOutput(inputKey, outputKey) ? "Conexión creada y simulación reiniciada." : "Conexión inválida: revisa que exista una entrada y una salida con esas Keys.";
        if (TempData["BuilderMessage"] is string message && message.StartsWith("Conexión creada", StringComparison.Ordinal)) SaveVersion("Conexión lógica agregada");
        return RedirectToAction(nameof(Simulation));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddTimedConnection(string inputKey, string outputKey, string presetMs)
    {
        EnsureDemo();
        var parsed = double.TryParse(presetMs, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds)
            || double.TryParse(presetMs, NumberStyles.Float, CultureInfo.CurrentCulture, out milliseconds);
        TempData["BuilderMessage"] = parsed && _sim.AddTimedConnection(inputKey, outputKey, milliseconds)
            ? $"Secuencia creada: {outputKey} se activará después de {milliseconds:0} ms."
            : "No se pudo crear la secuencia: revisa las Keys y el tiempo mayor que cero.";
        if (TempData["BuilderMessage"] is string message && message.StartsWith("Secuencia creada", StringComparison.Ordinal)) SaveVersion("Secuencia temporizada agregada");
        return RedirectToAction(nameof(Simulation));
    }

    private void SaveVersion(string reason)
    {
        if (_sim.Active is null) return;
        var json = JsonSerializer.Serialize(_sim.Active);
        var history = _versions.GetByProgramAsync(_sim.Active.Id).GetAwaiter().GetResult();
        var next = history.Count == 0 ? 1 : history.Max(v => v.VersionNumber) + 1;
        _versions.CreateAsync(_sim.Active.Id, next, json, User.Identity?.Name ?? "local", reason).GetAwaiter().GetResult();
    }

    private DashboardViewModel BuildModel()
    {
        var descriptors = _targets.GetAll();
        var statuses = descriptors.ToDictionary(t => t.Id, t => _targets.GetStatusAsync(t.Id).GetAwaiter().GetResult(), StringComparer.OrdinalIgnoreCase);
        var graph = _sim.Active is null ? null : _graphDocuments.GetAsync(_sim.Active.Id).GetAwaiter().GetResult();
        var graphJson = graph is null ? "null" : JsonSerializer.Serialize(graph, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });
        return new()
        {
        Runtime = _store.Snapshot,
        Project = _sim.Project!,
        Inputs = _sim.GetTypedInputsUi(),
        Outputs = _sim.GetTypedOutputsUi(),
        Explanation = _sim.Project?.Name ?? "",
        Catalog = _sim.GetLibrary(),
        ActiveProgramId = _sim.Active?.Id
        ,ModbusStatus = _modbus.ConnectionStatus
        ,ModbusError = _modbus.LastError
        ,TargetActions = descriptors.Select(t => TargetActionsViewModel.From(t, statuses[t.Id], _pluginRegistry.Get(t.Id)?.Actions ?? Array.Empty<TargetActionDescriptor>())).ToArray()
        ,TargetStatuses = statuses
        ,SelectedTargetId = _sim.Active is null ? null : _selections.GetAsync(_sim.Active.Id).GetAwaiter().GetResult()
        ,GraphJson = graphJson
        };
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}

public sealed class DashboardViewModel
{
    public RuntimeSnapshot Runtime { get; set; } = RuntimeSnapshot.Initial;
    public Project Project { get; set; } = null!;
    public IReadOnlyList<IoPointViewModel> Inputs { get; set; } = Array.Empty<IoPointViewModel>();
    public IReadOnlyList<IoPointViewModel> Outputs { get; set; } = Array.Empty<IoPointViewModel>();
    public string Explanation { get; set; } = "";
    public IReadOnlyList<PlcProgramDefinition> Catalog { get; set; } = new List<PlcProgramDefinition>();
    public Guid? ActiveProgramId { get; set; }
    public string ModbusStatus { get; set; } = "Disabled";
    public string? ModbusError { get; set; }
    public IReadOnlyList<TargetActionsViewModel> TargetActions { get; set; } = Array.Empty<TargetActionsViewModel>();
    public IReadOnlyDictionary<string, TargetRuntimeStatus> TargetStatuses { get; set; } = new Dictionary<string, TargetRuntimeStatus>();
    public string? SelectedTargetId { get; set; }
    public string GraphJson { get; set; } = "null";
}

public sealed class TargetActionsViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Simulate { get; init; }
    public bool Monitor { get; init; }
    public bool Generate { get; init; }
    public bool Compile { get; init; }
    public bool Deploy { get; init; }
    public bool Verify { get; init; }
    public IReadOnlyList<TargetActionDescriptor> Actions { get; init; } = Array.Empty<TargetActionDescriptor>();

    public string ConnectionState { get; init; } = "Unknown";
    public string ConnectionDetail { get; init; } = "";

    public static TargetActionsViewModel From(TargetDescriptor target, TargetRuntimeStatus status, IReadOnlyList<TargetActionDescriptor> actions) => new()
    {
        Id = target.Id,
        Name = target.DisplayName,
        Simulate = target.Capabilities.Supports(TargetCapability.Simulate),
        Monitor = target.Capabilities.Supports(TargetCapability.ReadLiveData),
        Generate = target.Capabilities.Supports(TargetCapability.GenerateSource) || target.Capabilities.Supports(TargetCapability.GenerateProject),
        Compile = target.Capabilities.Supports(TargetCapability.Compile),
        Deploy = target.Capabilities.Supports(TargetCapability.DeployProgram) || target.Capabilities.Supports(TargetCapability.DeployHardware),
        Verify = target.Capabilities.Supports(TargetCapability.VerifyDeployment),
        Actions = actions,
        ConnectionState = status.State,
        ConnectionDetail = status.Detail ?? ""
    };
}

public sealed class ProgramsViewModel
{
    public IReadOnlyList<PlcProgramDefinition> Catalog { get; set; } = new List<PlcProgramDefinition>();
    public Guid? ActiveProgramId { get; set; }
    public RuntimeSnapshot Runtime { get; set; } = RuntimeSnapshot.Initial;
}
