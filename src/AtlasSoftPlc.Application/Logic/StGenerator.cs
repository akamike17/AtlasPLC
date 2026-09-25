using System.Text;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>
/// Traduce la IR de Atlas a lenguaje Structured Text (ST) siguiendo el estándar IEC 61131-3.
/// Versión Estricta: Sin fallbacks genéricos.
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
                PlcDataType.Int16 => "INT",
                PlcDataType.UInt16 => "UINT",
                PlcDataType.Int32 => "DINT",
                PlcDataType.UInt32 => "UDINT",
                PlcDataType.Int64 => "LINT",
                PlcDataType.UInt64 => "ULINT",
                PlcDataType.Float => "REAL",
                PlcDataType.Double => "LREAL",
                PlcDataType.Decimal => "LREAL",
                PlcDataType.String => "STRING",
                PlcDataType.DateTime => "DATE_AND_TIME",
                PlcDataType.TimeSpan => "TIME",
                _ => throw new NotSupportedException($"Tipo de dato {v.DataType} no soportado para exportación ST.")
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

            var outputAction = rule.Actions.OfType<SetOutputAction>().FirstOrDefault();
            if (outputAction == null) continue;

            string targetVar = GetVariableKey(outputAction.VariableId, ir);
            var expression = TranslateExpression(rule.Condition, ir);
            sb.AppendLine($"{targetVar} := {expression};");
        }

        return sb.ToString();
    }

    private string TranslateExpression(ExpressionNode node, AtlasIrDocument ir)
    {
        return node switch
        {
            ConstantExpression c => TranslateConstant(c),
            VariableExpression v => GetVariableKey(v.VariableId, ir),
            NotExpression n => $"NOT ({TranslateExpression(n.Operand, ir)})",
            AndExpression a => string.Join(" AND ", a.Operands.Select(o => TranslateExpression(o, ir))),
            OrExpression o => string.Join(" OR ", o.Operands.Select(o => TranslateExpression(o, ir))),
            CompareExpression comp => $"({TranslateExpression(comp.Left, ir)} {TranslateOperator(comp.Operator)} {TranslateExpression(comp.Right, ir)})",
            ArithmeticExpression art => $"({TranslateExpression(art.Left, ir)} {TranslateOperator(art.Operator)} {TranslateExpression(art.Right, ir)})",
            _ => throw new NotSupportedException($"Nodo de expresión {node.GetType().Name} no soportado en ST.")
        };
    }

    private string TranslateConstant(ConstantExpression c)
    {
        return c.DataType switch
        {
            "Bool" => c.Value.ToUpper(),
            "String" => $"'{c.Value}'",
            _ => c.Value // Numéricos directos
        };
    }

    private string GetVariableKey(Guid id, AtlasIrDocument ir)
    {
        var v = ir.Variables.FirstOrDefault(x => x.Id == id);
        if (v == null) throw new KeyNotFoundException($"Variable {id} no encontrada en la IR.");
        return v.Key;
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
            _ => throw new NotSupportedException($"Operador de comparación {op} no soportado.")
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
            _ => throw new NotSupportedException($"Operador aritmético {op} no soportado.")
        };
    }
}
