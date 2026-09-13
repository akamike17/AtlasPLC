using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Simulation;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Application.Tests;

public sealed class P0PipelinePlantScenarioTests
{
    [Fact]
    public async Task CanonicalPipeline_ValidatesLowersAndSimulates()
    {
        var target = new RecordingTarget();
        var pipeline = new AtlasIrSimulationPipeline(Array.Empty<IValidationRule>());
        var result = await pipeline.SimulateAsync(AtlasIrFixtures.BuildMotorStopGuard(), target);

        Assert.True(result.Installed);
        Assert.NotNull(result.Program);
        Assert.NotEmpty(result.Program!.Hash);
        Assert.Same(result.Program, target.Program);
    }

    [Fact]
    public async Task CanonicalPipeline_BlockerDoesNotCallTarget()
    {
        var ir = AtlasIrFixtures.BuildMotorStopGuard();
        ir.Logic.Rules[0].Condition = new VariableExpression { VariableId = Guid.NewGuid() };
        var target = new RecordingTarget();
        var result = await new AtlasIrSimulationPipeline(new[] { new UndefinedReferenceValidationRule() }).SimulateAsync(ir, target);

        Assert.False(result.Installed);
        Assert.Null(target.Program);
        Assert.Contains(result.Validation.Issues, i => i.Severity == ValidationSeverity.Blocker);
    }

    [Fact]
    public void ScenarioRunner_IsDeterministicForMotorInvariants()
    {
        var scenario = new ScenarioDefinition
        {
            Inputs = new()
            {
                new(TimeSpan.Zero, "Start", true),
                new(TimeSpan.FromSeconds(1), "GuardClosed", true),
                new(TimeSpan.FromSeconds(2), "Stop", true),
                new(TimeSpan.FromSeconds(3), "Stop", false),
            },
            Assertions = new()
            {
                new(TimeSpan.FromSeconds(2), o => !o["Motor"], "Stop must turn Motor off"),
            },
            Duration = TimeSpan.FromSeconds(3)
        };
        IReadOnlyDictionary<string, bool> Evaluate(IReadOnlyDictionary<string, bool> i) =>
            new Dictionary<string, bool> { ["Motor"] = i.GetValueOrDefault("Start") && !i.GetValueOrDefault("Stop") && i.GetValueOrDefault("GuardClosed") };

        var first = new ScenarioRunner().Run(scenario, Evaluate, new VirtualSimulationClock());
        var second = new ScenarioRunner().Run(scenario, Evaluate, new VirtualSimulationClock());
        Assert.True(first.Passed);
        Assert.Equal(first.Trace.Select(x => (x.At, x.Outputs["Motor"])), second.Trace.Select(x => (x.At, x.Outputs["Motor"])));
    }

    private sealed class RecordingTarget : PlcTargetAdapterBase
    {
        public RecordingTarget() : base(new TargetIdentity { Manufacturer = "Test", Family = "Simulator", Model = "P0" }, new[] { TargetCapability.Simulate }) { }
        public PlcProgramDefinition? Program { get; private set; }
        public override Task<TargetOperationResult> SimulateAsync(PlcProgramDefinition project, CancellationToken ct = default)
        {
            Program = project;
            return Task.FromResult(TargetOperationResult.Ok());
        }
    }
}

public sealed class PlantModelValidationTests
{
    [Fact]
    public void MissingRequirementAndSafeStateAreBlockers()
    {
        var output = new VariableDefinition { Key = "Motor", Direction = VariableDirection.Output };
        var plant = new PlantModel
        {
            Components = new() { new PlantComponent { VariableId = output.Id, Requires = new() { Guid.NewGuid() } } }
        };
        var issues = new PlantModelValidationRule(plant).Validate(new LogicProgram(), new Dictionary<Guid, VariableDefinition> { [output.Id] = output }, new()).ToList();
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-PLANT-0002");
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-PLANT-0003");
    }

    [Fact]
    public void EnergizableActuatorWithoutMandatoryPermissionIsBlocked()
    {
        var output = new VariableDefinition { Key = "Motor", Direction = VariableDirection.Output };
        var plant = new PlantModel
        {
            Components = new() { new PlantComponent { VariableId = output.Id, RequiresPhysicalPermission = true, SafeState = new() } }
        };
        var issues = new PlantModelValidationRule(plant).Validate(new LogicProgram(), new Dictionary<Guid, VariableDefinition> { [output.Id] = output }, new()).ToList();
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-PLANT-0006");
    }
}
