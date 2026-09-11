using AtlasSoftPlc.Application.Logic;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Intent;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Infrastructure.Persistence;

namespace AtlasSoftPlc.Application.Tests;

public class LogicBuilderTests
{
    private static Dictionary<Guid, VariableDefinition> Vars()
    {
        var start = new VariableDefinition { Key = "Start", DisplayName = "Inicio", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var door = new VariableDefinition { Key = "DoorClosed", DisplayName = "Puerta cerrada", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var alarm = new VariableDefinition { Key = "Alarm", DisplayName = "Alarma", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var motor = new VariableDefinition { Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        return new Dictionary<Guid, VariableDefinition> { [start.Id] = start, [door.Id] = door, [alarm.Id] = alarm, [motor.Id] = motor };
    }

    [Fact]
    public void Build_ProducesProgramWithRules()
    {
        var vars = Vars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "DoorClosed", "not Alarm" },
            Actions = new List<string> { "Motor = true" },
            StopConditions = new List<string> { "not DoorClosed" },
            RawText = "enciende motor"
        };

        var program = builder.Build(intent);
        Assert.NotEmpty(program.Rules);
        Assert.Equal(2, program.Rules.Count); // rule + stop rule
    }

    [Fact]
    public void Build_StopRule_lowersOutputToFalse()
    {
        var vars = Vars();
        var builder = new LogicBuilder(vars);
        var intent = new AutomationIntent
        {
            Trigger = "Start",
            Conditions = new List<string> { "DoorClosed" },
            Actions = new List<string> { "Motor = true" },
            StopConditions = new List<string> { "not DoorClosed" }
        };

        var program = builder.Build(intent);
        var stopRule = program.Rules[1];
        var stopAction = Assert.IsType<SetOutputAction>(stopRule.Actions[0]);
        Assert.Equal("false", stopAction.Value);
    }
}

public class ValidationServiceTests
{
    [Fact]
    public void DuplicateKeys_ProduceError()
    {
        var v1 = new VariableDefinition { Key = "A", DisplayName = "A" };
        var v2 = new VariableDefinition { Key = "A", DisplayName = "A2" };
        var vars = new Dictionary<Guid, VariableDefinition> { [v1.Id] = v1, [v2.Id] = v2 };

        var svc = new ValidationService(new[] { new ReferencesValidationRule() });
        var report = svc.Validate(new LogicProgram(), vars);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "REF");
    }

    [Fact]
    public void MissingVariableReference_ProduceError()
    {
        var vars = new Dictionary<Guid, VariableDefinition>();
        var rule = new LogicRule
        {
            Name = "r",
            Condition = new VariableExpression { VariableId = Guid.NewGuid(), VariableKey = "Missing" },
            Actions = new List<LogicAction>()
        };

        var svc = new ValidationService(new[] { new ReferencesValidationRule() });
        var report = svc.Validate(new LogicProgram { Rules = new List<LogicRule> { rule } }, vars);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void SafetyCriticalOutput_ProducesWarning()
    {
        var outVar = new VariableDefinition { Key = "Motor", DisplayName = "Motor", Direction = VariableDirection.Output, SafetyCritical = true };
        var vars = new Dictionary<Guid, VariableDefinition> { [outVar.Id] = outVar };

        var svc = new ValidationService(new[] { new FailsafeValidationRule() });
        var report = svc.Validate(new LogicProgram(), vars);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Warning);
    }
}

public class SerializationTests
{
    [Fact]
    public void LogicProgram_RoundTrips_ThroughJson()
    {
        var program = new LogicProgram
        {
            Name = "Tank",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "fill",
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = Guid.NewGuid(), VariableKey = "Low" },
                            new NotExpression { Operand = new VariableExpression { VariableId = Guid.NewGuid(), VariableKey = "High" } }
                        }
                    },
                    Actions = new List<LogicAction>
                    {
                        new SetOutputAction { VariableId = Guid.NewGuid(), Value = "true" }
                    }
                }
            }
        };

        var json = AtlasJson.Serialize(program);
        var back = AtlasJson.Deserialize<LogicProgram>(json);

        Assert.NotNull(back);
        Assert.Equal(program.Name, back!.Name);
        Assert.Single(back.Rules);
        Assert.IsType<AndExpression>(back.Rules[0].Condition);
        var andExpr = (AndExpression)back.Rules[0].Condition!;
        Assert.IsType<NotExpression>(andExpr.Operands[1]);
        Assert.IsType<SetOutputAction>(back.Rules[0].Actions[0]);
    }

    [Fact]
    public void AllExpressionTypes_RoundTrip()
    {
        var expressions = new List<ExpressionNode>
        {
            new ConstantExpression { Value = "5", DataType = "Int32" },
            new CompareExpression { Left = new ConstantExpression { Value = "1" }, Right = new ConstantExpression { Value = "2" }, Operator = CompareOperator.GreaterThan },
            new ArithmeticExpression { Left = new ConstantExpression { Value = "1" }, Right = new ConstantExpression { Value = "2" }, Operator = ArithmeticOperator.Add },
            new EdgeExpression { Operand = new VariableExpression { VariableId = Guid.NewGuid() }, Kind = EdgeKind.RisingEdge },
            new TimerStateExpression { TimerId = Guid.NewGuid(), Field = "Done" },
            new CounterStateExpression { CounterId = Guid.NewGuid(), Field = "Current" }
        };

        foreach (var e in expressions)
        {
            var json = AtlasJson.Serialize(e);
            var back = AtlasJson.Deserialize<ExpressionNode>(json);
            Assert.NotNull(back);
            Assert.Equal(e.GetType(), back!.GetType());
        }
    }

    [Fact]
    public void AllActionTypes_RoundTrip()
    {
        var actions = new List<LogicAction>
        {
            new SetOutputAction { VariableId = Guid.NewGuid(), Value = "true" },
            new SetMemoryAction { VariableId = Guid.NewGuid(), Value = "42" },
            new ResetMemoryAction { VariableId = Guid.NewGuid() },
            new StartTimerAction { TimerId = Guid.NewGuid(), PresetMs = 1000 },
            new ResetTimerAction { TimerId = Guid.NewGuid() },
            new IncrementCounterAction { CounterId = Guid.NewGuid() },
            new ResetCounterAction { CounterId = Guid.NewGuid() },
            new RaiseAlarmAction { AlarmDefinitionId = Guid.NewGuid(), Message = "test" },
            new AcknowledgeAlarmAction { AlarmDefinitionId = Guid.NewGuid() },
            new LogEventAction { Message = "hello" }
        };

        foreach (var a in actions)
        {
            var json = AtlasJson.Serialize(a);
            var back = AtlasJson.Deserialize<LogicAction>(json);
            Assert.NotNull(back);
            Assert.Equal(a.GetType(), back!.GetType());
        }
    }
}

public class IntentParserTests
{
    [Fact]
    public void Parse_ExtractsAction_AndConditions()
    {
        var motor = new VariableDefinition { Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var lowSense = new VariableDefinition { Key = "LowLevel", DisplayName = "Nivel bajo", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor, [lowSense.Id] = lowSense };

        var parser = new IntentParser(vars);
        var intent = parser.Parse("Enciende el motor si nivel bajo", "operador");

        Assert.NotNull(intent);
        Assert.Contains(intent!.Actions, a => a.Contains("Motor"));
        Assert.Contains(intent.Conditions, c => c.Contains("bajo"));
    }

    [Fact]
    public void Parse_ReturnsNull_WhenNoActionDetected()
    {
        var motor = new VariableDefinition { Key = "Motor", DisplayName = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };
        var parser = new IntentParser(vars);
        Assert.Null(parser.Parse("hola mundo", "operador"));
    }
}