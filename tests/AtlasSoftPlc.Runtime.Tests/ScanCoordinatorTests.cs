using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Snapshots;

namespace AtlasSoftPlc.Runtime.Tests;

public class ScanCoordinatorTests
{
    private static (Guid start, Guid door, Guid alarm, Guid motor) BuildMotorModel(
        out Dictionary<Guid, VariableDefinition> defs)
    {
        defs = new Dictionary<Guid, VariableDefinition>();
        var start = Guid.NewGuid();
        var door = Guid.NewGuid();
        var alarm = Guid.NewGuid();
        var motor = Guid.NewGuid();

        defs[start] = new VariableDefinition { Id = start, Key = "Start", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        defs[door] = new VariableDefinition { Id = door, Key = "DoorClosed", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        defs[alarm] = new VariableDefinition { Id = alarm, Key = "Alarm", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        defs[motor] = new VariableDefinition { Id = motor, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output };

        return (start, door, alarm, motor);
    }

    private static LogicProgram MotorProgram(Guid start, Guid door, Guid alarm, Guid motor)
    {
        var rule = new LogicRule
        {
            Name = "Arranque motor",
            Priority = 100,
            Condition = new AndExpression
            {
                Operands = new List<ExpressionNode>
                {
                    new VariableExpression { VariableId = start, VariableKey = "Start" },
                    new VariableExpression { VariableId = door, VariableKey = "DoorClosed" },
                    new NotExpression { Operand = new VariableExpression { VariableId = alarm, VariableKey = "Alarm" } }
                }
            },
            Actions = new List<LogicAction>
            {
                new SetOutputAction { VariableId = motor, Value = "true" }
            }
        };
        return new LogicProgram { Name = "Motor", Rules = new List<LogicRule> { rule } };
    }

    private static Dictionary<Guid, RuntimeValue> Inputs(bool start, bool door, bool alarm, Guid s, Guid d, Guid a)
    {
        return new Dictionary<Guid, RuntimeValue>
        {
            [s] = new RuntimeValue { VariableId = s, Value = PlcValue.Bool(start), Quality = Quality.Good },
            [d] = new RuntimeValue { VariableId = d, Value = PlcValue.Bool(door), Quality = Quality.Good },
            [a] = new RuntimeValue { VariableId = a, Value = PlcValue.Bool(alarm), Quality = Quality.Good },
        };
    }

    [Fact]
    public void Scan_WithScanRequest_WorksSameAsOverload()
    {
        var (s, d, a, m) = BuildMotorModel(out var defs);
        var program = MotorProgram(s, d, a, m);
        var inputs = Inputs(true, true, false, s, d, a);
        var memory = new Dictionary<Guid, RuntimeValue>();
        var interlocks = new List<Interlock>();
        var failsafe = new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) };
        var outputTypes = new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool };

        // Vía ScanRequest (nuevo)
        var request = ScanRequest.Create(program, defs, inputs, memory, interlocks, failsafe, outputTypes, 50);
        var r1 = new ScanCoordinator().Scan(request);

        // Vía overload de compatibilidad (viejo)
        var r2 = new ScanCoordinator().Scan(program, defs, inputs, memory, interlocks, failsafe, outputTypes, 50);

        Assert.Equal(r1.Outputs.Get(m)!.Value.AsBool(), r2.Outputs.Get(m)!.Value.AsBool());
        Assert.True(r1.Outputs.Get(m)!.Value.AsBool());
    }

