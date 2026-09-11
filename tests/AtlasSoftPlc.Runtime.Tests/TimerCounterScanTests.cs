using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;

namespace AtlasSoftPlc.Runtime.Tests;

/// <summary>Prueba temporizadores y contadores a través del scan engine.</summary>
public class TimerCounterScanTests
{
    private static VariableDefinition Out(string key) =>
        new() { Key = key, DataType = PlcDataType.Bool, Direction = VariableDirection.Output };

    private static VariableDefinition In(string key) =>
        new() { Key = key, DataType = PlcDataType.Bool, Direction = VariableDirection.Input };

    [Fact]
    public void Ton_Timer_CyclesThroughScan()
    {
        var sensor = Guid.NewGuid();
        var fan = Guid.NewGuid();
        var timerId = Guid.NewGuid();

        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [sensor] = In("Sensor"),
            [fan] = Out("Fan")
        };

        var program = new LogicProgram
        {
            Name = "Fan",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Fan after 1s",
                    Condition = new VariableExpression { VariableId = sensor, VariableKey = "Sensor" },
                    Actions = new List<LogicAction> { new StartTimerAction { TimerId = timerId, PresetMs = 1000 } }
                },
                new LogicRule
                {
                    Name = "Fan on timer done",
                    Condition = new TimerStateExpression { TimerId = timerId, Field = "Done" },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = fan, Value = "true" } }
                }
            }
        };

        var coord = new ScanCoordinator();
        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [sensor] = new RuntimeValue { VariableId = sensor, Value = PlcValue.Bool(true), Quality = Quality.Good }
        };

        // scan 1: start timer, delta 500ms -> not done
        var r1 = coord.Scan(program, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [fan] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [fan] = PlcDataType.Bool }, 500);
        Assert.Null(r1.Outputs.Get(fan));

        // scan 2: delta 1000ms -> total >= 1000ms preset -> done
        var r2 = coord.Scan(program, defs, inputs, r1.Memory.Values,
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [fan] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [fan] = PlcDataType.Bool }, 1000);
        var fanVal = r2.Outputs.Get(fan);
        Assert.NotNull(fanVal);
        Assert.True(fanVal!.Value.AsBool());
    }

    [Fact]
    public void Counter_IncrementsAndSetsOutput()
    {
        var pulse = Guid.NewGuid();
        var lamp = Guid.NewGuid();
        var counterId = Guid.NewGuid();

        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [pulse] = In("Pulse"),
            [lamp] = Out("Lamp")
        };

        var program = new LogicProgram
        {
            Name = "Count",
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "count pulses",
                    Condition = new VariableExpression { VariableId = pulse, VariableKey = "Pulse" },
                    Actions = new List<LogicAction> { new IncrementCounterAction { CounterId = counterId } }
                },
                new LogicRule
                {
                    Name = "lamp at 3",
                    Condition = new CounterStateExpression { CounterId = counterId, Field = "Done" },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = lamp, Value = "true" } }
                }
            },
            CounterIds = new List<Guid> { counterId }
        };

        var coord = new ScanCoordinator();

        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [pulse] = new RuntimeValue { VariableId = pulse, Value = PlcValue.Bool(true), Quality = Quality.Good }
        };

        var r = coord.Scan(program, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [lamp] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [lamp] = PlcDataType.Bool }, 50);

        Assert.NotNull(r.Outputs.Get(lamp));
    }
}