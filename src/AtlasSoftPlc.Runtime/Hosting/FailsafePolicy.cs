using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Values;

namespace AtlasSoftPlc.Runtime.Hosting;

/// <summary>
/// Política de failsafe por defecto. Se aplica cuando una salida NO tiene un failsafe
/// explícitamente configurado. Es una decisión de diseño formal y validada: la salida
/// debe caer al estado más seguro posible dado su tipo, nunca conservar su último valor
/// lógico ni un valor implícito/no documentado.
///
/// Tabla de la política (por <see cref="PlcDataType"/>):
///   Bool        → false       (desenergizado)
///   Int16/UInt16/Int32/UInt32/Int64/UInt64 → 0
///   Float/Double/Decimal      → 0 (0f / 0d / 0m)
///   String      → string.Empty
///   DateTime    → <see cref="DateTime.MinValue"/> (default)
///   TimeSpan    → <see cref="TimeSpan.Zero"/>
///
/// Esta política es una red de seguridad. Para salidas que NO admiten un "apagado" seguro
/// (p.ej. una válvula que debe quedar abierta ante pérdida de energía), el proyecto DEBE
/// declarar un failsafe explícito; la política por defecto NO puede inferir esa intención.
/// </summary>
public static class FailsafePolicy
{
    /// <summary>Valor failsafe por defecto para un tipo de dato dado.</summary>
    /// <remarks>Nunca devuelve un valor aleatorio ni dependiente del último scan.</remarks>
    public static PlcValue Default(PlcDataType type) => type switch
    {
        PlcDataType.Bool => PlcValue.Bool(false),
        PlcDataType.Int16 => PlcValue.Int16(0),
        PlcDataType.UInt16 => PlcValue.UInt16(0),
        PlcDataType.Int32 => PlcValue.Int32(0),
        PlcDataType.UInt32 => PlcValue.UInt32(0),
        PlcDataType.Int64 => PlcValue.Int64(0),
        PlcDataType.UInt64 => PlcValue.UInt64(0),
        PlcDataType.Float => PlcValue.Float(0f),
        PlcDataType.Double => PlcValue.Double(0d),
        PlcDataType.Decimal => PlcValue.Decimal(0m),
        PlcDataType.String => PlcValue.String(string.Empty),
        PlcDataType.DateTime => PlcValue.DateTime(default),
        PlcDataType.TimeSpan => PlcValue.TimeSpan(System.TimeSpan.Zero),
        _ => PlcValue.Null(type)
    };
}