    [Fact]
    public void ScanRequest_PreservesAllFields()
    {
        var (s, d, a, m) = BuildMotorModel(out var defs);
        var program = MotorProgram(s, d, a, m);
        var inputs = Inputs(true, true, false, s, d, a);
        var memory = new Dictionary<Guid, RuntimeValue>();
        var interlocks = new List<Interlock>();
        var failsafe = new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) };
        var outputTypes = new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool };

        var request = ScanRequest.Create(program, defs, inputs, memory, interlocks, failsafe, outputTypes, 50);

        Assert.Same(program, request.Program);
        Assert.Same(defs, request.Definitions);
        Assert.Same(inputs, request.Inputs);
        Assert.Same(memory, request.Memory);
        Assert.Same(interlocks, request.Interlocks);
        Assert.Same(failsafe, request.FailsafeValues);
        Assert.Same(outputTypes, request.OutputTypes);
        Assert.Equal(50, request.DeltaMs);
    }

    [Fact]
    public void Motor_On_WhenAllConditionsMet()
    {
        var (s, d, a, m) = BuildMotorModel(out var defs);
        var coord = new ScanCoordinator();
        var program = MotorProgram(s, d, a, m);

        var result = coord.Scan(
            program, defs,
            Inputs(true, true, false, s, d, a),
            new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
            50);

        Assert.False(result.HasErrors);
        var motor = result.Outputs.Get(m);
        Assert.NotNull(motor);
        Assert.True(motor!.Value.AsBool());
    }

    [Fact]
    public void Motor_Off_WhenDoorOpen()
    {
        var (s, d, a, m) = BuildMotorModel(out var defs);
        var coord = new ScanCoordinator();
        var program = MotorProgram(s, d, a, m);

        var result = coord.Scan(
            program, defs,
            Inputs(true, false, false, s, d, a),
            new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
            50);

        Assert.NotNull(result.Outputs.Get(m));
        Assert.False(result.Outputs.Get(m)!.Value.AsBool()); // sin propuesta: failsafe (P1-1)
    }

    [Fact]
    public void Motor_Off_WhenAlarmActive()
    {
        var (s, d, a, m) = BuildMotorModel(out var defs);
        var coord = new ScanCoordinator();
        var program = MotorProgram(s, d, a, m);

        var result = coord.Scan(
            program, defs,
            Inputs(true, true, true, s, d, a),
            new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
            50);

        Assert.NotNull(result.Outputs.Get(m));
        Assert.False(result.Outputs.Get(m)!.Value.AsBool()); // sin propuesta: failsafe (P1-1)
    }

    [Fact]
    public void Scan_IsDeterministic()
    {
        var (s, d, a, m) = BuildMotorModel(out var defs);
        var program = MotorProgram(s, d, a, m);
        var inputs = Inputs(true, true, false, s, d, a);

        var r1 = new ScanCoordinator().Scan(program, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);
        var r2 = new ScanCoordinator().Scan(program, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        Assert.Equal(r1.Outputs.Get(m)!.Value.AsBool(), r2.Outputs.Get(m)!.Value.AsBool());
    }

    [Fact]
    public void Conflict_ResolvedByPriority_HigherWins()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [m] = new VariableDefinition { Id = m, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
        };

        var ruleOn = new LogicRule { Name = "ON", Priority = 10, Condition = null,
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = m, Value = "true" } } };
        var ruleOff = new LogicRule { Name = "OFF", Priority = 50, Condition = null,
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = m, Value = "false" } } };

        var program = new LogicProgram { Name = "Conflict", Rules = new List<LogicRule> { ruleOn, ruleOff } };
        var coord = new ScanCoordinator();

        var result = coord.Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(),
            new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
            50);

        // Ambos proponen distintos valores con distinta prioridad: la mayor (OFF, priority 50) gana
        var motor = result.Outputs.Get(m);
        Assert.NotNull(motor);
        Assert.False(motor!.Value.AsBool());
    }

    [Fact]
    public void EqualPriorityConflict_UsesFailsafe()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [m] = new VariableDefinition { Id = m, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
        };

        var ruleOn = new LogicRule { Name = "ON", Priority = 10, Condition = null,
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = m, Value = "true" } } };
        var ruleOff = new LogicRule { Name = "OFF", Priority = 10, Condition = null,
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = m, Value = "false" } } };

        var program = new LogicProgram { Name = "Conflict", Rules = new List<LogicRule> { ruleOn, ruleOff } };
        var coord = new ScanCoordinator();

        var result = coord.Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(),
            new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },  // failsafe = OFF
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
            50);

        var motor = result.Outputs.Get(m);
        Assert.NotNull(motor);
        Assert.False(motor!.Value.AsBool()); // failsafe applied
        Assert.True(result.Decisions.Single().Conflicted);
    }

    [Fact]
    public void Interlock_OverridesAutomaticControl()
    {
        var s = Guid.NewGuid();
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [s] = new VariableDefinition { Id = s, Key = "Start", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [m] = new VariableDefinition { Id = m, Key = "Motor", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
        };

        var rule = new LogicRule { Name = "Run", Priority = 10,
            Condition = new VariableExpression { VariableId = s, VariableKey = "Start" },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = m, Value = "true" } } };

        var door = Guid.NewGuid();
        var doorVar = new VariableDefinition { Id = door, Key = "Door", DataType = PlcDataType.Bool, Direction = VariableDirection.Input };
        defs[door] = doorVar;

        var interlock = new Interlock
        {
            Name = "DoorOpen",
            Condition = new NotExpression { Operand = new VariableExpression { VariableId = door, VariableKey = "Door" } },
            AffectedOutputs = new List<Guid> { m },
            SafeValue = false,
            Priority = OutputPriority.Interlock
        };

        var program = new LogicProgram { Name = "IL", Rules = new List<LogicRule> { rule } };
        var coord = new ScanCoordinator();

        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [s] = new RuntimeValue { VariableId = s, Value = PlcValue.Bool(true), Quality = Quality.Good },
            [door] = new RuntimeValue { VariableId = door, Value = PlcValue.Bool(false), Quality = Quality.Good } // door open
        };

        var result = coord.Scan(program, defs, inputs,
            new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock> { interlock },
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
            50);

        var motor = result.Outputs.Get(m);
        Assert.NotNull(motor);
        Assert.False(motor!.Value.AsBool()); // interlock closed motor
    }
}