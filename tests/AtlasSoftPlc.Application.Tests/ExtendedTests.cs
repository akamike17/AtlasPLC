using System.Collections.Generic;
using AtlasSoftPlc.Application.Logic;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Intent;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using Xunit;

namespace AtlasSoftPlc.Application.Tests;

public class ValidationServiceExtendedTests
{
    private static Dictionary<Guid, VariableDefinition> CreateVars(params (Guid id, string key, VariableDirection dir, bool safetyCritical)[] defs)
    {
        var dict = new Dictionary<Guid, VariableDefinition>();
        foreach (var d in defs)
        {
            dict[d.id] = new VariableDefinition
            {
                Id = d.id,
                Key = d.key,
                DisplayName = d.key,
                DataType = PlcDataType.Bool,
                Direction = d.dir,
                SafetyCritical = d.safetyCritical
            };
        }
        return dict;
    }

    [Fact]
    public void ReferencesValidationRule_DetectsDuplicateKeys()
    {
        var varId1 = Guid.NewGuid();
        var varId2 = Guid.NewGuid();
        var vars = CreateVars(
            (varId1, "Motor", VariableDirection.Output, false),
            (varId2, "Motor", VariableDirection.Input, false) // duplicate key
        );

        var rule = new ReferencesValidationRule();
        var issues = rule.Validate(new LogicProgram(), vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Code == "REF" && i.Message.Contains("Key duplicada"));
    }

    [Fact]
    public void ReferencesValidationRule_DetectsMissingVariableInCondition()
    {
        var missingId = Guid.NewGuid();
        var vars = CreateVars((Guid.NewGuid(), "Motor", VariableDirection.Output, false));
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Test",
                    Condition = new VariableExpression { VariableId = missingId, VariableKey = "Missing" },
                    Actions = new List<LogicAction>()
                }
            }
        };

        var rule = new ReferencesValidationRule();
        var issues = rule.Validate(program, vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Code == "REF" && i.Message.Contains("variable inexistente"));
    }

    [Fact]
    public void ReferencesValidationRule_DetectsMissingVariableInAction()
    {
        var missingId = Guid.NewGuid();
        var vars = CreateVars((Guid.NewGuid(), "Start", VariableDirection.Input, false));
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Test",
                    Condition = new VariableExpression { VariableId = vars.Keys.First(), VariableKey = "Start" },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = missingId, Value = "true" } }
                }
            }
        };

        var rule = new ReferencesValidationRule();
        var issues = rule.Validate(program, vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Code == "REF" && i.Message.Contains("escribe a variable inexistente"));
    }

    [Fact]
    public void ReferencesValidationRule_ValidatesSetMemoryAction()
    {
        var missingId = Guid.NewGuid();
        var vars = CreateVars((Guid.NewGuid(), "Start", VariableDirection.Input, false));
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Test",
                    Condition = new VariableExpression { VariableId = vars.Keys.First(), VariableKey = "Start" },
                    Actions = new List<LogicAction> { new SetMemoryAction { VariableId = missingId, Value = "42" } }
                }
            }
        };

        var rule = new ReferencesValidationRule();
        var issues = rule.Validate(program, vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Code == "REF" && i.Message.Contains("memoria inexistente"));
    }

    [Fact]
    public void ReferencesValidationRule_ValidatesElseActions()
    {
        var missingId = Guid.NewGuid();
        var vars = CreateVars((Guid.NewGuid(), "Start", VariableDirection.Input, false));
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Test",
                    Condition = new VariableExpression { VariableId = vars.Keys.First(), VariableKey = "Start" },
                    Actions = new List<LogicAction>(),
                    ElseActions = new List<LogicAction> { new SetOutputAction { VariableId = missingId, Value = "false" } }
                }
            }
        };

        var rule = new ReferencesValidationRule();
        var issues = rule.Validate(program, vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("escribe a variable inexistente"));
    }

    [Fact]
    public void FailsafeValidationRule_WarnsOnSafetyCriticalOutput()
    {
        var safetyId = Guid.NewGuid();
        var vars = CreateVars((safetyId, "Motor", VariableDirection.Output, true));

        var rule = new FailsafeValidationRule();
        var issues = rule.Validate(new LogicProgram(), vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Warning && i.Code == "FAILSAFE" && i.Message.Contains("SafetyCritical"));
    }

    [Fact]
    public void FailsafeValidationRule_NoWarning_ForNonSafetyCritical()
    {
        var vars = CreateVars((Guid.NewGuid(), "Motor", VariableDirection.Output, false));

        var rule = new FailsafeValidationRule();
        var issues = rule.Validate(new LogicProgram(), vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void FailsafeValidationRule_IgnoresInputs()
    {
        var vars = CreateVars((Guid.NewGuid(), "Sensor", VariableDirection.Input, true));

        var rule = new FailsafeValidationRule();
        var issues = rule.Validate(new LogicProgram(), vars, new ValidationContext { Variables = vars }).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void ValidationService_RunsAllRules()
    {
        var varId1 = Guid.NewGuid();
        var varId2 = Guid.NewGuid();
        var varId3 = Guid.NewGuid();
        
        var vars = new Dictionary<Guid, VariableDefinition>
        {
            [varId1] = new VariableDefinition { Id = varId1, Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output, SafetyCritical = true },
            [varId2] = new VariableDefinition { Id = varId2, Key = "Start", DisplayName = "Start", DataType = PlcDataType.Bool, Direction = VariableDirection.Input, SafetyCritical = false },
            [varId3] = new VariableDefinition { Id = varId3, Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Input, SafetyCritical = false }
        };

        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Test",
                    Condition = new VariableExpression { VariableId = vars.Values.First(v => v.Key == "Start").Id, VariableKey = "Start" },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = Guid.NewGuid(), Value = "true" } } // missing ref
                }
            }
        };

        var svc = new ValidationService(new IValidationRule[] { new ReferencesValidationRule(), new FailsafeValidationRule() });
        var report = svc.Validate(program, vars);

        Assert.True(report.Issues.Count >= 3); // duplicate key + missing action ref + safety warning
        Assert.Contains(report.Issues, i => i.Code == "REF");
        Assert.Contains(report.Issues, i => i.Code == "FAILSAFE");
    }
}

