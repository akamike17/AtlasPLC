using System.Security.Cryptography;
using System.Text;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Backends;

public sealed record EmitterDiagnostic(string DiagnosticId, string Message, Guid? ElementId = null);
public sealed record SourceMapEntry(Guid ElementId, int Line);
public sealed class StructuredTextArtifact
{
    public string Source { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public IReadOnlyList<SourceMapEntry> SourceMap { get; init; } = Array.Empty<SourceMapEntry>();
    public IReadOnlyList<EmitterDiagnostic> Diagnostics { get; init; } = Array.Empty<EmitterDiagnostic>();
    public bool IsSupported => Diagnostics.Count == 0;
}

/// <summary>Emite sólo el subset booleano y assignments que Atlas puede verificar.</summary>
public sealed class StructuredTextEmitter
{
    private sealed record EffectiveWrite(Guid RuleId, Guid VariableId, string Value, ExpressionNode? Condition, bool IsElse, bool IsMemory);

    public StructuredTextArtifact Emit(PlcProgramDefinition program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var lines = new List<string> { "(* AtlasPLC deterministic ST subset *)", "PROGRAM AtlasProgram", "VAR" };
        foreach (var variable in program.Variables.OrderBy(v => v.Key, StringComparer.Ordinal))
            lines.Add($"    {Identifier(variable.Key)} : {Type(variable)};");
        lines.Add("END_VAR");
        var map = new List<SourceMapEntry>();
        var diagnostics = new List<EmitterDiagnostic>();
        var enabledRules = program.Logic.Rules.Where(r => r.Enabled).ToList();
        var writes = enabledRules.SelectMany(r => r.Actions.Concat(r.ElseActions).Select(a => ToWrite(r, a))).Where(w => w is not null).Cast<EffectiveWrite>().ToList();
        foreach (var group in writes.GroupBy(w => (w.VariableId, w.IsMemory)))
        {
            var writers = group.ToList();
            if (writers.Any(w => ToDnf(EffectiveCondition(w)) is null) && !writers.All(w => writers.Any(other => other.RuleId == w.RuleId && other.IsElse != w.IsElse)))
                diagnostics.Add(new("ATLAS-ST-0005", $"No se puede demostrar exclusión mutua para la variable {group.Key.VariableId}.", group.Key.VariableId));
            for (var i = 0; i < writers.Count; i++)
                for (var j = i + 1; j < writers.Count; j++)
                {
                    if (writers[i].RuleId == writers[j].RuleId && writers[i].IsElse != writers[j].IsElse)
                        continue;
                    if (!MutuallyExclusive(EffectiveCondition(writers[i]), EffectiveCondition(writers[j])))
                        diagnostics.Add(new("ATLAS-ST-0004", $"La variable {group.Key.VariableId} tiene escritores que pueden competir en el mismo scan; no se puede preservar el arbitraje en ST.", group.Key.VariableId));
                }
        }
        foreach (var rule in enabledRules)
        {
            var allActions = rule.Actions.Concat(rule.ElseActions).ToList();
            foreach (var action in allActions)
            {
                if (action is not SetOutputAction and not SetMemoryAction)
                {
                    diagnostics.Add(new("ATLAS-ST-0001", $"Acción no soportada por el subset ST: {action.GetType().Name}.", action.Id));
                    continue;
                }
            }
            foreach (var target in allActions.Where(a => a is SetOutputAction or SetMemoryAction).Select(a => a is SetOutputAction o ? o.VariableId : ((SetMemoryAction)a).VariableId).Distinct())
            {
                var variable = program.Variables.FirstOrDefault(v => v.Id == target);
                var expression = Expression(rule.Condition, program.Variables, diagnostics, rule.Id);
                if (variable is null || expression is null) continue;
                if (variable.DataType != Domain.Common.PlcDataType.Bool)
                {
                    diagnostics.Add(new("ATLAS-ST-0003", $"La asignación booleana no soporta la variable no booleana {variable.Key}.", rule.Id));
                    continue;
                }
                var trueAction = rule.Actions.OfType<LogicAction>().FirstOrDefault(a => SameTarget(a, target));
                var falseAction = rule.ElseActions.OfType<LogicAction>().FirstOrDefault(a => SameTarget(a, target));
                var branch = trueAction is not null
                    ? falseAction is null
                        ? $"IF {expression} THEN {Identifier(variable.Key)} := {ActionValue(trueAction)}; END_IF; (* retains previous value when FALSE *)"
                        : $"IF {expression} THEN {Identifier(variable.Key)} := {ActionValue(trueAction)}; ELSE {Identifier(variable.Key)} := {ActionValue(falseAction)}; END_IF;"
                    : falseAction is not null
                        ? $"IF NOT ({expression}) THEN {Identifier(variable.Key)} := {ActionValue(falseAction)}; END_IF; (* retains previous value when TRUE *)"
                        : string.Empty;
                if (branch.Length == 0) continue;
                lines.Add($"    {branch}");
                map.Add(new(rule.Id, lines.Count));
            }
        }
        lines.Add("END_PROGRAM");
        var source = string.Join("\n", lines) + "\n";
        return new StructuredTextArtifact { Source = source, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant(), SourceMap = map, Diagnostics = diagnostics };
    }

