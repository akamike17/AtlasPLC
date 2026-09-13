using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Domain.Projects;

/// <summary>
/// Único hasher canónico del contenido semántico de un programa PLC (P0-3).
/// Vive en Domain para que Application y Runtime reutilicen EXACTAMENTE la misma
/// implementación — prohibido mantener dos hashers divergentes.
///
/// Reglas:
///  - incluye TODO lo semánticamente relevante: variables (tipo/dirección/clave),
///    árbol de expresión completo, acciones + else-actions + valores, interlocks,
///    failsafe/safe states, mapa Modbus, versión de esquema;
///  - las colecciones no semánticas se ordenan de forma estable (por Id / clave);
///  - NO incluye timestamps, metadatos volátiles ni Ids de relación de persistencia.
/// </summary>
public static class CanonicalProgramHasher
{
    /// <summary>Hash SHA-256 hexadecimal (minúsculas) del contenido semántico del programa.</summary>
    public static string ComputeHash(PlcProgramDefinition program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var sb = new StringBuilder(1024);

        // 1. Identidad semántica (nombre/versión) — no timestamps ni description volatil.
        sb.Append(program.Name).Append('\u0001');
        sb.Append(program.Version.ToString(CultureInfo.InvariantCulture)).Append('\u0001');

        // 2. Variables ordenadas por clave semántica; el Id de persistencia es volátil.
        foreach (var v in program.Variables.OrderBy(x => x.Key, StringComparer.Ordinal))
            AppendVariable(sb, v);

        // 3. Lógica: reglas ordenadas por Id, con árbol de condición y acciones completas.
        AppendLogicProgram(sb, program.Logic);

        // 4. Failsafe/safe states ordenados por Id de variable.
        foreach (var kv in program.Failsafe.OrderBy(x => x.Key))
        {
            sb.Append(kv.Key).Append('=');
            AppendPlcValue(sb, kv.Value);
            sb.Append('\u0001');
        }

        foreach (var component in program.Plant.Components.OrderBy(x => x.Id))
        {
            sb.Append("P:").Append(component.Id).Append('/').Append(component.VariableId).Append('/')
              .Append(component.Name).Append('/').Append(component.RequiresSafeState).Append('/')
              .Append(component.SafeState?.Value.AsString() ?? "<none>").Append('\u0001');
            foreach (var requirement in component.Requires.OrderBy(x => x)) sb.Append("R:").Append(requirement).Append(';');
            foreach (var exclusive in component.MutuallyExclusiveWith.OrderBy(x => x)) sb.Append("X:").Append(exclusive).Append(';');
            sb.Append('\u0001');
        }

        // 5. Mapa Modbus ordenado por clave (VariableKey).
        foreach (var kv in program.ModbusMap.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\u0001');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Hash de SOLO la porción de lógica (LogicProgram), usando el MISMO algoritmo
    /// canónico de serialización. Útil para el runtime, que instala configuración por
    /// partes (program + definitions + interlocks + failsafe) y sólo ve la lógica.
    /// </summary>
    public static string ComputeLogicHash(LogicProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var sb = new StringBuilder(1024);
        AppendLogicProgram(sb, program);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendVariable(StringBuilder sb, VariableDefinition v)
    {
        sb.Append("V:").Append(v.Key).Append('/')
          .Append(v.Direction).Append('/')
          .Append(v.DataType).Append('/')
          .Append(v.SafetyCritical ? '1' : '0').Append('\u0001');
    }

    private static void AppendLogicProgram(StringBuilder sb, LogicProgram program)
    {
        sb.Append("L:").Append(program.Name).Append('/')
          .Append(program.Version.ToString(CultureInfo.InvariantCulture)).Append('/')
          .Append(program.Rules.Count).Append('\u0001');

        // La posición de las reglas y acciones forma parte de la semántica del scan;
        // los GUID de persistencia no deben decidir el orden canónico.
        foreach (var rule in program.Rules)
        {
            AppendRule(sb, rule);
        }
    }

    private static void AppendRule(StringBuilder sb, LogicRule rule)
    {
        // NOTA: no incluye rule.Id (GUID de persistencia, no semántica). La identidad
        // semántica de la regla es su nombre + prioridad + enabled + condición + acciones.
        sb.Append("R:").Append(rule.Priority).Append('/')
          .Append(rule.Enabled ? '1' : '0').Append('/')
          .Append(rule.Name).Append('\u0001');

        sb.Append("C:");
        AppendExpression(sb, rule.Condition);
        sb.Append('\u0001');

        sb.Append("A:");
        foreach (var action in rule.Actions)
            AppendAction(sb, action);
        sb.Append('\u0001');

        sb.Append("E:");
        foreach (var action in rule.ElseActions)
            AppendAction(sb, action);
        sb.Append('\u0001');
    }

    private static void AppendAction(StringBuilder sb, LogicAction action)
    {
        switch (action)
        {
            case SetOutputAction o:
                sb.Append("SetOutput:").Append(o.VariableId).Append('=').Append(o.Value);
                break;
            case SetMemoryAction m:
                sb.Append("SetMemory:").Append(m.VariableId).Append('=').Append(m.Value);
                break;
            case ResetMemoryAction rm:
                sb.Append("ResetMemory:").Append(rm.VariableId);
                break;
            case StartTimerAction st:
                sb.Append("StartTimer:").Append(st.TimerId).Append('=').Append(st.PresetMs.ToString("R", CultureInfo.InvariantCulture));
                break;
            case ResetTimerAction rt:
                sb.Append("ResetTimer:").Append(rt.TimerId);
                break;
            case IncrementCounterAction ic:
                sb.Append("IncCounter:").Append(ic.CounterId);
                break;
            case ResetCounterAction rc:
                sb.Append("ResetCounter:").Append(rc.CounterId);
                break;
            case RaiseAlarmAction ra:
                sb.Append("RaiseAlarm:").Append(ra.AlarmDefinitionId).Append('=').Append(ra.Message);
                break;
            case AcknowledgeAlarmAction aa:
                sb.Append("AckAlarm:").Append(aa.AlarmDefinitionId);
                break;
            case LogEventAction le:
                sb.Append("LogEvent:").Append(le.Message);
                break;
            default:
                sb.Append(action.GetType().Name).Append(':').Append(action.Id);
                break;
        }
        sb.Append(';');
    }

    private static void AppendExpression(StringBuilder sb, ExpressionNode? node)
    {
        if (node is null) { sb.Append("<null>"); return; }

        switch (node)
        {
            case ConstantExpression c:
                sb.Append("Const(").Append(c.DataType).Append(')').Append(c.Value);
                break;
            case VariableExpression v:
                sb.Append("Var(").Append(v.VariableId).Append(')').Append(v.VariableKey);
                break;
            case NotExpression n:
                sb.Append("Not("); AppendExpression(sb, n.Operand); sb.Append(')');
                break;
            case AndExpression a:
                AppendNary(sb, "And", a.Operands);
                break;
            case OrExpression o:
                AppendNary(sb, "Or", o.Operands);
                break;
            case CompareExpression c:
                sb.Append("Cmp(").Append(c.Operator).Append(',');
                AppendExpression(sb, c.Left); sb.Append(',');
                AppendExpression(sb, c.Right); sb.Append(')');
                break;
            case ArithmeticExpression ar:
                sb.Append("Arith(").Append(ar.Operator).Append(',');
                AppendExpression(sb, ar.Left); sb.Append(',');
                AppendExpression(sb, ar.Right); sb.Append(')');
                break;
            case EdgeExpression e:
                sb.Append("Edge(").Append(e.Kind).Append(',');
                AppendExpression(sb, e.Operand); sb.Append(')');
                break;
            case TimerStateExpression t:
                sb.Append("Timer(").Append(t.TimerId).Append(',').Append(t.Field).Append(')');
                break;
            case CounterStateExpression ct:
                sb.Append("Counter(").Append(ct.CounterId).Append(',').Append(ct.Field).Append(')');
                break;
            default:
                sb.Append(node.GetType().Name).Append('(').Append(node.Id).Append(')');
                break;
        }
    }

    private static void AppendNary(StringBuilder sb, string name, IReadOnlyList<ExpressionNode> operands)
    {
        sb.Append(name).Append('(');
        for (var i = 0; i < operands.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendExpression(sb, operands[i]);
        }
        sb.Append(')');
    }

    private static void AppendPlcValue(StringBuilder sb, PlcValue value)
    {
        sb.Append(value.DataType).Append('=');
        sb.Append(value.HasValue
            ? value.AsString()
            : "<null>");
    }
}
