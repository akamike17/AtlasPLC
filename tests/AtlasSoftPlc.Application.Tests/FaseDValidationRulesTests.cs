using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Application.Validation;

namespace AtlasSoftPlc.Application.Tests;

/// <summary>FASE D — Validators deterministas con DiagnosticId + Hint (spec §39 FASE D).</summary>
public class FaseDValidationRulesTests
{
    private static VariableDefinition Out(string key = "Motor") =>
        new() { Key = key, DisplayName = key, DataType = PlcDataType.Bool, Direction = VariableDirection.Output };

    private static VariableDefinition In(string key) =>
        new() { Key = key, DisplayName = key, DataType = PlcDataType.Bool, Direction = VariableDirection.Input };

    private static List<ValidationIssue> Run(IValidationRule rule, LogicProgram program, IReadOnlyDictionary<Guid, VariableDefinition> variables)
        => rule.Validate(program, variables, new ValidationContext { Variables = variables }).ToList();

    // ── 1. Undefined reference ────────────────────────────────────────────

    [Fact]
    public void UndefinedReference_FlagsBlockWithHint()
    {
        var rule = new LogicRule
        {
            Name = "r",
            Condition = new VariableExpression { VariableId = Guid.NewGuid(), VariableKey = "Missing" },
        };
        var program = new LogicProgram { Rules = new List<LogicRule> { rule } };

        var issues = Run(new UndefinedReferenceValidationRule(), program, new Dictionary<Guid, VariableDefinition>());

        Assert.NotEmpty(issues);
        var issue = issues[0];
        Assert.Equal(ValidationSeverity.Blocker, issue.Severity);
        Assert.Equal("ATLAS-REF-0001", issue.DiagnosticId);
        Assert.False(string.IsNullOrWhiteSpace(issue.Hint));
    }

    [Fact]
    public void UndefinedReference_MissingWriter_Target_Blocked()
    {
        var missing = Guid.NewGuid();
        var rule = new LogicRule
        {
            Name = "r",
            Actions = new List<LogicAction> { new SetOutputAction { VariableId = missing, Value = "true" } },
        };
        var program = new LogicProgram { Rules = new List<LogicRule> { rule } };

        var issues = Run(new UndefinedReferenceValidationRule(), program, new Dictionary<Guid, VariableDefinition>());
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-REF-0002" && i.Severity == ValidationSeverity.Blocker);
    }

    // ── 2. Duplicate writer ───────────────────────────────────────────────

