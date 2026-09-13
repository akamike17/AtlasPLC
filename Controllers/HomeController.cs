using System.Diagnostics;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Models;
using AtlasSoftPlc.Web.Services;
using AtlasSoftPlc.Protocols.Modbus.Targets;
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

    public HomeController(RuntimeStateStore store, PlcRuntimeService runtime, SimulationService sim, ModbusIoService modbus)
    {
        _store = store;
        _runtime = runtime;
        _sim = sim;
        _modbus = modbus;
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

        return RedirectToAction(nameof(Programs));
    }

    private DashboardViewModel BuildModel() => new()
    {
        Runtime = _store.Snapshot,
        Project = _sim.Project!,
        Inputs = _sim.GetInputsUi(),
        Outputs = _sim.GetOutputsUi(),
        Explanation = _sim.Project?.Name ?? "",
        Catalog = _sim.GetLibrary(),
        ActiveProgramId = _sim.Active?.Id
        ,ModbusStatus = _modbus.ConnectionStatus
        ,ModbusError = _modbus.LastError
        ,TargetActions = new[]
        {
            TargetActionsViewModel.From(new AtlasSoftPlc.Runtime.Targets.AtlasRuntimeTargetAdapter(_runtime), "Atlas Runtime"),
            TargetActionsViewModel.From(new ModbusOnlineAdapter(), "Modbus Online")
        }
    };

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
    public Dictionary<string, object> Inputs { get; set; } = new();
    public Dictionary<string, object> Outputs { get; set; } = new();
    public string Explanation { get; set; } = "";
    public IReadOnlyList<PlcProgramDefinition> Catalog { get; set; } = new List<PlcProgramDefinition>();
    public Guid? ActiveProgramId { get; set; }
    public string ModbusStatus { get; set; } = "Disabled";
    public string? ModbusError { get; set; }
    public IReadOnlyList<TargetActionsViewModel> TargetActions { get; set; } = Array.Empty<TargetActionsViewModel>();
}

public sealed class TargetActionsViewModel
{
    public string Name { get; init; } = string.Empty;
    public bool Simulate { get; init; }
    public bool Monitor { get; init; }
    public bool Generate { get; init; }
    public bool Compile { get; init; }
    public bool Deploy { get; init; }
    public bool Verify { get; init; }

    public static TargetActionsViewModel From(IPlcTargetAdapter target, string name) => new()
    {
        Name = name,
        Simulate = target.Capabilities.Supports(TargetCapability.Simulate),
        Monitor = target.Capabilities.Supports(TargetCapability.ReadLiveData),
        Generate = target.Capabilities.Supports(TargetCapability.GenerateSource) || target.Capabilities.Supports(TargetCapability.GenerateProject),
        Compile = target.Capabilities.Supports(TargetCapability.Compile),
        Deploy = target.Capabilities.Supports(TargetCapability.DeployProgram) || target.Capabilities.Supports(TargetCapability.DeployHardware),
        Verify = target.Capabilities.Supports(TargetCapability.VerifyDeployment)
    };
}

public sealed class ProgramsViewModel
{
    public IReadOnlyList<PlcProgramDefinition> Catalog { get; set; } = new List<PlcProgramDefinition>();
    public Guid? ActiveProgramId { get; set; }
    public RuntimeSnapshot Runtime { get; set; } = RuntimeSnapshot.Initial;
}
