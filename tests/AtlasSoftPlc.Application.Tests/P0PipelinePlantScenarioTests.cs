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

    [Fact]
    public void ScenarioRunner_EvaluatesAssertionWithoutInputAndFinalDuration()
    {
        var scenario = new ScenarioDefinition
        {
            Duration = TimeSpan.FromSeconds(2),
            Inputs = new() { new(TimeSpan.Zero, "Permit", true) },
            Assertions = new()
            {
                new(TimeSpan.FromSeconds(1), o => o["Motor"], "midpoint assertion"),
                new(TimeSpan.FromSeconds(2), o => o["Motor"], "final assertion")
            }
        };
        var result = new ScenarioRunner().Run(scenario, i => new Dictionary<string, bool> { ["Motor"] = i.GetValueOrDefault("Permit") }, new VirtualSimulationClock());
        Assert.True(result.Passed);
        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, result.Trace.Select(x => x.At));
    }

    [Fact]
    public void ScenarioRunner_RejectsPointOutsideDuration()
    {
        var scenario = new ScenarioDefinition { Duration = TimeSpan.FromSeconds(1), Inputs = new() { new(TimeSpan.FromSeconds(2), "X", true) } };
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScenarioRunner().Run(scenario, _ => new Dictionary<string, bool>(), new VirtualSimulationClock()));
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

    [Fact]
    public void MutuallyExclusiveConditionsWithOppositePermissionsAreSafe()
    {
        var permission = new VariableDefinition { Key = "PermitA", Direction = VariableDirection.Input };
        var a = new VariableDefinition { Key = "A", Direction = VariableDirection.Output };
        var b = new VariableDefinition { Key = "B", Direction = VariableDirection.Output };
        var program = new LogicProgram { Rules = new()
        {
            new() { Condition = new VariableExpression { VariableId = permission.Id }, Actions = new() { new SetOutputAction { VariableId = a.Id, Value = "true" } } },
            new() { Condition = new NotExpression { Operand = new VariableExpression { VariableId = permission.Id } }, Actions = new() { new SetOutputAction { VariableId = b.Id, Value = "true" } } }
        }};
        var ca = new PlantComponent { VariableId = a.Id, SafeState = new() };
        var cb = new PlantComponent { VariableId = b.Id, SafeState = new() };
        ca.MutuallyExclusiveWith.Add(cb.Id);
        cb.MutuallyExclusiveWith.Add(ca.Id);
        var vars = new[] { permission, a, b }.ToDictionary(x => x.Id);
        var issues = new PlantModelValidationRule(new PlantModel { Components = new() { ca, cb } }).Validate(program, vars, new()).ToList();
        Assert.DoesNotContain(issues, i => i.DiagnosticId == "ATLAS-PLANT-0005");
    }

    [Fact]
    public void MutuallyExclusiveConditionsWithSharedPathAreBlocked()
    {
        var permission = new VariableDefinition { Key = "Permit", Direction = VariableDirection.Input };
        var a = new VariableDefinition { Key = "A", Direction = VariableDirection.Output };
        var b = new VariableDefinition { Key = "B", Direction = VariableDirection.Output };
        var condition = new VariableExpression { VariableId = permission.Id };
        var program = new LogicProgram { Rules = new()
        {
            new() { Condition = condition, Actions = new() { new SetOutputAction { VariableId = a.Id, Value = "true" } } },
            new() { Condition = new VariableExpression { VariableId = permission.Id }, Actions = new() { new SetOutputAction { VariableId = b.Id, Value = "true" } } }
        }};
        var ca = new PlantComponent { VariableId = a.Id, SafeState = new() };
        var cb = new PlantComponent { VariableId = b.Id, SafeState = new() };
        ca.MutuallyExclusiveWith.Add(cb.Id);
        var vars = new[] { permission, a, b }.ToDictionary(x => x.Id);
        var issues = new PlantModelValidationRule(new PlantModel { Components = new() { ca, cb } }).Validate(program, vars, new()).ToList();
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-PLANT-0005" && i.Severity == ValidationSeverity.Blocker);
    }

    [Fact]
    public void UnsupportedMutualExclusionAnalysisIsWarningOnly()
    {
        var a = new VariableDefinition { Key = "A", Direction = VariableDirection.Output };
        var b = new VariableDefinition { Key = "B", Direction = VariableDirection.Output };
        var program = new LogicProgram { Rules = new()
        {
            new() { Condition = new ArithmeticExpression(), Actions = new() { new SetOutputAction { VariableId = a.Id, Value = "true" } } },
            new() { Condition = new VariableExpression { VariableId = Guid.NewGuid() }, Actions = new() { new SetOutputAction { VariableId = b.Id, Value = "true" } } }
        }};
        var ca = new PlantComponent { VariableId = a.Id, SafeState = new() };
        var cb = new PlantComponent { VariableId = b.Id, SafeState = new() };
        ca.MutuallyExclusiveWith.Add(cb.Id);
        var vars = new[] { a, b }.ToDictionary(x => x.Id);
        var issues = new PlantModelValidationRule(new PlantModel { Components = new() { ca, cb } }).Validate(program, vars, new()).ToList();
        Assert.DoesNotContain(issues, i => i.DiagnosticId == "ATLAS-PLANT-0005");
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-PLANT-0010" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void RequirementBindingMustBePresentInEveryEnergizingPath()
    {
        var permission = new VariableDefinition { Key = "Guard", Direction = VariableDirection.Input };
        var motor = new VariableDefinition { Key = "Motor", Direction = VariableDirection.Output };
        var required = new PlantComponent { VariableId = permission.Id, SafeState = new() };
        var actuator = new PlantComponent { VariableId = motor.Id, SafeState = new() };
        actuator.Requires.Add(required.Id);
        actuator.RequirementBindings[required.Id] = permission.Id;
        var program = new LogicProgram { Rules = new() { new() { Condition = new ConstantExpression { Value = "true", DataType = "Bool" }, Actions = new() { new SetOutputAction { VariableId = motor.Id, Value = "true" } } } } };
        var issues = new PlantModelValidationRule(new PlantModel { Components = new() { required, actuator } }).Validate(program, new[] { permission, motor }.ToDictionary(x => x.Id), new()).ToList();
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-PLANT-0008" && i.Severity == ValidationSeverity.Blocker);
    }
}
