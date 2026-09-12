using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Ir;

/// <summary>
/// Fixtures de regresión construidos sobre la IR canónica (<see cref="AtlasIrDocument"/>).
/// Los demos del producto son fixtures de prueba (spec §0/§23), NUNCA el núcleo.
/// </summary>
public static class AtlasIrFixtures
{
    private static VariableDefinition Var(string key, string display, PlcDataType type, VariableDirection direction) =>
        new() { Key = key, DisplayName = display, DataType = type, Direction = direction };

    /// <summary>
    /// Fixture base "Motor" (spec §39 FASE E): la columna vertebral mínima.
    ///   Motor = Start AND NOT Stop AND GuardClosed
    /// Invariantes esperados: Stop → NOT Motor ; NOT GuardClosed → NOT Motor.
    /// </summary>
    public static AtlasIrDocument BuildMotorStopGuard()
    {
        var start = Var("Start", "Arranque", PlcDataType.Bool, VariableDirection.Input);
        var stop = Var("Stop", "Paro", PlcDataType.Bool, VariableDirection.Input);
        var guardClosed = Var("GuardClosed", "Guard cerrada", PlcDataType.Bool, VariableDirection.Input);
        var motor = Var("Motor", "Motor", PlcDataType.Bool, VariableDirection.Output);

        var rule = new LogicRule
        {
            Name = "Marcha del motor",
            Priority = 100,
            Condition = new AndExpression
            {
                Operands = new List<ExpressionNode>
                {
                    new VariableExpression { VariableId = start.Id, VariableKey = start.Key },
                    new NotExpression { Operand = new VariableExpression { VariableId = stop.Id, VariableKey = stop.Key } },
                    new VariableExpression { VariableId = guardClosed.Id, VariableKey = guardClosed.Key },
                },
            },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "true" } },
        };

        // Paro dominante: Stop apaga el motor (stop domina start, spec §5).
        var stopRule = new LogicRule
        {
            Name = "Paro dominante",
            Priority = 1000,
            Condition = new VariableExpression { VariableId = stop.Id, VariableKey = stop.Key },
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "false" } },
        };

        return new AtlasIrDocument
        {
            Name = "Motor con paro y guard",
            Description = "Motor = Start AND NOT Stop AND GuardClosed (fixture de regresión).",
            Variables = new List<VariableDefinition> { start, stop, guardClosed, motor },
            Logic = new LogicProgram
            {
                Name = "Motor con paro y guard",
                Rules = new List<LogicRule> { stopRule, rule },
            },
            Interlocks = new List<Interlock>(),
            SafeStates = new List<SafeState>
            {
                new() { VariableId = motor.Id, Value = PlcValue.Bool(false), Reason = "Motor se desenergiza por defecto." },
            },
        };
    }

    /// <summary>
    /// Fixture Tanque migrado a la IR canónica (spec §39 FASE C: migrar el ejemplo
    /// Tanque a la IR como regression fixture). Equivalente 1:1 a
    /// <c>PlcProgramCatalog.BuildTankDemo()</c>.
    /// </summary>
    public static AtlasIrDocument BuildTank()
    {
        var low = Var("LowLevelSensor", "Nivel bajo", PlcDataType.Bool, VariableDirection.Input);
        var high = Var("HighLevelSensor", "Nivel alto", PlcDataType.Bool, VariableDirection.Input);
        var estop = Var("EmergencyStop", "Paro de emergencia", PlcDataType.Bool, VariableDirection.Input);
        var pump = Var("Pump", "Bomba", PlcDataType.Bool, VariableDirection.Output);

        var logic = new LogicProgram
        {
            Name = "Tanque de agua",
            Rules = new List<LogicRule>
            {
                new()
                {
                    Name = "Paro de emergencia",
                    Priority = 1000,
                    Condition = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "false" } },
                },
                new()
                {
                    Name = "Encender bomba",
                    Priority = 100,
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = low.Id, VariableKey = low.Key },
                            new NotExpression { Operand = new VariableExpression { VariableId = high.Id, VariableKey = high.Key } },
                            new NotExpression { Operand = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key } },
                        },
                    },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "true" } },
                },
                new()
                {
                    Name = "Apagar bomba por nivel alto",
                    Priority = 100,
                    Condition = new VariableExpression { VariableId = high.Id, VariableKey = high.Key },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "false" } },
                },
            },
        };

        return new AtlasIrDocument
        {
            Name = "Tanque de agua",
            Description = "Llena el tanque cuando el nivel está bajo y se apaga al llegar arriba.",
            Variables = new List<VariableDefinition> { low, high, estop, pump },
            Logic = logic,
            Interlocks = new List<Interlock>(),
            SafeStates = new List<SafeState>
            {
                new() { VariableId = pump.Id, Value = PlcValue.Bool(false), Reason = "Bomba se desenergiza por defecto." },
            },
        };
    }
}