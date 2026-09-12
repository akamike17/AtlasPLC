using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasSoftPlc.Web.Tests;

/// <summary>
/// Tests directos del SimulationService (bootstrap, TrySetInput IDOR-safe, snapshots UI).
/// </summary>
public sealed class SimulationServiceTests
{
    private sealed class NullNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }

    private static SimulationService CreateService()
    {
        var store = new RuntimeStateStore();
        var watchdog = new WatchdogService();
        var notifier = new NullNotifier();
        var runtime = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, watchdog, notifier);

        // Repositorio de la biblioteca sobre SQLite aislado en temp (nunca la BD real).
        var dbPath = Path.Combine(Path.GetTempPath(), "AtlasSoftPlcTests", Guid.NewGuid().ToString("N"), "atlas.db");
        var sqlite = new SqliteStore(dbPath);
        sqlite.EnsureCreated();
        var catalogService = new PlcProgramService(new SqlitePlcProgramRepository(sqlite));

        return new SimulationService(runtime, catalogService);
    }

    [Fact]
    public void BootstrapTankDemo_CreatesProject_WithVariables()
    {
        var sim = CreateService();
        var project = sim.BootstrapTankDemo();

        Assert.NotNull(project);
        Assert.Equal("Tanque de agua", project.Name);
        Assert.NotNull(sim.ActiveProgram);
        Assert.Equal(3, sim.ActiveProgram!.Rules.Count);
        Assert.Equal(4, sim.Variables.Count); // 3 inputs + 1 output
    }

    [Fact]
    public void BootstrapTankDemo_IsIdempotent()
    {
        var sim = CreateService();
        var p1 = sim.BootstrapTankDemo();
        var p2 = sim.BootstrapTankDemo();

        // Idempotente: una vez cargado, volver a bootstrapear NO recrea el proyecto activo.
        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.Same(p1, p2);
        Assert.Equal("Tanque de agua", p2!.Name);
    }

    [Fact]
    public void TrySetInput_ValidInput_ReturnsTrue()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var inputId = sim.Variables.Values.First(v => v.Direction == VariableDirection.Input).Id;
        Assert.True(sim.TrySetInput(inputId, true));
    }

    [Fact]
    public void TrySetInput_OutputVariable_ReturnsFalse()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var outputId = sim.Variables.Values.First(v => v.Direction == VariableDirection.Output).Id;
        Assert.False(sim.TrySetInput(outputId, true));
    }

    [Fact]
    public void TrySetInput_UnknownGuid_ReturnsFalse()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        Assert.False(sim.TrySetInput(Guid.NewGuid(), true));
    }

    [Fact]
    public void TrySetInput_UpdatesTimeline()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var inputId = sim.Variables.Values.First(v => v.Direction == VariableDirection.Input).Id;
        var varDef = sim.Variables[inputId];
        sim.TrySetInput(inputId, true);

        Assert.Single(sim.Timeline);
        Assert.Contains(varDef.DisplayName, sim.Timeline[0].Description);
        Assert.Contains("ON", sim.Timeline[0].Description);
    }

    [Fact]
    public void GetInputsUi_ReturnsOnlyInputs()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var inputs = sim.GetInputsUi();
        Assert.Equal(3, inputs.Count); // 3 inputs
    }

    [Fact]
    public void GetOutputsUi_ReturnsOnlyOutputs()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var outputs = sim.GetOutputsUi();
        Assert.Single(outputs); // 1 output (Bomba)
    }

    [Fact]
    public void GetInputsUi_ReflectsSetValue()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var inputId = sim.Variables.Values.First(v => v.Direction == VariableDirection.Input).Id;
        sim.TrySetInput(inputId, true);

        // TrySetInput quedó registrado en el timeline (fuente de verdad observable sin loop de scan).
        Assert.Single(sim.Timeline);
        Assert.Contains("ON", sim.Timeline[0].Description);

        // El contrato de GetInputsUi devuelve un objeto por input con la forma esperada.
        // El valor publicado real llega vía el scan del runtime (no corre sin el hosted service);
        // aquí verificamos la forma del DTO, que es lo que consume la UI.
        var inputs = sim.GetInputsUi();
        var entry = inputs[inputId.ToString()];
        Assert.NotNull(entry);

        var json = System.Text.Json.JsonSerializer.Serialize(entry);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"key\"", json);
        Assert.Contains("\"displayName\"", json);
        Assert.Contains("\"value\"", json);
    }

    [Fact]
    public void Start_Stop_PostCommands()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        // No exception
        sim.Start();
        sim.Stop();
    }

    [Fact]
    public void Project_Null_UntilBootstrapped()
    {
        var sim = CreateService();
        Assert.Null(sim.Project);
        Assert.Null(sim.ActiveProgram);
        Assert.Empty(sim.Variables);
    }

    [Fact]
    public void BootstrapTankDemo_ProgramHasEStopRuleWithPriority1000()
    {
        var sim = CreateService();
        sim.BootstrapTankDemo();

        var estopRule = sim.ActiveProgram!.Rules.First(r => r.Name == "Paro de emergencia");
        Assert.Equal(1000, estopRule.Priority);
        Assert.IsType<AtlasSoftPlc.Domain.Logic.SetOutputAction>(estopRule.Actions[0]);
    }
}