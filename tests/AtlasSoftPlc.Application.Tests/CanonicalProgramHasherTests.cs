using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Tests;

/// <summary>
/// P0-3 — El hash canónico DEBE representar la lógica real: cambiar la semántica
/// (condición, action/value, failsafe, ModbusMap) cambia el hash; no depende de
/// IDs/nombre/prioridad solamente.
/// </summary>
public class CanonicalProgramHasherTests
{
    private static PlcProgramDefinition BaseProgram() => new()
    {
        Name = "P",
        Version = 1,
        Variables = new List<VariableDefinition>
        {
            new() { Key = "Start", Direction = VariableDirection.Input, DataType = PlcDataType.Bool },
            new() { Key = "Motor", Direction = VariableDirection.Output, DataType = PlcDataType.Bool },
        },
        Logic = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                new()
                {
                    Name = "r",
                    Priority = 100,
                    Condition = new VariableExpression { VariableId = FIXED.Start, VariableKey = "Start" },
                    Actions = new List<LogicAction> { new SetOutputAction { VariableId = FIXED.Motor, Value = "true" } },
                },
            },
        },
    };

    private static class FIXED
    {
        // IDs estables para poder comparar hashes entre programas construidos igual.
        public static readonly Guid Start = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public static readonly Guid Motor = Guid.Parse("00000000-0000-0000-0000-000000000002");
    }

    [Fact]
    public void SameSemantics_SameHash()
    {
        var a = BaseProgram();
        var b = BaseProgram();
        Assert.Equal(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void ConditionChanged_HashChanges()
    {
        var a = BaseProgram();

        // Cambiar "Motor = Start" a "Motor = NOT Start" (misma variable, misma regla).
        var b = BaseProgram();
        b.Logic.Rules[0].Condition = new NotExpression
        {
            Operand = new VariableExpression { VariableId = FIXED.Start, VariableKey = "Start" },
        };

        Assert.NotEqual(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void ActionValueChanged_HashChanges()
    {
        var a = BaseProgram();

        var b = BaseProgram();
        ((SetOutputAction)b.Logic.Rules[0].Actions[0]).Value = "false";

        Assert.NotEqual(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void FailsafeChanged_HashChanges()
    {
        var a = BaseProgram();

        var b = BaseProgram();
        b.Failsafe[FIXED.Motor] = PlcValue.Bool(true);

        Assert.NotEqual(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void ModbusMapChanged_HashChanges()
    {
        var a = BaseProgram();

        var b = BaseProgram();
        b.ModbusMap["Motor"] = "coil:99";

        Assert.NotEqual(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void VariableDirectionChanged_HashChanges()
    {
        var a = BaseProgram();

        var b = BaseProgram();
        // Cambiar la dirección de una variable es un cambio semántico.
        b.Variables[0].Direction = VariableDirection.Memory;

        Assert.NotEqual(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void IdOnlyChange_DoesNotChangeHash()
    {
        // Un cambio de Id de regla (metadato de persistencia) NO debe alterar la semántica:
        // el hasher ordena por Id pero no incluye el Id de regla en el digest. Para mantener
        // la semántica idéntica comparamos dos programas con la MISMA estructura y distinto
        // Id de programa (que tampoco forma parte del digest).
        var a = BaseProgram();
        var b = BaseProgram();
        b.Id = Guid.NewGuid(); // Id de programa NO forma parte del hash.

        Assert.Equal(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }

    [Fact]
    public void TimestampChange_DoesNotChangeHash()
    {
        var a = BaseProgram();

        var b = BaseProgram();
        b.UpdatedUtc = DateTime.UtcNow.AddDays(1); // metadato volátil, no debe afectar.

        Assert.Equal(CanonicalProgramHasher.ComputeHash(a), CanonicalProgramHasher.ComputeHash(b));
    }
}