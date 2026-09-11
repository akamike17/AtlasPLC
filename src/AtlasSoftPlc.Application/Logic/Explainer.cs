using System.Text;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>Genera explicación humana de cada regla (sección 22).</summary>
public sealed class Explainer
{
    private readonly IReadOnlyDictionary<Guid, VariableDefinition> _variables;

    public Explainer(IReadOnlyDictionary<Guid, VariableDefinition> variables)
    {
        _variables = variables;
    }

    public string ExplainRule(LogicRule rule)
    {
        var sb = new StringBuilder();
        sb.AppendLine(rule.Name.ToUpperInvariant());

        var when = DescribeCondition(rule.Condition);
        if (when.Count > 0)
        {
            sb.AppendLine("Encenderá/activará cuando:");
            foreach (var w in when) sb.AppendLine($"  ✓ {w}");
        }

        var actions = rule.Actions.Select(DescribeAction).ToList();
        if (actions.Count > 0)
        {
            sb.AppendLine("Entonces:");
            foreach (var a in actions) sb.AppendLine($"  → {a}");
        }

        var elseActions = rule.ElseActions.Select(DescribeAction).ToList();
        if (elseActions.Count > 0)
        {
            sb.AppendLine("En caso contrario:");
            foreach (var a in elseActions) sb.AppendLine($"  → {a}");
        }

        return sb.ToString().TrimEnd();
    }

    private List<string> DescribeCondition(ExpressionNode? node)
    {
        var result = new List<string>();
        if (node is null) return result;

        switch (node)
        {
            case VariableExpression v:
                result.Add($"{Name(v.VariableId)} esté activo");
                break;
            case NotExpression n:
                if (n.Operand is VariableExpression nv)
                    result.Add($"{Name(nv.VariableId)} NO esté activo");
                else
                    result.Add($"no ({string.Join(" y ", DescribeCondition(n.Operand))})");
                break;
            case AndExpression a:
                foreach (var op in a.Operands)
                    result.AddRange(DescribeCondition(op));
                break;
            case OrExpression o:
                var parts = o.Operands.SelectMany(DescribeCondition).ToList();
                result.Add(string.Join(" o ", parts));
                break;
            case CompareExpression c:
                result.Add($"{Name(FirstVariable(c.Left))} {Op(c.Operator)} {ValueOf(c.Right)}");
                break;
            case TimerStateExpression t:
                result.Add($"temporizador {t.TimerId.ToString("N")[..8]} {t.Field}");
                break;
            case CounterStateExpression cnt:
                result.Add($"contador {cnt.CounterId.ToString("N")[..8]} {cnt.Field}");
                break;
            case ConstantExpression con:
                result.Add($"constante {con.Value}");
                break;
            case EdgeExpression e:
                result.Add($"{Name(FirstVariable(e.Operand))} flanco {(e.Kind == EdgeKind.RisingEdge ? "ascendente" : "descendente")}");
                break;
            default:
                result.Add(node.GetType().Name);
                break;
        }
        return result;
    }

    private string DescribeAction(LogicAction action) => action switch
    {
        SetOutputAction o => $"{Name(o.VariableId)} = {o.Value}",
        SetMemoryAction m => $"{Name(m.VariableId)} = {m.Value}",
        ResetMemoryAction rm => $"{Name(rm.VariableId)} = false",
        StartTimerAction st => $"iniciar temporizador {st.PresetMs}ms",
        ResetTimerAction rt => "reiniciar temporizador",
        IncrementCounterAction ic => "incrementar contador",
        ResetCounterAction rc => "reiniciar contador",
        RaiseAlarmAction ra => $"alarma: {ra.Message}",
        LogEventAction le => $"registro: {le.Message}",
        _ => action.GetType().Name
    };

    private string Name(Guid id) =>
        _variables.TryGetValue(id, out var v) ? v.DisplayName : id.ToString("N")[..8];

    private static string Op(CompareOperator op) => op switch
    {
        CompareOperator.Equal => "==",
        CompareOperator.NotEqual => "!=",
        CompareOperator.GreaterThan => ">",
        CompareOperator.GreaterThanOrEqual => ">=",
        CompareOperator.LessThan => "<",
        CompareOperator.LessThanOrEqual => "<=",
        _ => "?"
    };

    private string ValueOf(ExpressionNode n)
    {
        if (n is ConstantExpression c) return c.Value;
        if (n is VariableExpression v) return Name(v.VariableId);
        return "?";
    }

    private Guid FirstVariable(ExpressionNode? n)
    {
        if (n is VariableExpression v) return v.VariableId;
        return Guid.Empty;
    }
}