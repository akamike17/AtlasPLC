using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Simulation;
using AtlasSoftPlc.Application.Simulation;
using AtlasSoftPlc.Runtime.Engine;
using Xunit;
using FluentAssertions;

namespace AtlasSoftPlc.Application.Tests.Simulation;

public class ScenarioRunnerTests
{
    [Fact]
    public void Run_SimpleLogic_ShouldPass()
    {
        // Lógica: Motor = Start AND NOT Stop
        var startId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var motorId = Guid.NewGuid();

        var ir = new AtlasIrDocument
        {
            Name = "Test Scenario",
            Variables = new List<VariableDefinition>
            {
                new() { Id = startId, Key = "Start", Type = PlcDataType.Boolean },
                new() { Id = stopId, Key = "Stop", Type = PlcDataType.Boolean },
                new() { Id = motorId, Key = "Motor", Type = PlcDataType.Boolean },
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

        var coordinator = new ScanCoordinator();
        var runner = new AtlasScenarioRunner(coordinator);
        var scenario = new SimulationScenario
        {
            Name = "Motor Logic Test",
            Steps = new List<ScenarioStep>
            {
                new() 
                { 
                    Description = "Start activated", 
                    InputOverrides = new Dictionary<Guid, bool> { { startId, true } }, 
                    ExpectedOutputs = new Dictionary<Guid, bool> { { motorId, true } } 
                },
                new() 
                { 
                    Description = "Stop activated", 
                    InputOverrides = new Dictionary<Guid, bool> { { stopId, true } }, 
                    ExpectedOutputs = new Dictionary<Guid, bool> { { motorId, false } } 
                }
            }
        };

        var result = runner.Run(ir, scenario);

        Assert.True(result.IsSuccess);
    }
}