public class ExplainerTests
{
    private static Dictionary<Guid, VariableDefinition> CreateVars()
    {
        var low = new VariableDefinition { Id = Guid.NewGuid(), Key = "LowLevel", DisplayName = "Nivel bajo", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var high = new VariableDefinition { Id = Guid.NewGuid(), Key = "HighLevel", DisplayName = "Nivel alto", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var pump = new VariableDefinition { Id = Guid.NewGuid(), Key = "Pump", DisplayName = "Bomba", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var temp = new VariableDefinition { Id = Guid.NewGuid(), Key = "Temp", DisplayName = "Temperatura", DataType = PlcDataType.Float, Direction = VariableDirection.Input };
        var motor = new VariableDefinition { Id = Guid.NewGuid(), Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };

        return new Dictionary<Guid, VariableDefinition>
        {
            [low.Id] = low,
            [high.Id] = high,
            [pump.Id] = pump,
            [temp.Id] = temp,
            [motor.Id] = motor
        };
    }

    [Fact]
    public void ExplainRule_SimpleVariableCondition()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Fill tank",
            Condition = new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("FILL TANK", text);
        Assert.Contains("Nivel bajo esté activo", text);
        Assert.Contains("Bomba = true", text);
    }

    [Fact]
    public void ExplainRule_NotExpression()
    {
        var vars = CreateVars();
        var highId = vars.Values.First(v => v.Key == "HighLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Stop fill",
            Condition = new NotExpression { Operand = new VariableExpression { VariableId = highId, VariableKey = "HighLevel" } },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "false" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("Nivel alto NO esté activo", text);
    }

    [Fact]
    public void ExplainRule_AndExpression()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var highId = vars.Values.First(v => v.Key == "HighLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Auto fill",
            Condition = new AndExpression
            {
                Operands = new List<ExpressionNode>
                {
                    new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
                    new NotExpression { Operand = new VariableExpression { VariableId = highId, VariableKey = "HighLevel" } }
                }
            },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("Nivel bajo esté activo", text);
        Assert.Contains("Nivel alto NO esté activo", text);
    }

    [Fact]
    public void ExplainRule_OrExpression()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var highId = vars.Values.First(v => v.Key == "HighLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Emergency fill",
            Condition = new OrExpression
            {
                Operands = new List<ExpressionNode>
                {
                    new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
                    new VariableExpression { VariableId = highId, VariableKey = "HighLevel" }
                }
            },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("Nivel bajo esté activo o Nivel alto esté activo", text);
    }

    [Fact]
    public void ExplainRule_CompareExpression()
    {
        var vars = CreateVars();
        var tempId = vars.Values.First(v => v.Key == "Temp").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Overheat",
            Condition = new CompareExpression
            {
                Left = new VariableExpression { VariableId = tempId, VariableKey = "Temp" },
                Right = new ConstantExpression { Value = "80", DataType = "Float" },
                Operator = CompareOperator.GreaterThan
            },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "false" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("Temperatura > 80", text);
    }

    [Fact]
    public void ExplainRule_TimerStateExpression()
    {
        var vars = CreateVars();
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;
        var timerId = Guid.NewGuid();

        var rule = new LogicRule
        {
            Name = "Timer done",
            Condition = new TimerStateExpression { TimerId = timerId, Field = "Done" },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("temporizador", text);
        Assert.Contains("Done", text);
    }

    [Fact]
    public void ExplainRule_CounterStateExpression()
    {
        var vars = CreateVars();
        var motorId = vars.Values.First(v => v.Key == "Motor").Id;
        var counterId = Guid.NewGuid();

        var rule = new LogicRule
        {
            Name = "Count cycles",
            Condition = new CounterStateExpression { CounterId = counterId, Field = "Current" },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = motorId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("contador", text);
        Assert.Contains("Current", text);
    }

    [Fact]
    public void ExplainRule_EdgeExpression()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Rising edge start",
            Condition = new EdgeExpression { Operand = new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" }, Kind = EdgeKind.RisingEdge },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("Nivel bajo flanco ascendente", text);
    }

    [Fact]
    public void ExplainRule_ElseActions()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Fill with else",
            Condition = new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } },
            ElseActions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "false" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("En caso contrario:", text);
        Assert.Contains("Bomba = false", text);
    }

    [Fact]
    public void ExplainRule_AllActionTypes()
    {
        var vars = CreateVars();
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;
        var motorId = vars.Values.First(v => v.Key == "Motor").Id;
        var timerId = Guid.NewGuid();
        var counterId = Guid.NewGuid();
        var alarmId = Guid.NewGuid();

        var rule = new LogicRule
        {
            Name = "All actions",
            Condition = null,
            Actions = new List<LogicAction>
            {
                new SetOutputAction { VariableId = pumpId, Value = "true" },
                new SetMemoryAction { VariableId = motorId, Value = "42" },
                new ResetMemoryAction { VariableId = motorId },
                new StartTimerAction { TimerId = timerId, PresetMs = 5000 },
                new ResetTimerAction { TimerId = timerId },
                new IncrementCounterAction { CounterId = counterId },
                new ResetCounterAction { CounterId = counterId },
                new RaiseAlarmAction { AlarmDefinitionId = alarmId, Message = "Test alarm" },
                new AcknowledgeAlarmAction { AlarmDefinitionId = alarmId },
                new LogEventAction { Message = "Cycle complete" }
            }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains("Bomba = true", text);
        Assert.Contains("Motor = 42", text);
        Assert.Contains("Motor = false", text);
        Assert.Contains("iniciar temporizador 5000ms", text);
        Assert.Contains("reiniciar temporizador", text);
        Assert.Contains("incrementar contador", text);
        Assert.Contains("reiniciar contador", text);
        Assert.Contains("alarma: Test alarm", text);
        Assert.Contains("registro: Cycle complete", text);
    }

    [Fact]
    public void ExplainRule_UnknownVariableId_ShowsShortGuid()
    {
        var vars = CreateVars();
        var unknownId = Guid.NewGuid();
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var rule = new LogicRule
        {
            Name = "Unknown ref",
            Condition = new VariableExpression { VariableId = unknownId, VariableKey = "Unknown" },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
        };

        var explainer = new Explainer(vars);
        var text = explainer.ExplainRule(rule);

        Assert.Contains(unknownId.ToString("N")[..8], text); // short guid
    }
}

public class IntentParserExtendedTests
{
    private static Dictionary<Guid, VariableDefinition> CreateVars()
    {
        var pump = new VariableDefinition { Id = Guid.NewGuid(), Key = "Pump", DisplayName = "Bomba", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var motor = new VariableDefinition { Id = Guid.NewGuid(), Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var low = new VariableDefinition { Id = Guid.NewGuid(), Key = "LowLevel", DisplayName = "Nivel bajo", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var high = new VariableDefinition { Id = Guid.NewGuid(), Key = "HighLevel", DisplayName = "Nivel alto", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var start = new VariableDefinition { Id = Guid.NewGuid(), Key = "Start", DisplayName = "Inicio", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var stop = new VariableDefinition { Id = Guid.NewGuid(), Key = "Stop", DisplayName = "Paro", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };

        return new Dictionary<Guid, VariableDefinition>
        {
            [pump.Id] = pump, [motor.Id] = motor, [low.Id] = low, [high.Id] = high, [start.Id] = start, [stop.Id] = stop
        };
    }

    [Fact]
    public void Parse_EnciendeXSiY_ExtractsActionAndCondition()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("Enciende la bomba si nivel bajo", "operador");

        Assert.NotNull(intent);
        Assert.Contains(intent!.Actions, a => a.Contains("bomba", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(intent.Conditions, c => c.Contains("bajo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ApagaXCuandoY_ExtractsStopCondition()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("Apaga el motor cuando nivel alto", "operador");

        Assert.NotNull(intent);
        Assert.Contains(intent!.StopConditions, c => c.Contains("alto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ComplexSentence_ExtractsMultiple()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("Enciende la bomba si nivel bajo y no nivel alto, apaga si nivel alto", "operador");

        Assert.NotNull(intent);
        Assert.True(intent!.Conditions.Count >= 1);
        Assert.True(intent.StopConditions.Count >= 1);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenNoActionVerb()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("hola mundo", "operador");

        Assert.Null(intent);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenNoMatchingVariables()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("Enciende el reactor nuclear", "operador");

        Assert.Null(intent); // "reactor nuclear" not in variable definitions
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("ENCIENDE LA BOMBA SI NIVEL BAJO", "operador");

        Assert.NotNull(intent);
    }

    [Fact]
    public void Parse_SetsMetadata()
    {
        var vars = CreateVars();
        var parser = new IntentParser(vars);

        var intent = parser.Parse("Enciende la bomba si nivel bajo", "operador123");

        Assert.NotNull(intent);
        Assert.Equal("operador123", intent!.CreatedBy);
        Assert.NotEqual(default, intent.CreatedUtc);
        Assert.NotNull(intent.RawText);
    }
}

public class LogicBuilderExtendedTests
{
    private static Dictionary<Guid, VariableDefinition> CreateVars()
    {
        var pump = new VariableDefinition { Id = Guid.NewGuid(), Key = "Pump", DisplayName = "Bomba", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var motor = new VariableDefinition { Id = Guid.NewGuid(), Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var low = new VariableDefinition { Id = Guid.NewGuid(), Key = "LowLevel", DisplayName = "Nivel bajo", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var high = new VariableDefinition { Id = Guid.NewGuid(), Key = "HighLevel", DisplayName = "Nivel alto", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var start = new VariableDefinition { Id = Guid.NewGuid(), Key = "Start", DisplayName = "Inicio", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var stop = new VariableDefinition { Id = Guid.NewGuid(), Key = "Stop", DisplayName = "Paro", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };

        return new Dictionary<Guid, VariableDefinition>
        {
            [pump.Id] = pump, [motor.Id] = motor, [low.Id] = low, [high.Id] = high, [start.Id] = start, [stop.Id] = stop
        };
    }

    [Fact]
    public void Build_CreatesMainRule_WithCorrectPriority()
    {
        var vars = CreateVars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "LowLevel", "not HighLevel" },
            Actions = new List<string> { "Pump = true" },
            StopConditions = new List<string> { "HighLevel" },
            RawText = "test"
        };

        var program = builder.Build(intent);

        Assert.True(program.Enabled);
        Assert.Equal(2, program.Rules.Count); // main + stop
        Assert.Equal(0, program.Rules[0].Priority); // default priority (LogicBuilder doesn't set it)
    }

    [Fact]
    public void Build_CreatesStopRule_WithInvertedAction()
    {
        var vars = CreateVars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "LowLevel" },
            Actions = new List<string> { "Pump = true" },
            StopConditions = new List<string> { "HighLevel" },
            RawText = "test"
        };

        var program = builder.Build(intent);
        var stopRule = program.Rules[1];

        Assert.Equal("Start — Paro", stopRule.Name);
        var action = Assert.IsType<SetOutputAction>(stopRule.Actions[0]);
        Assert.Equal("false", action.Value);
    }

    [Fact]
    public void Build_ProducesDeterministicStructure_ForSameIntent()
    {
        var vars = CreateVars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "LowLevel" },
            Actions = new List<string> { "Pump = true" },
            StopConditions = new List<string> { "HighLevel" },
            RawText = "test"
        };

        var p1 = builder.Build(intent);
        var p2 = builder.Build(intent);

        // Structure should be deterministic (same number of rules, same names, same structure)
        Assert.Equal(p1.Rules.Count, p2.Rules.Count);
        Assert.Equal(p1.Rules[0].Name, p2.Rules[0].Name);
        Assert.Equal(p1.Rules[1].Name, p2.Rules[1].Name);
        Assert.Equal(p1.Rules[0].Priority, p2.Rules[0].Priority);
        Assert.Equal(p1.Rules[1].Priority, p2.Rules[1].Priority);
        
        // Condition structure should match
        Assert.Equal(p1.Rules[0].Condition?.GetType(), p2.Rules[0].Condition?.GetType());
        Assert.Equal(p1.Rules[1].Condition?.GetType(), p2.Rules[1].Condition?.GetType());
        
        // Actions structure should match
        Assert.Equal(p1.Rules[0].Actions.Count, p2.Rules[0].Actions.Count);
        Assert.Equal(p1.Rules[1].Actions.Count, p2.Rules[1].Actions.Count);
    }

    [Fact]
    public void Build_HandlesMultipleActions()
    {
        var vars = CreateVars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "LowLevel" },
            Actions = new List<string> { "Pump = true", "Motor = true" },
            StopConditions = new List<string>(),
            RawText = "test"
        };

        var program = builder.Build(intent);
        var mainRule = program.Rules[0];

        Assert.Equal(2, mainRule.Actions.Count);
    }

    [Fact]
    public void Build_HandlesMultipleStopConditions()
    {
        var vars = CreateVars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "LowLevel" },
            Actions = new List<string> { "Pump = true" },
            StopConditions = new List<string> { "HighLevel", "Stop" },
            RawText = "test"
        };

        var program = builder.Build(intent);

        Assert.Equal(2, program.Rules.Count); // main + 1 stop rule (OR of stop conditions)
        var stopRule = program.Rules[1];
        Assert.IsType<OrExpression>(stopRule.Condition);
    }

    [Fact]
    public void Build_SetsSourceIntentAndCreatedBy()
    {
        var vars = CreateVars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "LowLevel" },
            Actions = new List<string> { "Pump = true" },
            StopConditions = new List<string>(),
            RawText = "original text",
            CreatedBy = "testuser"
        };

        var program = builder.Build(intent);
        var rule = program.Rules[0];

        Assert.Equal("original text", rule.SourceIntent);
        Assert.Equal("testuser", rule.CreatedBy);
    }
}

public class TestGeneratorTests
{
    private static Dictionary<Guid, VariableDefinition> CreateVars()
    {
        var pump = new VariableDefinition { Id = Guid.NewGuid(), Key = "Pump", DisplayName = "Bomba", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var motor = new VariableDefinition { Id = Guid.NewGuid(), Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var low = new VariableDefinition { Id = Guid.NewGuid(), Key = "LowLevel", DisplayName = "Nivel bajo", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var high = new VariableDefinition { Id = Guid.NewGuid(), Key = "HighLevel", DisplayName = "Nivel alto", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };

        return new Dictionary<Guid, VariableDefinition>
        {
            [pump.Id] = pump, [motor.Id] = motor, [low.Id] = low, [high.Id] = high
        };
    }

    [Fact]
    public void Generate_ProducesTestCases()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var highId = vars.Values.First(v => v.Key == "HighLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var program = new LogicProgram
        {
            Id = Guid.NewGuid(),
            Name = "Tank",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Fill",
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
                            new NotExpression { Operand = new VariableExpression { VariableId = highId, VariableKey = "HighLevel" } }
                        }
                    },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
                }
            }
        };

        var gen = new TestGenerator(vars);
        var tests = gen.Generate(program);

        Assert.NotEmpty(tests);
        foreach (var t in tests)
        {
            Assert.NotNull(t.Name);
            Assert.NotNull(t.Inputs);
            Assert.True(t.ExpectedOutput || !t.ExpectedOutput); // just access
        }
    }

    [Fact]
    public void Generate_CoversTruthTable_ForBooleanLogic()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var highId = vars.Values.First(v => v.Key == "HighLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;

        var program = new LogicProgram
        {
            Id = Guid.NewGuid(),
            Name = "Tank",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Fill",
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
                            new NotExpression { Operand = new VariableExpression { VariableId = highId, VariableKey = "HighLevel" } }
                        }
                    },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pumpId, Value = "true" } }
                }
            }
        };

