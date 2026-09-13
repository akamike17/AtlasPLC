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
    public StructuredTextArtifact Emit(PlcProgramDefinition program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var lines = new List<string> { "(* AtlasPLC deterministic ST subset *)", "PROGRAM AtlasProgram", "VAR" };
        foreach (var variable in program.Variables.OrderBy(v => v.Key, StringComparer.Ordinal))
            lines.Add($"    {Identifier(variable.Key)} : {Type(variable)};");
        lines.Add("END_VAR");
        var map = new List<SourceMapEntry>();
        var diagnostics = new List<EmitterDiagnostic>();
        foreach (var rule in program.Logic.Rules.Where(r => r.Enabled))
        {
            foreach (var action in rule.Actions)
            {
                if (action is not SetOutputAction and not SetMemoryAction)
                {
                    diagnostics.Add(new("ATLAS-ST-0001", $"Acción no soportada por el subset ST: {action.GetType().Name}.", action.Id));
                    continue;
                }
                var target = action is SetOutputAction output ? output.VariableId : ((SetMemoryAction)action).VariableId;
                var variable = program.Variables.FirstOrDefault(v => v.Id == target);
                var expression = Expression(rule.Condition, program.Variables, diagnostics, rule.Id);
                if (variable is null || expression is null) continue;
                lines.Add($"    IF {expression} THEN {Identifier(variable.Key)} := {BoolLiteral(action is SetOutputAction o ? o.Value : ((SetMemoryAction)action).Value)}; END_IF;");
                map.Add(new(rule.Id, lines.Count));
            }
        }
        lines.Add("END_PROGRAM");
        var source = string.Join("\n", lines) + "\n";
        return new StructuredTextArtifact { Source = source, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant(), SourceMap = map, Diagnostics = diagnostics };
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
