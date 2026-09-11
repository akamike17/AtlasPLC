using System.Globalization;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Runtime.Expressions;

/// <summary>
/// Evaluador determinista del IR de expresiones (sección 13).
/// No usa eval ni compila texto. Valida división por cero, overflow,
/// conversiones inválidas y comparación de tipos incompatibles.
/// </summary>
public sealed class ExpressionEngine
{
    public EvalResult Evaluate(ExpressionNode? node, IExpressionContext ctx)
    {
        if (node is null)
            return EvalResult.Bool(true); // condición ausente == siempre verdadera

        return node switch
        {
            ConstantExpression c => EvaluateConstant(c),
            VariableExpression v => EvaluateVariable(v, ctx),
            NotExpression n => EvaluateNot(n, ctx),
            AndExpression a => EvaluateAnd(a, ctx),
            OrExpression o => EvaluateOr(o, ctx),
            CompareExpression cmp => EvaluateCompare(cmp, ctx),
            ArithmeticExpression ar => EvaluateArithmetic(ar, ctx),
            EdgeExpression e => EvaluateEdge(e, ctx),
            TimerStateExpression t => EvaluateTimer(t, ctx),
            CounterStateExpression cnt => EvaluateCounter(cnt, ctx),
            _ => EvalResult.Fail($"Nodo de expresión no soportado: {node.GetType().Name}")
        };
    }

    private static EvalResult EvaluateConstant(ConstantExpression c)
    {
        try
        {
            var type = ParseDataType(c.DataType);
            return EvalResult.Val(new PlcValue(type, ParseConstantValue(type, c.Value)));
        }
        catch (Exception ex)
        {
            return EvalResult.Fail($"Constante inválida '{c.Value}': {ex.Message}");
        }
    }

    private static EvalResult EvaluateVariable(VariableExpression v, IExpressionContext ctx)
    {
        var value = ctx.ReadVariable(v.VariableId);
        if (value is null)
            return EvalResult.Fail($"Variable no encontrada: {v.VariableKey}");
        return EvalResult.Val(value.Value);
    }

    private EvalResult EvaluateNot(NotExpression n, IExpressionContext ctx)
    {
        var operand = Evaluate(n.Operand, ctx);
        if (!operand.Ok) return operand;
        if (!CanInterpretAsBool(operand.Value))
            return EvalResult.Fail("NOT requiere un operando booleano");
        return EvalResult.Bool(!operand.Value.AsBool());
    }

    private EvalResult EvaluateAnd(AndExpression a, IExpressionContext ctx)
    {
        if (a.Operands.Count == 0) return EvalResult.Bool(true);
        var result = true;
        foreach (var operand in a.Operands)
        {
            var e = Evaluate(operand, ctx);
            if (!e.Ok) return e;
            if (!CanInterpretAsBool(e.Value))
                return EvalResult.Fail("AND requiere operandos booleanos");
            result &= e.Value.AsBool();
            if (!result) return EvalResult.Bool(false); // cortocircuito
        }
        return EvalResult.Bool(result);
    }

    private EvalResult EvaluateOr(OrExpression o, IExpressionContext ctx)
    {
        if (o.Operands.Count == 0) return EvalResult.Bool(false);
        var result = false;
        foreach (var operand in o.Operands)
        {
            var e = Evaluate(operand, ctx);
            if (!e.Ok) return e;
            if (!CanInterpretAsBool(e.Value))
                return EvalResult.Fail("OR requiere operandos booleanos");
            result |= e.Value.AsBool();
            if (result) return EvalResult.Bool(true); // cortocircuito
        }
        return EvalResult.Bool(result);
    }

