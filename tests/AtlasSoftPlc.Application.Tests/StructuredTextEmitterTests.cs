using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Tests;

public sealed class StructuredTextEmitterTests
{
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
