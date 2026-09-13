using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Tests;

public sealed class StructuredTextEmitterTests
{
    [Fact]
    public void NonLatchedOutputRetainsPreviousValueWithoutElseAndElseActionIsExplicit()
    {
        var start = new VariableDefinition { Key = "Start", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var stop = new VariableDefinition { Key = "Stop", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var guard = new VariableDefinition { Key = "Guard", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var motor = new VariableDefinition { Key = "Motor", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var rule = new LogicRule { Condition = new AndExpression { Operands = new() { new VariableExpression { VariableId = start.Id }, new NotExpression { Operand = new VariableExpression { VariableId = stop.Id } }, new VariableExpression { VariableId = guard.Id } } }, Actions = new() { new SetOutputAction { VariableId = motor.Id, Value = "true" } }, ElseActions = new() { new SetOutputAction { VariableId = motor.Id, Value = "false" } } };
        var artifact = new StructuredTextEmitter().Emit(new PlcProgramDefinition { Variables = new() { start, stop, guard, motor }, Logic = new LogicProgram { Rules = new() { rule } } });
        Assert.True(artifact.IsSupported);
        Assert.Contains("Start", artifact.Source);
        Assert.Contains("NOT (Stop)", artifact.Source);
        Assert.Contains("Guard", artifact.Source);
        Assert.Contains("Motor := TRUE", artifact.Source);
        Assert.Contains("ELSE Motor := FALSE", artifact.Source);
    }

    [Fact]
    public void ActionOnlyOutputRetainsPreviousValueWhenConditionBecomesFalse()
    {
        var condition = new VariableDefinition { Key = "Condition", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var output = new VariableDefinition { Key = "Output", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var rule = new LogicRule { Condition = new VariableExpression { VariableId = condition.Id }, Actions = new() { new SetOutputAction { VariableId = output.Id, Value = "true" } } };
        var artifact = new StructuredTextEmitter().Emit(new PlcProgramDefinition { Variables = new() { condition, output }, Logic = new LogicProgram { Rules = new() { rule } } });
        Assert.True(artifact.IsSupported);
        Assert.Contains("IF Condition THEN Output := TRUE; END_IF;", artifact.Source);
        Assert.Contains("retains previous value when FALSE", artifact.Source);
        Assert.DoesNotContain("ELSE Output := FALSE", artifact.Source);
    }

    [Fact]
    public void ElseOnlyAction_IsEmittedAndMatchesRuntimeBranch()
    {
        var condition = new VariableDefinition { Key = "Condition", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var output = new VariableDefinition { Key = "Output", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var rule = new LogicRule { Condition = new VariableExpression { VariableId = condition.Id }, ElseActions = new() { new SetOutputAction { VariableId = output.Id, Value = "true" } } };
        var artifact = new StructuredTextEmitter().Emit(new PlcProgramDefinition { Variables = new() { condition, output }, Logic = new LogicProgram { Rules = new() { rule } } });
        Assert.True(artifact.IsSupported);
        Assert.Contains("IF NOT (Condition) THEN Output := TRUE; END_IF;", artifact.Source);
        Assert.DoesNotContain("ELSE Output := FALSE", artifact.Source);
    }

    [Fact]
    public void SimultaneousWritersAreRejectedButMutuallyExclusiveWritersAreAllowed()
    {
        var input = new VariableDefinition { Key = "Input", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        var output = new VariableDefinition { Key = "Output", DataType = AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
        LogicRule Rule(ExpressionNode condition, string value) => new() { Condition = condition, Actions = new() { new SetOutputAction { VariableId = output.Id, Value = value } } };
        var concurrent = new StructuredTextEmitter().Emit(new PlcProgramDefinition { Variables = new() { input, output }, Logic = new LogicProgram { Rules = new() { Rule(new ConstantExpression { Value = "true" }, "true"), Rule(new ConstantExpression { Value = "true" }, "false") } } });
        Assert.False(concurrent.IsSupported);
        Assert.Contains(concurrent.Diagnostics, d => d.DiagnosticId == "ATLAS-ST-0004");
        var exclusive = new StructuredTextEmitter().Emit(new PlcProgramDefinition { Variables = new() { input, output }, Logic = new LogicProgram { Rules = new() { Rule(new VariableExpression { VariableId = input.Id }, "true"), Rule(new NotExpression { Operand = new VariableExpression { VariableId = input.Id } }, "false") } } });
        Assert.True(exclusive.IsSupported);
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
