using System.Text.Json.Serialization;

namespace AtlasSoftPlc.Domain.Logic;

/// <summary>Nodos de expresión del IR (sección 12). No se ejecuta texto del usuario.</summary>
[JsonDerivedType(typeof(ConstantExpression), typeDiscriminator: "constant")]
[JsonDerivedType(typeof(VariableExpression), typeDiscriminator: "variable")]
[JsonDerivedType(typeof(NotExpression), typeDiscriminator: "not")]
[JsonDerivedType(typeof(AndExpression), typeDiscriminator: "and")]
[JsonDerivedType(typeof(OrExpression), typeDiscriminator: "or")]
[JsonDerivedType(typeof(CompareExpression), typeDiscriminator: "compare")]
[JsonDerivedType(typeof(ArithmeticExpression), typeDiscriminator: "arithmetic")]
[JsonDerivedType(typeof(EdgeExpression), typeDiscriminator: "edge")]
[JsonDerivedType(typeof(TimerStateExpression), typeDiscriminator: "timer")]
[JsonDerivedType(typeof(CounterStateExpression), typeDiscriminator: "counter")]
public abstract class ExpressionNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class ConstantExpression : ExpressionNode
{
    public string Value { get; set; } = string.Empty;
    public string DataType { get; set; } = "Bool";
}

public sealed class VariableExpression : ExpressionNode
{
    public Guid VariableId { get; set; }
    public string VariableKey { get; set; } = string.Empty;
}

public sealed class NotExpression : ExpressionNode
{
    public ExpressionNode Operand { get; set; } = null!;
}

public sealed class AndExpression : ExpressionNode
{
    public List<ExpressionNode> Operands { get; set; } = new();
}

public sealed class OrExpression : ExpressionNode
{
    public List<ExpressionNode> Operands { get; set; } = new();
}

public enum CompareOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed class CompareExpression : ExpressionNode
{
    public ExpressionNode Left { get; set; } = null!;
    public ExpressionNode Right { get; set; } = null!;
    public CompareOperator Operator { get; set; }
}

public enum ArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo
}

public sealed class ArithmeticExpression : ExpressionNode
{
    public ExpressionNode Left { get; set; } = null!;
    public ExpressionNode Right { get; set; } = null!;
    public ArithmeticOperator Operator { get; set; }
}

public enum EdgeKind
{
    RisingEdge,
    FallingEdge
}

public sealed class EdgeExpression : ExpressionNode
{
    public ExpressionNode Operand { get; set; } = null!;
    public EdgeKind Kind { get; set; }
}

/// <summary>Expresión que interroga el estado de un temporizador (Running/Done/Elapsed).</summary>
public sealed class TimerStateExpression : ExpressionNode
{
    public Guid TimerId { get; set; }
    public string Field { get; set; } = "Done"; // Running | Done | Elapsed
}

public sealed class CounterStateExpression : ExpressionNode
{
    public Guid CounterId { get; set; }
    public string Field { get; set; } = "Done"; // Current | Done
}