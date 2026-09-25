using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Application.Logic;
using Xunit;
using FluentAssertions;

namespace AtlasSoftPlc.Application.Tests.Logic;

public class StGeneratorTests
{
    [Fact]
    public void Generate_SimpleLogic_ShouldProduceValidST()
    {
        var startId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var motorId = Guid.NewGuid();

        var ir = new AtlasIrDocument
        {
            Name = "ST Test",
            Variables = new List<VariableDefinition>
            {
                new() { Id = startId, Key = "Start", DataType = PlcDataType.Bool },
                new() { Id = stopId, Key = "Stop", DataType = PlcDataType.Bool },
                new() { Id = motorId, Key = "Motor", DataType = PlcDataType.Bool },
            },
            Logic = new LogicProgram
            {
                Rules = new List<LogicRule>
                {
                    new() { 
                        TargetVariableId = motorId, 
                        Expression = new AndNode(
                            new VariableNode(startId), 
                            new NotNode(new VariableNode(stopId))
                        ) 
                    }
                }
            }
        };

        var generator = new StGenerator();
        var stCode = generator.Generate(ir);

        stCode.Should().Contain("VAR");
        stCode.Should().Contain("Start : BOOL;");
        stCode.Should().Contain("Stop : BOOL;");
        stCode.Should().Contain("Motor : BOOL;");
        stCode.Should().Contain("END_VAR");
        stCode.Should().Contain("Motor := (Start AND NOT (Stop));");
    }
}
