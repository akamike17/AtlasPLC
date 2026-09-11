using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Expressions;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

public class ExpressionEngineExtendedTests
{
    private readonly ExpressionEngine _engine = new();

    private sealed class Context : IExpressionContext
    {
        public Dictionary<Guid, PlcValue> Vars { get; } = new();
        public Dictionary<Guid, double> Timers { get; } = new();
        public Dictionary<Guid, long> Counters { get; } = new();
        private readonly Dictionary<Guid, bool> _edges = new();

        public PlcValue? ReadVariable(Guid variableId)
            => Vars.TryGetValue(variableId, out var v) ? v : null;

        public bool ReadTimerField(Guid timerId, string field, out double value)
        {
            value = Timers.TryGetValue(timerId, out var v) ? v : 0;
            return Timers.ContainsKey(timerId);
        }

        public bool ReadCounterField(Guid counterId, string field, out long value)
        {
            value = Counters.TryGetValue(counterId, out var v) ? v : 0;
            return Counters.ContainsKey(counterId);
        }

        public bool DetectEdge(bool currentValue, Guid operandId, bool expectedRising)
        {
            var prev = _edges.TryGetValue(operandId, out var p) && p;
            _edges[operandId] = currentValue;
            return expectedRising ? currentValue && !prev : !currentValue && prev;
        }
    }

    [Fact]
    public void Null_Node_IsAlwaysTrue()
    {
        var r = _engine.Evaluate(null, new Context());
        Assert.True(r.Ok);
        Assert.True(r.Value.AsBool());
    }

    [Fact]
    public void Empty_And_IsTrue()
    {
        var expr = new AndExpression { Operands = new List<ExpressionNode>() };
        Assert.True(_engine.Evaluate(expr, new Context()).Value.AsBool());
    }

    [Fact]
    public void Empty_Or_IsFalse()
    {
        var expr = new OrExpression { Operands = new List<ExpressionNode>() };
        Assert.False(_engine.Evaluate(expr, new Context()).Value.AsBool());
    }

    [Fact]
    public void Constant_InvalidBool_ReportsError()
    {
        var expr = new ConstantExpression { Value = "notabool", DataType = "Bool" };
        var r = _engine.Evaluate(expr, new Context());
        Assert.False(r.Ok);
    }

    [Fact]
    public void Constant_Int64_Parses()
    {
        var expr = new ConstantExpression { Value = "123", DataType = "Int64" };
        var r = _engine.Evaluate(expr, new Context());
        Assert.True(r.Ok);
        Assert.Equal(123L, r.Value.As<long>());
    }

    [Fact]
    public void Constant_Double_Parses()
    {
        var expr = new ConstantExpression { Value = "1.5", DataType = "Double" };
        var r = _engine.Evaluate(expr, new Context());
        Assert.True(r.Ok);
        Assert.Equal(1.5, r.Value.AsDouble());
    }

    [Fact]
    public void Constant_UnknownType_TreatsAsString()
    {
        var expr = new ConstantExpression { Value = "hello", DataType = "Bogus" };
        var r = _engine.Evaluate(expr, new Context());
        Assert.True(r.Ok);
        Assert.Equal("hello", r.Value.AsString());
    }

