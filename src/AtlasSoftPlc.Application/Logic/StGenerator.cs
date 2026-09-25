using System.Text;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>
/// Traduce la IR de Atlas a lenguaje Structured Text (ST) siguiendo el estándar IEC 61131-3.
/// </summary>
public class StGenerator
{
    public string Generate(AtlasIrDocument ir)
    {
        var sb = new StringBuilder();

        // 1. Declaración de Variables (VAR / END_VAR)
        sb.AppendLine("// --- Declaraciones de Variables ---");
        sb.AppendLine("VAR");
        foreach (var v in ir.Variables)
        {
            string stType = v.DataType switch
            {
                PlcDataType.Bool => "BOOL",
                _ => "BOOL" // Simplificación MVP
            };
            sb.AppendLine($"    {v.Key} : {stType};");
        }
        sb.AppendLine("END_VAR");
        sb.AppendLine();

        // 2. Lógica de Control (Cuerpo del programa)
        sb.AppendLine("// --- Lógica de Control ---");
        foreach (var rule in ir.Logic.Rules)
        {
            if (rule.Condition == null) continue;

            // En Atlas, la lógica se traduce como asignaciones directas.
            // Buscamos la acción SetOutput para saber qué variable estamos controlando.
            var outputAction = rule.Actions.FirstOrDefault(a => a.GetType().Name == "SetOutputAction");
            
            string targetVar = "result";
            
            // Intento de obtener el ID de la variable mediante reflexión ya que SetOutputAction no es visible aquí
            var varIdProp = outputAction?.GetType().GetProperty("VariableId");
            if (varIdProp != null)
            {
                var id = (Guid)varIdProp.GetValue(outputAction)!;
                targetVar = GetVariableKey(id, ir);
            }

            var expression = TranslateExpression(rule.Condition, ir);
            sb.AppendLine($"{targetVar} := {expression};");
        }

        return sb.ToString();
    }

    private string TranslateExpression(ExpressionNode node, AtlasIrDocument ir)
    {
        return node switch
        {
            ConstantExpression c => c.Value.ToUpper(),
            VariableExpression v => GetVariableKey(v.VariableId, ir),
            NotExpression n => $"NOT ({TranslateExpression(n.Operand, ir)})",
            AndExpression a => string.Join(" AND ", a.Operands.Select(o => TranslateExpression(o, ir))),
            OrExpression o => string.Join(" OR ", o.Operands.Select(o => TranslateExpression(o, ir))),
            CompareExpression comp => $"({TranslateExpression(comp.Left, ir)} {TranslateOperator(comp.Operator)} {TranslateExpression(comp.Right, ir)})",
            ArithmeticExpression art => $"({TranslateExpression(art.Left, ir)} {TranslateOperator(art.Operator)} {TranslateExpression(art.Right, ir)})",
            _ => "TRUE"
        };
    }

    private string GetVariableKey(Guid id, AtlasIrDocument ir)
    {
        return ir.Variables.FirstOrDefault(v => v.Id == id)?.Key ?? $"VAR_{id.ToString().Substring(0, 8)}";
    }

    private string TranslateOperator(CompareOperator op)
    {
        return op switch
        {
            CompareOperator.Equal => "=",
            CompareOperator.NotEqual => "<>",
            CompareOperator.GreaterThan => ">",
            CompareOperator.GreaterThanOrEqual => ">=",
            CompareOperator.LessThan => "<",
            CompareOperator.LessThanOrEqual => "<=",
            _ => "="
        };
    }

    private string TranslateOperator(ArithmeticOperator op)
    {
        return op switch
        {
            ArithmeticOperator.Add => "+",
            ArithmeticOperator.Subtract => "-",
            ArithmeticOperator.Multiply => "*",
            ArithmeticOperator.Divide => "/",
            ArithmeticOperator.Modulo => "MOD",
            _ => "+"
        };
    }
}
