using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Application.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using Xunit;

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
                        Id = Guid.NewGuid(),
                        Name = "PumpRule",
                        Condition = new AndExpression { 
                            Operands = new List<ExpressionNode> { 
                                new VariableExpression { VariableId = startId, VariableKey = "Start" },
                                new NotExpression { Operand = new VariableExpression { VariableId = stopId, VariableKey = "Stop" } }
                            }
                        },
                        Actions = new List<LogicAction> { new SetOutputAction { VariableId = motorId, Value = "true" } }
                    }
                }
            }
        };

        var generator = new StGenerator();
        var stCode = generator.Generate(ir);

        Assert.Contains("VAR", stCode);
        Assert.Contains("Start : BOOL;", stCode);
        Assert.Contains("Stop : BOOL;", stCode);
        Assert.Contains("Motor : BOOL;", stCode);
        Assert.Contains("END_VAR", stCode);
        Assert.Contains("Motor := Start AND NOT (Stop);", stCode);
    }
}