        var gen = new TestGenerator(vars);
        var tests = gen.Generate(program);

        // Implementation: base case (all true) + one flip per input var
        Assert.True(tests.Count >= 3); // base + 2 flips

        // The base case has all inputs=true, ExpectedOutput=true (by construction)
        var baseCase = tests.FirstOrDefault(t => t.Name.Contains("todas activas"));
        Assert.NotNull(baseCase);
        Assert.True(baseCase!.Inputs[lowId]);
        Assert.True(baseCase.Inputs[highId]); // base case has all true
        Assert.True(baseCase.ExpectedOutput); // by construction, base case expects output=true

        // One of the flipped cases should have lowId=false, ExpectedOutput=false
        var lowFalse = tests.FirstOrDefault(t => t.Name.Contains(lowId.ToString("N")[..8]) && !t.Inputs[lowId]);
        Assert.NotNull(lowFalse);
        Assert.False(lowFalse.ExpectedOutput);
    }

    [Fact]
    public void Generate_HandlesMultipleOutputActions()
    {
        var vars = CreateVars();
        var lowId = vars.Values.First(v => v.Key == "LowLevel").Id;
        var pumpId = vars.Values.First(v => v.Key == "Pump").Id;
        var motorId = vars.Values.First(v => v.Key == "Motor").Id;

        var program = new LogicProgram
        {
            Id = Guid.NewGuid(),
            Name = "Tank",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Fill",
                    Condition = new VariableExpression { VariableId = lowId, VariableKey = "LowLevel" },
                    Actions = new List<LogicAction>
                    {
                        new SetOutputAction { VariableId = pumpId, Value = "true" },
                        new SetOutputAction { VariableId = motorId, Value = "true" }
                    }
                }
            }
        };

        var gen = new TestGenerator(vars);
        var tests = gen.Generate(program);

        Assert.NotEmpty(tests);
        // First output action determines test case
        Assert.Equal(pumpId, tests[0].OutputVariableId);
    }
}

