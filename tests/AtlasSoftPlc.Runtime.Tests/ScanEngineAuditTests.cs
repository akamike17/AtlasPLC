using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

/// <summary>
/// Pruebas de regresión quirúrgica del motor de scan: P1-1 (toda salida decidida),
/// P1-2 (conflicto determinista/seguro), P1-3 (parseo sin String silencioso) y
/// P0-2 (interlock no-evaluable fail-closed).
/// </summary>
public class ScanEngineAuditTests
{
    private static VariableDefinition Out(string key, PlcDataType type = PlcDataType.Bool) =>
        new() { Key = key, DataType = type, Direction = VariableDirection.Output };

    private static LogicRule Rule(string name, int priority, ExpressionNode? condition, params LogicAction[] actions) =>
        new() { Name = name, Priority = priority, Condition = condition, Actions = actions.ToList() };

    // ── P1-1: output sin propuesta no desaparece ──────────────────────────
    [Fact]
    public void Output_WithoutProposal_GetsFailsafe_NotMissing()
    {
        var on = Guid.NewGuid();   // recibe propuesta
        var off = Guid.NewGuid();  // no recibe propuesta
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [on] = Out("On"),
            [off] = Out("Off")
        };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                Rule("SetOn", 10, null, new SetOutputAction { VariableId = on, Value = "true" })
            }
        };
        var failsafe = new Dictionary<Guid, PlcValue>
        {
            [on] = PlcValue.Bool(false),
            [off] = PlcValue.Bool(false)
        };
        var outputTypes = new Dictionary<Guid, PlcDataType>
        {
            [on] = PlcDataType.Bool,
            [off] = PlcDataType.Bool
        };

        var r = new ScanCoordinator().Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), failsafe, outputTypes, 50);

        // Ambas salidas definidas tienen decisión en el scan.
        Assert.True(r.Outputs.Get(on)!.Value.AsBool());   // calculada
        Assert.False(r.Outputs.Get(off)!.Value.AsBool()); // failsafe (no desapareció)
        Assert.DoesNotContain(r.Errors, e => e.Contains("Sin propuesta"));
    }

    [Fact]
    public void Output_WithoutProposal_AndWithoutFailsafe_ReportsError()
    {
        var on = Guid.NewGuid();
        var off = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [on] = Out("On"), [off] = Out("Off") };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule> { Rule("SetOn", 10, null, new SetOutputAction { VariableId = on, Value = "true" }) }
        };
        // sin failsafe para `off`
        var failsafe = new Dictionary<Guid, PlcValue> { [on] = PlcValue.Bool(false) };
        var outputTypes = new Dictionary<Guid, PlcDataType> { [on] = PlcDataType.Bool, [off] = PlcDataType.Bool };

        var r = new ScanCoordinator().Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), failsafe, outputTypes, 50);

        Assert.Contains(r.Errors, e => e.Contains("Sin propuesta"));
    }

    // ── P1-2: conflicto igual prioridad determinista/seguro ───────────────
    private static LogicProgram ConflictingProgram() => new()
    {
        Name = "Conflict",
        Rules = new List<LogicRule>
        {
            Rule("ON", 10, null, new SetOutputAction { VariableId = Guid.Empty, Value = "true" }),
            Rule("OFF", 10, null, new SetOutputAction { VariableId = Guid.Empty, Value = "false" })
        }
    };

    [Fact]
    public void EqualPriorityConflict_WithFailsafe_Deterministic()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M") };

        var r1 = new ScanCoordinator().Scan(
            ConflictProgram(m, first: true), defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        var r2 = new ScanCoordinator().Scan(
            ConflictProgram(m, first: false), defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        // Invertir el orden de las reglas produce exactamente el mismo resultado: failsafe.
        Assert.False(r1.Outputs.Get(m)!.Value.AsBool());
        Assert.False(r2.Outputs.Get(m)!.Value.AsBool());
        Assert.True(r1.Decisions.Single(d => d.VariableId == m).Conflicted);
        Assert.True(r2.Decisions.Single(d => d.VariableId == m).Conflicted);
    }

    [Fact]
    public void EqualPriorityConflict_WithoutFailsafe_ReportsError_NotFirstWins()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M") };

        var r = new ScanCoordinator().Scan(
            ConflictProgram(m, first: true), defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue>(), // sin failsafe
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        // No "primero gana": la salida no recibe una decisión operable y se reporta error.
        Assert.Contains(r.Errors, e => e.Contains("Conflicto de igual prioridad"));
        Assert.Null(r.Outputs.Get(m));
    }

    private static LogicProgram ConflictProgram(Guid m, bool first)
    {
        var ruleA = Rule("ON", 10, null, new SetOutputAction { VariableId = m, Value = "true" });
        var ruleB = Rule("OFF", 10, null, new SetOutputAction { VariableId = m, Value = "false" });
        return new LogicProgram
        {
            Name = "Conflict",
            Rules = first ? new List<LogicRule> { ruleA, ruleB } : new List<LogicRule> { ruleB, ruleA }
        };
    }

    // ── P1-3: parseo no devuelve String silencioso ────────────────────────
    [Fact]
    public void ParseTyped_IntOverflow_ReportsError_NotString()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M", PlcDataType.Int16) };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                Rule("Overflow", 10, null, new SetOutputAction { VariableId = m, Value = "999999999999" })
            }
        };

        var r = new ScanCoordinator().Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Int16(0) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Int16 }, 50);

        Assert.Contains(r.Errors, e => e.Contains("valor inválido"));
        // Failsafe aplicado (0), nunca un String.
        Assert.Equal(PlcDataType.Int16, r.Outputs.Get(m)!.Value.DataType);
        Assert.Equal((short)0, r.Outputs.Get(m)!.Value.As<short>());
    }

    [Fact]
    public void ParseTyped_BoolInvalid_ReportsError_NotString()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M", PlcDataType.Bool) };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                Rule("BadBool", 10, null, new SetOutputAction { VariableId = m, Value = "perhaps" })
            }
        };

        var r = new ScanCoordinator().Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        Assert.Contains(r.Errors, e => e.Contains("valor inválido"));
        Assert.False(r.Outputs.Get(m)!.Value.AsBool()); // failsafe
    }

    [Fact]
    public void ParseTyped_ValidInt_Parses()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M", PlcDataType.Int32) };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                Rule("Set42", 10, null, new SetOutputAction { VariableId = m, Value = "42" })
            }
        };

        var r = new ScanCoordinator().Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock>(), new Dictionary<Guid, PlcValue> { [m] = PlcValue.Int32(0) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Int32 }, 50);

        Assert.False(r.HasErrors);
        Assert.Equal(42, r.Outputs.Get(m)!.Value.As<int>());
    }

    // ── P0-3 (integración en motor): force vs interlock ─────────────────────
    [Fact]
    public void Force_AgainstActiveInterlock_InterlockWins()
    {
        var door = Guid.NewGuid();
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [door] = new VariableDefinition { Id = door, Key = "Door", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [m] = Out("M")
        };
        // Lógica apaga el motor (valor false), force intenta encenderlo.
        var program = new LogicProgram
        {
            Name = "IL",
            Rules = new List<LogicRule>
            {
                Rule("Off", 10, null, new SetOutputAction { VariableId = m, Value = "false" })
            }
        };
        var interlock = new Interlock
        {
            Name = "DoorOpen",
            Condition = new NotExpression { Operand = new VariableExpression { VariableId = door, VariableKey = "Door" } },
            AffectedOutputs = new List<Guid> { m },
            SafeValue = false
        };
        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [door] = new RuntimeValue { VariableId = door, Value = PlcValue.Bool(false) } // door open
        };

        // Force encender el motor (prioridad ManualForcedSafeCommand).
        var force = new OutputProposal
        {
            VariableId = m,
            Value = true,
            Priority = OutputPriority.ManualForcedSafeCommand,
            Reason = "manual force"
        };

        var r = new ScanCoordinator().Scan(
            ScanRequest.Create(program, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
                new List<Interlock> { interlock },
                new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
                new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
                50, new[] { force }));

        // El interlock activo gana sobre el force: SafeValue OFF.
        Assert.False(r.Outputs.Get(m)!.Value.AsBool());
    }

    [Fact]
    public void Force_Winning_MarksQualityAndSourceForced()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M") };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                Rule("Off", 10, null, new SetOutputAction { VariableId = m, Value = "false" })
            }
        };

        var force = new OutputProposal
        {
            VariableId = m,
            Value = true,
            Priority = OutputPriority.ManualForcedSafeCommand,
            Reason = "manual force"
        };

        var r = new ScanCoordinator().Scan(
            ScanRequest.Create(program, defs, new Dictionary<Guid, RuntimeValue>(),
                new Dictionary<Guid, RuntimeValue>(), new List<Interlock>(),
                new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
                new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
                50, new[] { force }));

        var outVal = r.Outputs.Get(m)!;
        Assert.True(outVal.Value.AsBool());          // force ganó
        Assert.Equal(Quality.Forced, outVal.Quality);  // marca Forced (Riesgo 2)
        Assert.Equal(ValueSource.Forced, outVal.Source);
    }

    [Fact]
    public void Force_OverriddenByInterlock_NotMarkedForced()
    {
        var door = Guid.NewGuid();
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [door] = new VariableDefinition { Id = door, Key = "Door", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [m] = Out("M")
        };
        var program = new LogicProgram
        {
            Name = "P",
            Rules = new List<LogicRule>
            {
                Rule("Off", 10, null, new SetOutputAction { VariableId = m, Value = "false" })
            }
        };
        var interlock = new Interlock
        {
            Name = "DoorOpen",
            Condition = new NotExpression { Operand = new VariableExpression { VariableId = door, VariableKey = "Door" } },
            AffectedOutputs = new List<Guid> { m },
            SafeValue = false
        };
        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [door] = new RuntimeValue { VariableId = door, Value = PlcValue.Bool(false) } // door open
        };

        var force = new OutputProposal { VariableId = m, Value = true, Priority = OutputPriority.ManualForcedSafeCommand, Reason = "manual force" };

        var r = new ScanCoordinator().Scan(
            ScanRequest.Create(program, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
                new List<Interlock> { interlock },
                new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
                new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool },
                50, new[] { force }));

        var outVal = r.Outputs.Get(m)!;
        Assert.False(outVal.Value.AsBool());            // interlock ganó (OFF)
        Assert.NotEqual(Quality.Forced, outVal.Quality); // no marcado Forced
        Assert.NotEqual(ValueSource.Forced, outVal.Source);
    }

    // ── P0-2: interlock no-evaluable fail-closed ──────────────────────────
    [Fact]
    public void Interlock_ValidFalse_NormalControl()
    {
        var s = Guid.NewGuid();
        var door = Guid.NewGuid();
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [s] = new VariableDefinition { Id = s, Key = "Start", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [door] = new VariableDefinition { Id = door, Key = "Door", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [m] = Out("M")
        };
        var program = new LogicProgram
        {
            Name = "IL",
            Rules = new List<LogicRule>
            {
                Rule("Run", 10, new VariableExpression { VariableId = s, VariableKey = "Start" },
                    new SetOutputAction { VariableId = m, Value = "true" })
            }
        };
        var interlock = new Interlock
        {
            Name = "DoorOpen",
            Condition = new NotExpression { Operand = new VariableExpression { VariableId = door, VariableKey = "Door" } },
            AffectedOutputs = new List<Guid> { m },
            SafeValue = false
        };
        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [s] = new RuntimeValue { VariableId = s, Value = PlcValue.Bool(true) },
            [door] = new RuntimeValue { VariableId = door, Value = PlcValue.Bool(true) } // door closed
        };

        var r = new ScanCoordinator().Scan(program, defs, inputs,
            new Dictionary<Guid, RuntimeValue>(), new List<Interlock> { interlock },
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        // interlock inactivo -> control normal (motor ON)
        Assert.True(r.Outputs.Get(m)!.Value.AsBool());
    }

    [Fact]
    public void Interlock_ValidTrue_ForcesSafeValue()
    {
        var s = Guid.NewGuid();
        var door = Guid.NewGuid();
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition>
        {
            [s] = new VariableDefinition { Id = s, Key = "Start", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [door] = new VariableDefinition { Id = door, Key = "Door", DataType = PlcDataType.Bool, Direction = VariableDirection.Input },
            [m] = Out("M")
        };
        var program = new LogicProgram
        {
            Name = "IL",
            Rules = new List<LogicRule>
            {
                Rule("Run", 10, new VariableExpression { VariableId = s, VariableKey = "Start" },
                    new SetOutputAction { VariableId = m, Value = "true" })
            }
        };
        var interlock = new Interlock
        {
            Name = "DoorOpen",
            Condition = new NotExpression { Operand = new VariableExpression { VariableId = door, VariableKey = "Door" } },
            AffectedOutputs = new List<Guid> { m },
            SafeValue = false
        };
        var inputs = new Dictionary<Guid, RuntimeValue>
        {
            [s] = new RuntimeValue { VariableId = s, Value = PlcValue.Bool(true) },
            [door] = new RuntimeValue { VariableId = door, Value = PlcValue.Bool(false) } // door open
        };

        var r = new ScanCoordinator().Scan(program, defs, inputs,
            new Dictionary<Guid, RuntimeValue>(), new List<Interlock> { interlock },
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        Assert.False(r.Outputs.Get(m)!.Value.AsBool()); // interlock activo -> SafeValue
    }

    [Fact]
    public void Interlock_InvalidExpression_FailClosed_WithError()
    {
        var m = Guid.NewGuid();
        var defs = new Dictionary<Guid, VariableDefinition> { [m] = Out("M") };
        var program = new LogicProgram
        {
            Name = "IL",
            Rules = new List<LogicRule>
            {
                Rule("Run", 10, null, new SetOutputAction { VariableId = m, Value = "true" })
            }
        };
        // interlock referencia una variable inexistente -> expresión no evaluable
        var interlock = new Interlock
        {
            Name = "MissingSensor",
            Condition = new VariableExpression { VariableId = Guid.NewGuid(), VariableKey = "Missing" },
            AffectedOutputs = new List<Guid> { m },
            SafeValue = false
        };

        var r = new ScanCoordinator().Scan(program, defs,
            new Dictionary<Guid, RuntimeValue>(), new Dictionary<Guid, RuntimeValue>(),
            new List<Interlock> { interlock },
            new Dictionary<Guid, PlcValue> { [m] = PlcValue.Bool(false) },
            new Dictionary<Guid, PlcDataType> { [m] = PlcDataType.Bool }, 50);

        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("MissingSensor"));
        // fail-closed: SafeValue (OFF)
        Assert.False(r.Outputs.Get(m)!.Value.AsBool());
    }
}