    private static EffectiveWrite? ToWrite(LogicRule rule, LogicAction action) => action switch
    {
        SetOutputAction o => new(rule.Id, o.VariableId, o.Value, rule.Condition, rule.ElseActions.Contains(action), false),
        SetMemoryAction m => new(rule.Id, m.VariableId, m.Value, rule.Condition, rule.ElseActions.Contains(action), true),
        _ => null
    };

    private static ExpressionNode? EffectiveCondition(EffectiveWrite write) => write.IsElse
        ? write.Condition is null ? new ConstantExpression { Value = "false" } : new NotExpression { Operand = write.Condition }
        : write.Condition;

    private static bool SameTarget(LogicAction action, Guid target) => action switch
    {
        SetOutputAction o => o.VariableId == target,
        SetMemoryAction m => m.VariableId == target,
        _ => false
    };

    private static string ActionValue(LogicAction action) => action switch
    {
        SetOutputAction o => BoolLiteral(o.Value),
        SetMemoryAction m => BoolLiteral(m.Value),
        _ => throw new InvalidOperationException("Unsupported action")
    };

    private sealed record Term(IReadOnlyDictionary<Guid, bool> Values);

    private static bool MutuallyExclusive(ExpressionNode? left, ExpressionNode? right)
    {
        var a = ToDnf(left);
        var b = ToDnf(right);
        return a is not null && b is not null && a.All(x => b.All(y => x.Values.Any(p => y.Values.TryGetValue(p.Key, out var value) && value != p.Value)));
    }

    private static List<Term>? ToDnf(ExpressionNode? node)
    {
        if (node is null) return new() { new(new Dictionary<Guid, bool>()) };
        if (node is ConstantExpression c) return bool.TryParse(c.Value, out var value) ? value ? new() { new(new Dictionary<Guid, bool>()) } : new() : null;
        if (node is VariableExpression v) return new() { new(new Dictionary<Guid, bool> { [v.VariableId] = true }) };
        if (node is NotExpression n && n.Operand is VariableExpression nv) return new() { new(new Dictionary<Guid, bool> { [nv.VariableId] = false }) };
        if (node is NotExpression) return null;
        if (node is OrExpression o)
        {
            var result = new List<Term>();
            foreach (var operand in o.Operands) { var terms = ToDnf(operand); if (terms is null) return null; result.AddRange(terms); }
            return result;
        }
        if (node is AndExpression a)
        {
            var result = new List<Term> { new(new Dictionary<Guid, bool>()) };
            foreach (var operand in a.Operands)
            {
                var terms = ToDnf(operand); if (terms is null) return null;
                var next = new List<Term>();
                foreach (var x in result) foreach (var y in terms)
                {
                    var values = new Dictionary<Guid, bool>(x.Values); var compatible = true;
                    foreach (var pair in y.Values) { if (values.TryGetValue(pair.Key, out var old) && old != pair.Value) { compatible = false; break; } values[pair.Key] = pair.Value; }
                    if (compatible) next.Add(new(values));
                }
                result = next;
            }
            return result;
        }
        return null;
    }

    private static string? Expression(ExpressionNode? node, IReadOnlyCollection<VariableDefinition> variables, List<EmitterDiagnostic> diagnostics, Guid elementId)
    {
        switch (node)
        {
            case null: return "TRUE";
            case ConstantExpression c when c.Value.Equals("true", StringComparison.OrdinalIgnoreCase): return "TRUE";
            case ConstantExpression c when c.Value.Equals("false", StringComparison.OrdinalIgnoreCase): return "FALSE";
            case VariableExpression v:
                var variable = variables.FirstOrDefault(x => x.Id == v.VariableId);
                return variable is null ? Unsupported(diagnostics, elementId, "referencia de variable inexistente") : Identifier(variable.Key);
            case NotExpression n: return $"NOT ({Expression(n.Operand, variables, diagnostics, elementId)})";
            case AndExpression a: return string.Join(" AND ", a.Operands.Select(x => $"({Expression(x, variables, diagnostics, elementId)})"));
            case OrExpression o: return string.Join(" OR ", o.Operands.Select(x => $"({Expression(x, variables, diagnostics, elementId)})"));
            default: return Unsupported(diagnostics, elementId, $"expresión {node.GetType().Name}");
        }
    }

    private static string Unsupported(List<EmitterDiagnostic> diagnostics, Guid id, string detail)
    { diagnostics.Add(new("ATLAS-ST-0002", $"Feature no soportada en ST determinista: {detail}.", id)); return "FALSE"; }
    private static string Identifier(string value) => string.IsNullOrWhiteSpace(value) ? "_unnamed" : new string(value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
    private static string Type(VariableDefinition variable) => variable.DataType switch { Domain.Common.PlcDataType.Bool => "BOOL", Domain.Common.PlcDataType.Int16 => "INT", Domain.Common.PlcDataType.UInt16 => "UINT", Domain.Common.PlcDataType.Int32 => "DINT", Domain.Common.PlcDataType.UInt32 => "UDINT", Domain.Common.PlcDataType.Float => "REAL", Domain.Common.PlcDataType.Double => "LREAL", _ => "BOOL" };
    private static string BoolLiteral(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
}
