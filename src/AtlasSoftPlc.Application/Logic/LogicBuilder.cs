using AtlasSoftPlc.Domain.Intent;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>
/// Convierte un AutomationIntent normalizado en un LogicProgram (IR).
/// Determinista: no ejecuta texto ni eval. (sección 20).
/// </summary>
public sealed class LogicBuilder
{
    private readonly IReadOnlyDictionary<string, Guid> _keyIndex;

    public LogicBuilder(IReadOnlyDictionary<Guid, VariableDefinition> variables)
    {
        _keyIndex = variables.ToDictionary(
            v => NormalizeKey(v.Value.Key),
            v => v.Key,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string key) =>
        new string(key.Where(char.IsLetterOrDigit).ToArray());

    public LogicProgram Build(AutomationIntent intent)
    {
        var program = new LogicProgram
        {
            Name = intent.Trigger ?? "Automatización",
            Rules = new List<LogicRule>()
        };

        // condiciones = AND de las condiciones normalizadas
        var operands = new List<ExpressionNode>();
        if (intent.Trigger is not null && _keyIndex.TryGetValue(intent.Trigger, out var triggerId))
            operands.Add(new VariableExpression { VariableId = triggerId, VariableKey = intent.Trigger });

        foreach (var cond in intent.Conditions)
        {
            if (TryParseCondition(cond, out var expr))
                operands.Add(expr);
        }

        ExpressionNode? condition = operands.Count switch
        {
            0 => null,
            1 => operands[0],
            _ => new AndExpression { Operands = operands }
        };

        var actions = new List<LogicAction>();
        foreach (var a in intent.Actions)
        {
            if (TryParseAction(a, out var action))
                actions.Add(action);
        }

        var elseActions = new List<LogicAction>();
        var stopConditions = new List<ExpressionNode>();
        foreach (var sc in intent.StopConditions)
        {
            if (TryParseCondition(sc, out var expr))
                stopConditions.Add(expr);
        }

        var rule = new LogicRule
        {
            Name = intent.Trigger ?? "Regla",
            Condition = condition,
            Actions = actions,
            ElseActions = elseActions,
            SourceIntent = intent.RawText,
            CreatedBy = intent.CreatedBy
        };

        program.Rules.Add(rule);

        // Los stop conditions generan una regla complementaria que apaga la salida
        if (stopConditions.Count > 0 && actions.Any(a => a is SetOutputAction))
        {
            var target = (SetOutputAction)actions.First(a => a is SetOutputAction);
            var stopRule = new LogicRule
            {
                Name = $"{rule.Name} — Paro",
                Priority = rule.Priority - 1,
                Condition = stopConditions.Count == 1 ? stopConditions[0] : new OrExpression { Operands = stopConditions },
                Actions = new List<LogicAction>
                {
                    new SetOutputAction { VariableId = target.VariableId, Value = "false" }
                },
                SourceIntent = intent.RawText,
                CreatedBy = intent.CreatedBy
            };
            program.Rules.Add(stopRule);
        }

        return program;
    }

    private bool TryParseCondition(string cond, out ExpressionNode expr)
    {
        expr = null!;
        // Soporta "Key == true" / "Key == false" / "not Key" / "Key"
        var trimmed = cond.Trim();
        var negate = trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("!", StringComparison.OrdinalIgnoreCase);
        if (negate)
        {
            var inner = trimmed.Substring(trimmed.IndexOf(' ') + 1).Trim();
            if (TryResolveKey(inner, out var id))
            {
                expr = new NotExpression { Operand = new VariableExpression { VariableId = id, VariableKey = inner } };
                return true;
            }
        }

        // "Key == value" o "Key = value"
        var eqIdx = trimmed.IndexOf("==", StringComparison.Ordinal);
        if (eqIdx < 0) eqIdx = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (eqIdx >= 0)
        {
            var left = trimmed.Substring(0, eqIdx).Trim();
            var right = trimmed.Substring(eqIdx + (trimmed.Contains("==", StringComparison.Ordinal) ? 2 : 1)).Trim();
            var rightIsFalse = right.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                               right == "0";
            var rightIsTrue = right.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                              right == "1";
            if ((rightIsFalse || rightIsTrue) && TryResolveKey(left, out var id))
            {
                var varExpr = new VariableExpression { VariableId = id, VariableKey = left };
                expr = rightIsFalse ? new NotExpression { Operand = varExpr } : varExpr;
                return true;
            }
        }

        // "Key" sola
        if (TryResolveKey(trimmed, out var sid))
        {
            expr = new VariableExpression { VariableId = sid, VariableKey = trimmed };
            return true;
        }

        return false;
    }

    private bool TryParseAction(string a, out LogicAction action)
    {
        action = null!;
        var trimmed = a.Trim();
        var eqIdx = trimmed.IndexOf('=');
        if (eqIdx < 0) return false;

        var left = trimmed.Substring(0, eqIdx).Trim();
        var right = trimmed.Substring(eqIdx + 1).Trim();
        if (!TryResolveKey(left, out var id)) return false;

        action = new SetOutputAction { VariableId = id, Value = right };
        return true;
    }

    private bool TryResolveKey(string key, out Guid id)
    {
        return _keyIndex.TryGetValue(NormalizeKey(key), out id);
    }
}