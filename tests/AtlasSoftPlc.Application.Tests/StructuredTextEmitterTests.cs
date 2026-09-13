using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Tests;

public sealed class StructuredTextEmitterTests
{
    [Fact]
    public void NonLatchedOutputIsAssignedOnBothBranchesAndPreservesElseActions()
    {
        var start = new VariableDefinition { Key = "Start", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var stop = new VariableDefinition { Key = "Stop", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var guard = new VariableDefinition { Key = "Guard", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var motor = new VariableDefinition { Key = "Motor", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var rule = new LogicRule { Condition = new AndExpression { Operands = new() { new VariableExpression { VariableId = start.Id }, new NotExpression { Operand = new VariableExpression { VariableId = stop.Id } }, new VariableExpression { VariableId = guard.Id } } }, Actions = new() { new SetOutputAction { VariableId = motor.Id, Value = "true" } } };
        var elseRule = new LogicRule { Condition = new VariableExpression { VariableId = stop.Id }, Actions = new() { new SetOutputAction { VariableId = motor.Id, Value = "false" } }, ElseActions = new() { new SetOutputAction { VariableId = motor.Id, Value = "true" } } };
        var artifact = new StructuredTextEmitter().Emit(new PlcProgramDefinition { Variables = new() { start, stop, guard, motor }, Logic = new LogicProgram { Rules = new() { rule, elseRule } } });
        Assert.True(artifact.IsSupported);
        Assert.Contains("Start", artifact.Source);
        Assert.Contains("NOT (Stop)", artifact.Source);
        Assert.Contains("Guard", artifact.Source);
        Assert.Contains("Motor := TRUE", artifact.Source);
        Assert.Contains("Motor := FALSE", artifact.Source);
        Assert.Contains("ELSE Motor := TRUE", artifact.Source);
    }
    [Fact]
    public void SameProgramProducesIdenticalSourceAndHash()
    {
        var program = AtlasIrFixtures.BuildMotorStopGuard().ToProgramDefinition();
        var emitter = new StructuredTextEmitter();
        var first = emitter.Emit(program);
        var second = emitter.Emit(program);
        Assert.True(first.IsSupported);
        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEmpty(first.SourceMap);
    }

    [Fact]
    public void UnsupportedFeatureProducesDiagnosticAndIsNotPresentedAsCompiled()
    {
        var program = new PlcProgramDefinition { Name = "unsupported", Logic = new LogicProgram { Rules = new() { new() { Condition = new TimerStateExpression(), Actions = new() { new SetOutputAction() } } } } };
        var artifact = new StructuredTextEmitter().Emit(program);
        Assert.False(artifact.IsSupported);
        Assert.Contains(artifact.Diagnostics, d => d.DiagnosticId == "ATLAS-ST-0001" || d.DiagnosticId == "ATLAS-ST-0002");
    }
}