    private EvalResult EvaluateCompare(CompareExpression cmp, IExpressionContext ctx)
    {
        var left = Evaluate(cmp.Left, ctx);
        if (!left.Ok) return left;
        var right = Evaluate(cmp.Right, ctx);
        if (!right.Ok) return right;

        // Strings: comparación ordinal (solo == y !=)
        if (left.Value.DataType == PlcDataType.String || right.Value.DataType == PlcDataType.String)
        {
            if (left.Value.DataType == PlcDataType.String && right.Value.DataType == PlcDataType.String)
            {
                var sc = string.CompareOrdinal(left.Value.AsString(), right.Value.AsString());
                bool sres = cmp.Operator switch
                {
                    CompareOperator.Equal => sc == 0,
                    CompareOperator.NotEqual => sc != 0,
                    CompareOperator.GreaterThan => sc > 0,
                    CompareOperator.GreaterThanOrEqual => sc >= 0,
                    CompareOperator.LessThan => sc < 0,
                    CompareOperator.LessThanOrEqual => sc <= 0,
                    _ => false
                };
                return EvalResult.Bool(sres);
            }
            return EvalResult.Fail("Comparación de tipos incompatibles (string vs no-string)");
        }

        // Booleans: solo igualdad/desigualdad
        if (left.Value.DataType == PlcDataType.Bool && right.Value.DataType == PlcDataType.Bool)
        {
            bool bres = cmp.Operator switch
            {
                CompareOperator.Equal => left.Value.AsBool() == right.Value.AsBool(),
                CompareOperator.NotEqual => left.Value.AsBool() != right.Value.AsBool(),
                _ => false
            };
            return EvalResult.Bool(bres);
        }

        if (!left.Value.IsNumeric || !right.Value.IsNumeric)
            return EvalResult.Fail("Comparación de tipos incompatibles");

        var l = left.Value.AsDouble();
        var r = right.Value.AsDouble();
        bool result = cmp.Operator switch
        {
            CompareOperator.Equal => l == r,
            CompareOperator.NotEqual => l != r,
            CompareOperator.GreaterThan => l > r,
            CompareOperator.GreaterThanOrEqual => l >= r,
            CompareOperator.LessThan => l < r,
            CompareOperator.LessThanOrEqual => l <= r,
            _ => false
        };
        return EvalResult.Bool(result);
    }

    private EvalResult EvaluateArithmetic(ArithmeticExpression ar, IExpressionContext ctx)
    {
        var left = Evaluate(ar.Left, ctx);
        if (!left.Ok) return left;
        var right = Evaluate(ar.Right, ctx);
        if (!right.Ok) return right;

        var lv = left.Value;
        var rv = right.Value;

        if (!lv.IsNumeric || !rv.IsNumeric)
            return EvalResult.Fail("Aritmética requiere operandos numéricos");

        try
        {
            if (lv.IsFloatType || rv.IsFloatType)
            {
                var l = lv.AsDouble();
                var r = rv.AsDouble();
                double result = ar.Operator switch
                {
                    ArithmeticOperator.Add => l + r,
                    ArithmeticOperator.Subtract => l - r,
                    ArithmeticOperator.Multiply => l * r,
                    ArithmeticOperator.Divide => Divide(l, r),
                    ArithmeticOperator.Modulo => l % r,
                    _ => throw new InvalidOperationException()
                };
                if (double.IsNaN(result) || double.IsInfinity(result))
                    return EvalResult.Fail("Resultado aritmético no válido (NaN/infinity)");
                return EvalResult.Val(PlcValue.Double(result));
            }

            // integral — usamos long checked para detectar overflow
            var li = lv.As<long>() ?? Convert.ToInt64(lv.AsDecimal());
            var ri = rv.As<long>() ?? Convert.ToInt64(rv.AsDecimal());
            long iresult;
            checked
            {
                iresult = ar.Operator switch
                {
                    ArithmeticOperator.Add => li + ri,
                    ArithmeticOperator.Subtract => li - ri,
                    ArithmeticOperator.Multiply => li * ri,
                    ArithmeticOperator.Divide => ri == 0
                        ? throw new DivideByZeroException("Dividir por cero")
                        : li / ri,
                    ArithmeticOperator.Modulo => ri == 0
                        ? throw new DivideByZeroException("Módulo por cero")
                        : li % ri,
                    _ => throw new InvalidOperationException()
                };
            }
            return EvalResult.Val(PlcValue.Int64(iresult));
        }
        catch (DivideByZeroException ex)
        {
            return EvalResult.Fail(ex.Message);
        }
        catch (OverflowException)
        {
            return EvalResult.Fail("Desbordamiento aritmético (overflow)");
        }
        catch (Exception ex)
        {
            return EvalResult.Fail(ex.Message);
        }
    }