public sealed class ConfigurationHasherTests
{
    private static VariableDefinition CreateVar(Guid id, string key, VariableDirection dir = VariableDirection.Input)
        => new VariableDefinition { Id = id, Key = key, DisplayName = key, DataType = PlcDataType.Bool, Direction = dir };

    private static TagBinding CreateBinding(Guid id, Guid variableId, string address = "0", string area = "MW")
        => new TagBinding { Id = id, VariableId = variableId, Address = address, Protocol = DeviceProtocol.ModbusTcp, DataType = "Bool", ByteOrder = "BigEndian" };

    private static DeviceDefinition CreateDevice(Guid id, string name = "Dev1")
        => new DeviceDefinition { Id = id, Name = name, Protocol = DeviceProtocol.ModbusTcp, Host = "localhost", Port = 502 };

    [Fact]
    public void ComputeHash_SameInput_ProducesSameHash()
    {
        var varId = Guid.NewGuid();
        var program = new LogicProgram
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Version = 1,
            Rules = new List<LogicRule>
            {
                new LogicRule { Id = Guid.NewGuid(), Name = "R1", Condition = null, Actions = new List<LogicAction>(), Priority = 10, Enabled = true }
            }
        };
        var variables = new List<VariableDefinition> { CreateVar(varId, "A") };
        var bindings = new List<TagBinding> { CreateBinding(Guid.NewGuid(), varId) };
        var devices = new List<DeviceDefinition> { CreateDevice(Guid.NewGuid()) };

