using System.Xml.Linq;
using System.Text;
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
        XNamespace plc = "http://www.plcopen.org/xml/tc6_0201";
        Assert.All(XDocument.Parse(first.Xml).Root!.Descendants(), element => Assert.Equal(plc, element.Name.Namespace));
        Assert.Contains("encoding=\"utf-8\"", first.Xml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first.Xml, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(first.Xml)));
        Assert.Equal("PLCopen XML candidate", first.Status);
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
