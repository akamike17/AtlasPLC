using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Services;

/// <summary>
/// Catálogo de programas iniciales de la biblioteca. Cada demo es una
/// <see cref="PlcProgramDefinition"/> autocontenida (variables + lógica + failsafe + mapa).
/// </summary>
public static class PlcProgramCatalog
{
    public static PlcProgramDefinition BuildTankDemo()
    {
        var low = Var("LowLevelSensor", "Nivel bajo", PlcDataType.Bool, VariableDirection.Input);
        var high = Var("HighLevelSensor", "Nivel alto", PlcDataType.Bool, VariableDirection.Input);
        var estop = Var("EmergencyStop", "Paro de emergencia", PlcDataType.Bool, VariableDirection.Input);
        var pump = Var("Pump", "Bomba", PlcDataType.Bool, VariableDirection.Output);

        var logic = new LogicProgram
        {
            Name = "Tanque de agua",
            Version = 1,
            ProjectId = Guid.Empty,
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Paro de emergencia",
                    Priority = 1000,
                    Condition = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "false" } }
                },
                new LogicRule
                {
                    Name = "Encender bomba",
                    Priority = 100,
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = low.Id, VariableKey = low.Key },
                            new NotExpression { Operand = new VariableExpression { VariableId = high.Id, VariableKey = high.Key } },
                            new NotExpression { Operand = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key } }
                        }
                    },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "true" } }
                },
                new LogicRule
                {
                    Name = "Apagar bomba por nivel alto",
                    Priority = 100,
                    Condition = new VariableExpression { VariableId = high.Id, VariableKey = high.Key },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "false" } }
                }
            }
        };

        return new PlcProgramDefinition
        {
            Name = "Tanque de agua",
            Version = 1,
            Description = "Llena el tanque cuando el nivel está bajo y se apaga al llegar arriba.",
            Variables = new List<VariableDefinition> { low, high, estop, pump },
            Logic = logic,
            Failsafe = new Dictionary<Guid, PlcValue> { [pump.Id] = PlcValue.Bool(false) },
            ModbusMap = new Dictionary<string, string>
            {
                ["LowLevelSensor"] = "coil:0",
                ["HighLevelSensor"] = "coil:1",
                ["EmergencyStop"] = "coil:2",
                ["Pump"] = "coil:10"
            }
        };
    }

    public static PlcProgramDefinition BuildIrrigationDemo()
    {
        var soilDry = Var("SoilDry", "Suelo seco", PlcDataType.Bool, VariableDirection.Input);
        var soilWet = Var("SoilWet", "Suelo húmedo", PlcDataType.Bool, VariableDirection.Input);
        var rain = Var("RainSensor", "Sensor de lluvia", PlcDataType.Bool, VariableDirection.Input);
        var estop = Var("EmergencyStop", "Paro de emergencia", PlcDataType.Bool, VariableDirection.Input);
        var valve = Var("IrrigationValve", "Válvula de riego", PlcDataType.Bool, VariableDirection.Output);

        var logic = new LogicProgram
        {
            Name = "Riego automático",
            Version = 1,
            ProjectId = Guid.Empty,
            Rules = new List<LogicRule>
            {
                new LogicRule
                {
                    Name = "Paro de emergencia",
                    Priority = 1000,
                    Condition = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = valve.Id, Value = "false" } }
                },
                new LogicRule
                {
                    Name = "Abrir riego",
                    Priority = 100,
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = soilDry.Id, VariableKey = soilDry.Key },
                            new NotExpression { Operand = new VariableExpression { VariableId = rain.Id, VariableKey = rain.Key } },
                            new NotExpression { Operand = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key } }
                        }
                    },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = valve.Id, Value = "true" } }
                },
                new LogicRule
                {
                    Name = "Cerrar riego por suelo húmedo",
                    Priority = 100,
                    Condition = new VariableExpression { VariableId = soilWet.Id, VariableKey = soilWet.Key },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = valve.Id, Value = "false" } }
                }
            }
        };

        return new PlcProgramDefinition
        {
            Name = "Riego automático",
            Version = 1,
            Description = "Riega cuando el suelo está seco, salvo si llueve o hay paro de emergencia.",
            Variables = new List<VariableDefinition> { soilDry, soilWet, rain, estop, valve },
            Logic = logic,
            Failsafe = new Dictionary<Guid, PlcValue> { [valve.Id] = PlcValue.Bool(false) },
            ModbusMap = new Dictionary<string, string>
            {
                ["SoilDry"] = "coil:0",
                ["SoilWet"] = "coil:1",
                ["RainSensor"] = "coil:2",
                ["EmergencyStop"] = "coil:3",
                ["IrrigationValve"] = "coil:10"
            }
        };
    }

    private static VariableDefinition Var(string key, string display, PlcDataType type, VariableDirection direction)
        => new()
        {
            Key = key,
            DisplayName = display,
            DataType = type,
            Direction = direction
        };
}