    private static double Divide(double l, double r)
    {
        if (r == 0.0) throw new DivideByZeroException("Dividir por cero");
        return l / r;
    }

    private EvalResult EvaluateEdge(EdgeExpression e, IExpressionContext ctx)
    {
        var current = Evaluate(e.Operand, ctx);
        if (!current.Ok) return current;
        if (!CanInterpretAsBool(current.Value))
            return EvalResult.Fail("Edge requiere operando booleano");
        var value = current.Value.AsBool();
        var rising = e.Kind == EdgeKind.RisingEdge;
        return EvalResult.Bool(ctx.DetectEdge(value, e.Operand.Id, rising));
    }

    private static EvalResult EvaluateTimer(TimerStateExpression t, IExpressionContext ctx)
    {
        if (!ctx.ReadTimerField(t.TimerId, t.Field, out var value))
            return EvalResult.Fail($"Timer no encontrado: {t.TimerId}");
        return t.Field == "Elapsed"
            ? EvalResult.Val(PlcValue.Double(value))
            : EvalResult.Bool(value != 0);
    }

    private static EvalResult EvaluateCounter(CounterStateExpression c, IExpressionContext ctx)
    {
        if (!ctx.ReadCounterField(c.CounterId, c.Field, out var value))
            return EvalResult.Fail($"Contador no encontrado: {c.CounterId}");
        return c.Field == "Current"
            ? EvalResult.Val(PlcValue.Int64(value))
            : EvalResult.Bool(value != 0);
    }

    private static bool CanInterpretAsBool(PlcValue v)
    {
        if (!v.HasValue) return true;
        if (v.DataType == PlcDataType.Bool) return true;
        // permitimos 0/1 numérico interpretable como bool
        if (v.IsNumeric)
        {
            var d = v.AsDouble();
            return d == 0.0 || d == 1.0;
        }
        return false;
    }

    private static PlcDataType ParseDataType(string type) => type switch
    {
        "Bool" => PlcDataType.Bool,
        "Int16" => PlcDataType.Int16,
        "UInt16" => PlcDataType.UInt16,
        "Int32" => PlcDataType.Int32,
        "UInt32" => PlcDataType.UInt32,
        "Int64" => PlcDataType.Int64,
        "UInt64" => PlcDataType.UInt64,
        "Float" => PlcDataType.Float,
        "Double" => PlcDataType.Double,
        "Decimal" => PlcDataType.Decimal,
        "String" => PlcDataType.String,
        "DateTime" => PlcDataType.DateTime,
        "TimeSpan" => PlcDataType.TimeSpan,
        _ => PlcDataType.String
    };

    private static object ParseConstantValue(PlcDataType type, string value) => type switch
    {
        PlcDataType.Bool => bool.TryParse(value, out var b) ? b
            : value is "1" or "0" ? value == "1"
            : throw new FormatException($"No es bool: {value}"),
        PlcDataType.Int16 => short.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.UInt16 => ushort.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.Int32 => int.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.UInt32 => uint.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.Float => float.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.Double => double.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.Decimal => decimal.Parse(value, CultureInfo.InvariantCulture),
        PlcDataType.String => value,
        PlcDataType.DateTime => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        PlcDataType.TimeSpan => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
        _ => value
    };
}