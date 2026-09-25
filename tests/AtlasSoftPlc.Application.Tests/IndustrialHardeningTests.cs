using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Simulation;
using AtlasSoftPlc.Application.Simulation;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Snapshots;

namespace AtlasSoftPlc.Application.Tests.Industrial;

public class IndustrialHardeningTests
{
    private AtlasIrDocument CreateSimplePumpIr()
    {
        var pumpId = Guid.NewGuid();
        var valveId = Guid.NewGuid();
        
        var ir = new AtlasIrDocument();
        ir.Variables.Add(new VariableDefinition { Id = pumpId, Key = "Pump", DataType = PlcDataType.Bool });
        ir.Variables.Add(new VariableDefinition { Id = valveId, Key = "Valve", DataType = PlcDataType.Bool });
        
        ir.Plant.Components.Add(new PlantComponent { 
            Name = "MainPump", 
            VariableId = pumpId, 
            Requires = new List<Guid> { valveId } 
        });
        
        ir.Logic.Rules.Add(new LogicRule { 
            Id = Guid.NewGuid(),
            Name = "PumpRule",
            Condition = new VariableExpression { VariableId = valveId },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        });
        
        return ir;
    }

    [Fact]
    public void Chaos_Seed_ShouldBeDeterministic()
    {
        var ir = CreateSimplePumpIr();
        var scenario = new SimulationScenario {
            Name = "DeterminismTest",
            Steps = new List<ScenarioStep> {
                new ScenarioStep { 
                    Description = "Step 1", 
                    SettleTimeMs = 50, 
                    InputOverrides = new Dictionary<Guid, bool> { { ir.Variables.First(v => v.Key == "Valve").Id, true } },
                    ExpectedOutputs = new Dictionary<Guid, bool> { { ir.Variables.First(v => v.Key == "Pump").Id, true } }
                }
            }
        };
        
        var chaos = new ChaosConfig { 
            PacketLossChance = 0.2, 
            JitterChance = 0.2, 
            Seed = 42 
        };
        
        var runner1 = new AtlasScenarioRunner(new ScanCoordinator());
        var res1 = runner1.Run(ir, scenario, chaos);
        
        var runner2 = new AtlasScenarioRunner(new ScanCoordinator());
        var res2 = runner2.Run(ir, scenario, chaos);
        
        Assert.Equal(res1.IsSuccess, res2.IsSuccess);
        Assert.Equal(res1.Errors, res2.Errors);
    }

    [Fact]
    public void Chaos_Latency_ShouldAffectSimulation()
    {
        var ir = CreateSimplePumpIr();
        var scenario = new SimulationScenario {
            Name = "LatencyTest",
            Steps = new List<ScenarioStep> {
                new ScenarioStep { 
                    Description = "Quick Start", 
                    SettleTimeMs = 5, // Very short settle time
                    InputOverrides = new Dictionary<Guid, bool> { { ir.Variables.First(v => v.Key == "Valve").Id, true } },
                    ExpectedOutputs = new Dictionary<Guid, bool> { { ir.Variables.First(v => v.Key == "Pump").Id, true } }
                }
            }
        };
        
        // With high latency, the pump shouldn't activate within 5ms
        var chaos = new ChaosConfig { LatencyMaxMs = 50 }; 
        var runner = new AtlasScenarioRunner(new ScanCoordinator());
        var res = runner.Run(ir, scenario, chaos);
        
        Assert.False(res.IsSuccess); // Should fail because pump didn't react in time
    }

    [Fact]
    public void Invariant_Violation_ShouldFail()
    {
        var ir = CreateSimplePumpIr();
        var scenario = new SimulationScenario {
            Name = "InvariantTest",
            Steps = new List<ScenarioStep> {
                new ScenarioStep { 
                    Description = "Safe Step", 
                    SettleTimeMs = 20, 
                    InputOverrides = new Dictionary<Guid, bool> { { ir.Variables.First(v => v.Key == "Valve").Id, true } },
                    ExpectedOutputs = new Dictionary<Guid, bool> { { ir.Variables.First(v => v.Key == "Pump").Id, true } }
                }
            },
            Invariants = new List<SimulationInvariant> {
                new SimulationInvariant { 
                    Condition = "FALSE", // Immediate violation
                    Description = "Must never be false" 
                }
            }
        };
        
        var runner = new AtlasScenarioRunner(new ScanCoordinator());
        var res = runner.Run(ir, scenario);
        
        Assert.False(res.IsSuccess);
        Assert.Contains(res.Errors, e => e.Contains("Invariante violada"));
    }

    [Fact]
    public void PhysicalShield_EvaluationError_ShouldFailClosed()
    {
        var ir = CreateSimplePumpIr();
        // Force an evaluation error by providing a corrupt IR condition (e.g. using a variable that doesn't exist in an expression)
        ir.Logic.Rules[0].Condition = new VariableExpression { VariableId = Guid.NewGuid() }; 
        
        var validator = new PhysicalConstraintValidationRule();
        var issues = validator.ValidateFull(ir).ToList();
        
        // Should produce a Blocker issue due to fail-closed on evaluation error
        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Blocker && i.Code == "PHYS");
    }
}