    [Fact]
    public void DuplicateWriter_SamePriority_Blocks()
    {
        var motor = Out();
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new() { Name = "a", Priority = 100, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "true" } } },
                new() { Name = "b", Priority = 100, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "false" } } },
            },
        };

        var issues = Run(new DuplicateWriterValidationRule(), program, vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0003" && i.Severity == ValidationSeverity.Blocker);
    }

    // ── 3. Output safe state ──────────────────────────────────────────────

    [Fact]
    public void OutputWithoutWriter_Blocks()
    {
        var motor = Out();
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };

        var issues = Run(new OutputSafeStateValidationRule(), new LogicProgram(), vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0005" && i.Severity == ValidationSeverity.Blocker);
    }

    [Fact]
    public void SafetyCriticalOutput_Warns()
    {
        var motor = Out();
        motor.SafetyCritical = true;
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };

        var issues = Run(new OutputSafeStateValidationRule(), new LogicProgram(), vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-SAFE-0004" && i.Severity == ValidationSeverity.Warning);
    }

    // ── 4. Interlock dominance ────────────────────────────────────────────

    [Fact]
    public void StopNotDominatingStart_Warns()
    {
        var motor = Out();
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new() { Name = "start", Priority = 200, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "true" } } },
                new() { Name = "stop", Priority = 100, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "false" } } },
            },
        };

        var issues = Run(new InterlockDominanceValidationRule(), program, vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-SAFE-0006" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void StopDominatingStart_NoWarning()
    {
        var motor = Out();
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new() { Name = "start", Priority = 100, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "true" } } },
                new() { Name = "stop", Priority = 1000, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "false" } } },
            },
        };

        var issues = Run(new InterlockDominanceValidationRule(), program, vars);
        Assert.DoesNotContain(issues, i => i.DiagnosticId == "ATLAS-SAFE-0006");
    }

    // ── 5. Contradictory boolean expression ───────────────────────────────

    [Fact]
    public void Contradictory_And_SameVariableAndNegation_AlwaysFalse()
    {
        var x = In("X");
        var vars = new Dictionary<Guid, VariableDefinition> { [x.Id] = x };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new()
                {
                    Name = "r",
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = x.Id, VariableKey = "X" },
                            new NotExpression { Operand = new VariableExpression { VariableId = x.Id, VariableKey = "X" } },
                        },
                    },
                },
            },
        };

        var issues = Run(new ContradictoryExpressionValidationRule(), program, vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0007" && i.Severity == ValidationSeverity.Blocker);
    }

    [Fact]
    public void Tautological_Or_SameVariableAndNegation_AlwaysTrue()
    {
        var x = In("X");
        var vars = new Dictionary<Guid, VariableDefinition> { [x.Id] = x };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new()
                {
                    Name = "r",
                    Condition = new OrExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = x.Id, VariableKey = "X" },
                            new NotExpression { Operand = new VariableExpression { VariableId = x.Id, VariableKey = "X" } },
                        },
                    },
                },
            },
        };

        var issues = Run(new ContradictoryExpressionValidationRule(), program, vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0008" && i.Severity == ValidationSeverity.Warning);
    }

    // ── 6. Unreachable branch ─────────────────────────────────────────────

    [Fact]
    public void UnreachableBranch_ContradictoryCondition_Blocks()
    {
        var x = In("X");
        var vars = new Dictionary<Guid, VariableDefinition> { [x.Id] = x };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new()
                {
                    Name = "dead",
                    Condition = new AndExpression
                    {
                        Operands = new List<ExpressionNode>
                        {
                            new VariableExpression { VariableId = x.Id, VariableKey = "X" },
                            new NotExpression { Operand = new VariableExpression { VariableId = x.Id, VariableKey = "X" } },
                        },
                    },
                },
            },
        };

        var issues = Run(new UnreachableBranchValidationRule(), program, vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0009" && i.Severity == ValidationSeverity.Blocker);
    }

    // ── Integración: el reporte invalida con Blocker ──────────────────────

    [Fact]
    public void Report_BlockerInvalidates()
    {
        var report = new ValidationReport();
        report.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Blocker });
        Assert.False(report.IsValid);
    }

    // ── P1-1: separación SafetyCritical vs HasWriter ────────────────────────

    [Fact]
    public void SafetyCriticalOutput_WithoutWriter_EmitsBothBlockerAndSafeStateWarning()
    {
        var motor = Out();
        motor.SafetyCritical = true;
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };

        // Sin escritor: el Blocker de writer NO debe ser excluido por SafetyCritical (P1-1).
        var issues = Run(new OutputSafeStateValidationRule(), new LogicProgram(), vars);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0005" && i.Severity == ValidationSeverity.Blocker);
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-SAFE-0004" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void SafetyCriticalOutput_WithWriterAndSafeState_NoFalsePositive()
    {
        var motor = Out();
        motor.SafetyCritical = true;
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new() { Name = "r", Priority = 100, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "true" } } },
            },
        };
        var context = new ValidationContext
        {
            Variables = vars,
            SafeStates = new Dictionary<Guid, AtlasSoftPlc.Domain.Values.PlcValue>
            {
                [motor.Id] = AtlasSoftPlc.Domain.Values.PlcValue.Bool(false),
            },
        };

        var issues = new OutputSafeStateValidationRule().Validate(program, vars, context).ToList();

        // Tiene escritor (no Blocker) y safe-state presente (no Warning): sin falso positivo.
        Assert.DoesNotContain(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0005");
        Assert.DoesNotContain(issues, i => i.DiagnosticId == "ATLAS-SAFE-0004");
    }

    [Fact]
    public void SafetyCriticalOutput_WithWriterButNoSafeState_Warns()
    {
        var motor = Out();
        motor.SafetyCritical = true;
        var vars = new Dictionary<Guid, VariableDefinition> { [motor.Id] = motor };
        var program = new LogicProgram
        {
            Rules = new List<LogicRule>
            {
                new() { Name = "r", Priority = 100, Actions = new List<LogicAction> { new SetOutputAction { VariableId = motor.Id, Value = "true" } } },
            },
        };
        var context = new ValidationContext { Variables = vars }; // sin SafeStates

        var issues = new OutputSafeStateValidationRule().Validate(program, vars, context).ToList();

        // Tiene escritor → sin Blocker; sin safe-state → Warning (diagnóstico esperado).
        Assert.DoesNotContain(issues, i => i.DiagnosticId == "ATLAS-LOGIC-0005");
        Assert.Contains(issues, i => i.DiagnosticId == "ATLAS-SAFE-0004" && i.Severity == ValidationSeverity.Warning);
    }
}