    [Fact]
    public void Not_NonBool_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("notbool");
        var expr = new NotExpression { Operand = new VariableExpression { VariableId = a } };
        var r = _engine.Evaluate(expr, ctx);
        Assert.False(r.Ok);
        Assert.Contains("booleano", r.Errors);
    }

    [Fact]
    public void And_NonBool_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("s");
        var expr = new AndExpression { Operands = new List<ExpressionNode> { new VariableExpression { VariableId = a } } };
        var r = _engine.Evaluate(expr, ctx);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Or_NonBool_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("s");
        var expr = new OrExpression { Operands = new List<ExpressionNode> { new VariableExpression { VariableId = a } } };
        var r = _engine.Evaluate(expr, ctx);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Compare_Numeric_GreaterThanOrEqual()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(5);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.GreaterThanOrEqual
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_Numeric_LessThan()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(3);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.LessThan
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_Numeric_LessThanOrEqual()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(5);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.LessThanOrEqual
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_Numeric_NotEqual()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(3);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.NotEqual
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_Bool_Equal()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(true);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Bool(true);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.Equal
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_Bool_GreaterThan_ReturnsFalse()
    {
        // bool only supports == and !=; > returns false
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(true);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Bool(false);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.GreaterThan
        };
        Assert.False(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_MixedStringNumber_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("abc");
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.Equal
        };
        Assert.False(_engine.Evaluate(expr, ctx).Ok);
    }

    [Fact]
    public void Compare_String_GreaterThan()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("b");
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.String("a");
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.GreaterThan
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Arithmetic_Subtract()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int64(10);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int64(3);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Subtract
        };
        Assert.Equal(7L, _engine.Evaluate(expr, ctx).Value.As<long>());
    }

    [Fact]
    public void Arithmetic_Multiply()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int64(4);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int64(5);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Multiply
        };
        Assert.Equal(20L, _engine.Evaluate(expr, ctx).Value.As<long>());
    }

    [Fact]
    public void Arithmetic_Modulo()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int64(10);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int64(3);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Modulo
        };
        Assert.Equal(1L, _engine.Evaluate(expr, ctx).Value.As<long>());
    }

    [Fact]
    public void Arithmetic_FloatDivision()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Double(10.0);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Double(4.0);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Divide
        };
        Assert.Equal(2.5, _engine.Evaluate(expr, ctx).Value.AsDouble());
    }

    [Fact]
    public void Arithmetic_FloatDivision_ByZero_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Double(10.0);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Double(0.0);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Divide
        };
        Assert.False(_engine.Evaluate(expr, ctx).Ok);
    }

    [Fact]
    public void Arithmetic_NonNumeric_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("s");
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Add
        };
        Assert.False(_engine.Evaluate(expr, ctx).Ok);
    }

    [Fact]
    public void TimerField_Done_ReturnsBool()
    {
        var ctx = new Context();
        var timerId = Guid.NewGuid();
        ctx.Timers[timerId] = 1.0;
        var expr = new TimerStateExpression { TimerId = timerId, Field = "Done" };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void TimerField_Elapsed_ReturnsDouble()
    {
        var ctx = new Context();
        var timerId = Guid.NewGuid();
        ctx.Timers[timerId] = 500.0;
        var expr = new TimerStateExpression { TimerId = timerId, Field = "Elapsed" };
        var r = _engine.Evaluate(expr, ctx);
        Assert.True(r.Ok);
        Assert.Equal(500.0, r.Value.AsDouble());
    }

    [Fact]
    public void TimerField_Missing_ReportsError()
    {
        var ctx = new Context();
        var expr = new TimerStateExpression { TimerId = Guid.NewGuid(), Field = "Done" };
        Assert.False(_engine.Evaluate(expr, ctx).Ok);
    }

    [Fact]
    public void CounterField_Current_ReturnsInt64()
    {
        var ctx = new Context();
        var counterId = Guid.NewGuid();
        ctx.Counters[counterId] = 42;
        var expr = new CounterStateExpression { CounterId = counterId, Field = "Current" };
        var r = _engine.Evaluate(expr, ctx);
        Assert.True(r.Ok);
        Assert.Equal(42L, r.Value.As<long>());
    }

    [Fact]
    public void CounterField_Done_ReturnsBool()
    {
        var ctx = new Context();
        var counterId = Guid.NewGuid();
        ctx.Counters[counterId] = 1;
        var expr = new CounterStateExpression { CounterId = counterId, Field = "Done" };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void CounterField_Missing_ReportsError()
    {
        var ctx = new Context();
        var expr = new CounterStateExpression { CounterId = Guid.NewGuid(), Field = "Current" };
        Assert.False(_engine.Evaluate(expr, ctx).Ok);
    }

    [Fact]
    public void FallingEdge_DetectsTransition()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(true);
        var expr = new EdgeExpression { Operand = new VariableExpression { VariableId = a }, Kind = EdgeKind.FallingEdge };

        // first eval: prev=false, current=true -> not falling
        Assert.False(_engine.Evaluate(expr, ctx).Value.AsBool());

        // set to false -> falling edge detected
        ctx.Vars[a] = PlcValue.Bool(false);
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Edge_NonBool_ReportsError()
    {
        var ctx = new Context();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("s");
        var expr = new EdgeExpression { Operand = new VariableExpression { VariableId = a }, Kind = EdgeKind.RisingEdge };
        Assert.False(_engine.Evaluate(expr, ctx).Ok);
    }
}