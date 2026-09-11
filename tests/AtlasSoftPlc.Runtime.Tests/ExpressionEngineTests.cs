using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Expressions;

namespace AtlasSoftPlc.Runtime.Tests;

public class ExpressionEngineTests
{
    private readonly ExpressionEngine _engine = new();

    private sealed class DictContext : IExpressionContext
    {
        public Dictionary<Guid, PlcValue> Vars { get; } = new();
        private readonly Dictionary<Guid, bool> _edges = new();

        public PlcValue? ReadVariable(Guid variableId)
            => Vars.TryGetValue(variableId, out var v) ? v : null;

        public bool ReadTimerField(Guid timerId, string field, out double value)
        { value = 0; return false; }

        public bool ReadCounterField(Guid counterId, string field, out long value)
        { value = 0; return false; }

        public bool DetectEdge(bool currentValue, Guid operandId, bool expectedRising)
        {
            var prev = _edges.TryGetValue(operandId, out var p) && p;
            _edges[operandId] = currentValue;
            return expectedRising ? currentValue && !prev : !currentValue && prev;
        }
    }

    [Fact]
    public void And_ShortCircuits()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(false);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Bool(true);

        var expr = new AndExpression
        {
            Operands = new List<ExpressionNode>
            {
                new VariableExpression { VariableId = a },
                new VariableExpression { VariableId = b }
            }
        };
        var r = _engine.Evaluate(expr, ctx);
        Assert.True(r.Ok);
        Assert.False(r.Value.AsBool());
    }

    [Fact]
    public void Or_TrueWhenAnyTrue()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(false);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Bool(true);
        var expr = new OrExpression
        {
            Operands = new List<ExpressionNode>
            {
                new VariableExpression { VariableId = a },
                new VariableExpression { VariableId = b }
            }
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Not_Inverts()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(true);
        var expr = new NotExpression { Operand = new VariableExpression { VariableId = a } };
        Assert.False(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Compare_GreaterThan()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(10);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(5);
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.GreaterThan
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }

    [Fact]
    public void Divide_ByZero_ReportsError()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(10);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(0);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Divide
        };
        var r = _engine.Evaluate(expr, ctx);
        Assert.False(r.Ok);
        Assert.Contains("cero", r.Errors);
    }

    [Fact]
    public void Arithmetic_Add()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int32(2);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int32(3);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Add
        };
        var r = _engine.Evaluate(expr, ctx);
        Assert.Equal(5L, r.Value.As<long>());
    }

    [Fact]
    public void Arithmetic_IntegerOverflow_ReportsError()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Int64(long.MaxValue);
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.Int64(1);
        var expr = new ArithmeticExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = ArithmeticOperator.Add
        };
        var r = _engine.Evaluate(expr, ctx);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Constant_Bool_Parses()
    {
        var expr = new ConstantExpression { Value = "true", DataType = "Bool" };
        Assert.True(_engine.Evaluate(expr, new DictContext()).Value.AsBool());
    }

    [Fact]
    public void RisingEdge_DetectsTransition()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.Bool(true);
        var expr = new EdgeExpression
        {
            Operand = new VariableExpression { VariableId = a },
            Kind = EdgeKind.RisingEdge
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());   // false->true
        Assert.False(_engine.Evaluate(expr, ctx).Value.AsBool());  // true->true
    }

    [Fact]
    public void MissingVariable_ReportsError()
    {
        var ctx = new DictContext();
        var expr = new VariableExpression { VariableId = Guid.NewGuid(), VariableKey = "Missing" };
        var r = _engine.Evaluate(expr, ctx);
        Assert.False(r.Ok);
    }

    [Fact]
    public void StringComparison_EqualWorks()
    {
        var ctx = new DictContext();
        var a = Guid.NewGuid(); ctx.Vars[a] = PlcValue.String("abc");
        var b = Guid.NewGuid(); ctx.Vars[b] = PlcValue.String("abc");
        var expr = new CompareExpression
        {
            Left = new VariableExpression { VariableId = a },
            Right = new VariableExpression { VariableId = b },
            Operator = CompareOperator.Equal
        };
        Assert.True(_engine.Evaluate(expr, ctx).Value.AsBool());
    }
}