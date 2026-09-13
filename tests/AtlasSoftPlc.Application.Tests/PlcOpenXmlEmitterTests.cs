using System.Xml.Linq;
using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Tests;

public sealed class PlcOpenXmlEmitterTests
{
    [Fact]
    public void SameProgramProducesIdenticalNormalizedXmlAndHash()
    {
        var program = AtlasIrFixtures.BuildMotorStopGuard().ToProgramDefinition();
        var emitter = new PlcOpenXmlEmitter();
        var first = emitter.Emit(program);
        var second = emitter.Emit(program);
        Assert.True(first.IsSupported);
        Assert.Equal(first.Xml, second.Xml);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal("project", XDocument.Parse(first.Xml).Root!.Name.LocalName);
        Assert.Contains("AtlasProgram", first.Xml);
    }

    [Fact]
    public void UnsupportedIrIsRejectedBeforeXmlIsPresented()
    {
        var program = new PlcProgramDefinition { Name = "unsupported", Logic = new LogicProgram { Rules = new() { new() { Condition = new TimerStateExpression(), Actions = new() { new SetOutputAction() } } } } };
        var artifact = new PlcOpenXmlEmitter().Emit(program);
        Assert.False(artifact.IsSupported);
        Assert.Empty(artifact.Xml);
        Assert.NotEmpty(artifact.Diagnostics);
    }
}
