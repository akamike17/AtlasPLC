using System.Diagnostics;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Models;
using AtlasSoftPlc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

public class HomeController : Controller
{
    private readonly RuntimeStateStore _store;
    private readonly SimulationService _sim;

    public HomeController(RuntimeStateStore store, SimulationService sim)
    {
        _store = store;
        _sim = sim;
    }

    public IActionResult Index()
    {
        if (_sim.Project is null)
            _sim.BootstrapTankDemo();

        var model = new DashboardViewModel
        {
            Runtime = _store.Snapshot,
            Project = _sim.Project!,
            Inputs = _sim.GetInputsUi(),
            Outputs = _sim.GetOutputsUi(),
            Explanation = _sim.Project!.Name
        };
        return View(model);
    }

    public IActionResult Simulation()
    {
        if (_sim.Project is null)
            _sim.BootstrapTankDemo();

        return View(new DashboardViewModel
        {
            Runtime = _store.Snapshot,
            Project = _sim.Project!,
            Inputs = _sim.GetInputsUi(),
            Outputs = _sim.GetOutputsUi()
        });
    }

    public IActionResult Diagnostics()
    {
        if (_sim.Project is null)
            _sim.BootstrapTankDemo();

        return View(new DashboardViewModel
        {
            Runtime = _store.Snapshot,
            Project = _sim.Project!,
            Inputs = _sim.GetInputsUi(),
            Outputs = _sim.GetOutputsUi()
        });
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
    public AtlasSoftPlc.Domain.Projects.Project Project { get; set; } = null!;
    public Dictionary<string, object> Inputs { get; set; } = new();
    public Dictionary<string, object> Outputs { get; set; } = new();
    public string Explanation { get; set; } = "";
}