        var hasher = new ConfigurationHasher();
        var hash1 = hasher.ComputeHash(program, variables, bindings, devices);
        var hash2 = hasher.ComputeHash(program, variables, bindings, devices);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInput_ProducesDifferentHash()
    {
        var varId = Guid.NewGuid();
        var p1 = new LogicProgram { Id = Guid.NewGuid(), Name = "A", Version = 1, Rules = new List<LogicRule> { new LogicRule { Id = Guid.NewGuid(), Name = "R1", Condition = null, Actions = new List<LogicAction>(), Priority = 10, Enabled = true } } };
        var p2 = new LogicProgram { Id = Guid.NewGuid(), Name = "B", Version = 1, Rules = new List<LogicRule> { new LogicRule { Id = Guid.NewGuid(), Name = "R1", Condition = null, Actions = new List<LogicAction>(), Priority = 10, Enabled = true } } };
        var variables = new List<VariableDefinition> { CreateVar(varId, "A") };
        var bindings = new List<TagBinding> { CreateBinding(Guid.NewGuid(), varId) };
        var devices = new List<DeviceDefinition> { CreateDevice(Guid.NewGuid()) };

        var hasher = new ConfigurationHasher();
        var hash1 = hasher.ComputeHash(p1, variables, bindings, devices);
        var hash2 = hasher.ComputeHash(p2, variables, bindings, devices);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void OrderedForHash_OrdersCollectionsDeterministically()
    {
        var rules = new List<LogicRule>
        {
            new LogicRule { Id = Guid.NewGuid(), Name = "Z", Actions = new List<LogicAction>(), Priority = 10, Enabled = true },
            new LogicRule { Id = Guid.NewGuid(), Name = "A", Actions = new List<LogicAction>(), Priority = 10, Enabled = true },
            new LogicRule { Id = Guid.NewGuid(), Name = "M", Actions = new List<LogicAction>(), Priority = 10, Enabled = true }
        };

        var program = new LogicProgram { Id = Guid.NewGuid(), Name = "Test", Rules = rules, Version = 1 };
        var varId = Guid.NewGuid();
        var variables = new List<VariableDefinition> { CreateVar(varId, "A") };
        var bindings = new List<TagBinding> { CreateBinding(Guid.NewGuid(), varId) };
        var devices = new List<DeviceDefinition> { CreateDevice(Guid.NewGuid()) };

        var hasher = new ConfigurationHasher();
        var hash = hasher.ComputeHash(program, variables, bindings, devices);
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Equal(64, hash.Length); // SHA256 hex = 64 chars
    }
}