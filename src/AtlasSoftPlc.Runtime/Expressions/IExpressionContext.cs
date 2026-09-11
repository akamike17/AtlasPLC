using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Runtime.Expressions;

/// <summary>Resultado de evaluar una expresión: valor bool tipado o cadena/numérico.</summary>
public sealed class EvalResult
{
    public PlcValue Value { get; init; }
    public bool IsBoolean { get; init; }
    public string? Errors { get; init; }

    public bool Ok => string.IsNullOrEmpty(Errors);

    public static EvalResult Bool(bool v) => new() { Value = PlcValue.Bool(v), IsBoolean = true };
    public static EvalResult Fail(string msg) => new() { Value = PlcValue.Null(AtlasSoftPlc.Domain.Common.PlcDataType.Bool), IsBoolean = false, Errors = msg };
    public static EvalResult Val(PlcValue v) => new() { Value = v, IsBoolean = v.DataType == AtlasSoftPlc.Domain.Common.PlcDataType.Bool };
}

/// <summary>
/// Contexto de evaluación de expresiones. Provee acceso a entradas, memoria,
/// y estado de timers/counters/edges sin permitir efectos laterales.
/// </summary>
public interface IExpressionContext
{
    PlcValue? ReadVariable(Guid variableId);
    bool ReadTimerField(Guid timerId, string field, out double value);
    bool ReadCounterField(Guid counterId, string field, out long value);
    /// <summary>Evalúa el flanco de un operando comparando con el ciclo previo.</summary>
    bool DetectEdge(bool currentValue, Guid operandId, bool expectedRising);
}