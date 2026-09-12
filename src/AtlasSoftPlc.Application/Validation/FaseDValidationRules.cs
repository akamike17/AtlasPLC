using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Validation;

/// <summary>
/// FASE D — Validators deterministas con DiagnosticId + Hint (spec §39 FASE D / §4/§7).
/// Ninguno usa IA: reglas estáticas sobre la IR.
/// </summary>

/// <summary>1. Referencia indefinida: expresión/escritura a variable inexistente.</summary>
public sealed class UndefinedReferenceValidationRule : IValidationRule
{
    public string Code => "UNDEF";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        foreach (var rule in program.Rules)
        {
            foreach (var id in CollectVariableRefs(rule.Condition).Distinct())
            {
                if (!variables.ContainsKey(id))
                {
                    yield return new ValidationIssue
                    {
                        DiagnosticId = "ATLAS-REF-0001",
                        Severity = ValidationSeverity.Blocker,
                        Code = Code,
                        Category = ValidationCategory.Reference,
                        Message = $"La regla '{rule.Name}' referencia una variable que no existe.",
                        Why = "Una referencia a una variable no declarada produce un estado indeterminado en runtime.",
                        Evidence = $"VariableId={id} en condición de regla {rule.Id}",
                        Hint = $"Declara la variable con id {id} o corrige la referencia.",
                        RuleId = rule.Id,
                        VariableId = id,
                    };
                }
            }

            foreach (var action in rule.Actions.Concat(rule.ElseActions))
            {
                Guid? target = action switch
                {
                    SetOutputAction o => o.VariableId,
                    SetMemoryAction m => m.VariableId,
                    ResetMemoryAction rm => rm.VariableId,
                    _ => null,
                };
                if (target is Guid tid && !variables.ContainsKey(tid))
                {
                    yield return new ValidationIssue
                    {
                        DiagnosticId = "ATLAS-REF-0002",
                        Severity = ValidationSeverity.Blocker,
                        Code = Code,
                        Category = ValidationCategory.Reference,
                        Message = $"La regla '{rule.Name}' escribe a una variable que no existe.",
                        Why = "Escribir a una salida/memoria no declarada es un error de modelo.",
                        Evidence = $"VariableId={tid} en acción de regla {rule.Id}",
                        Hint = $"Declara la variable {tid} como Output/Memory antes de escribirla.",
                        RuleId = rule.Id,
                        VariableId = tid,
                    };
                }
            }
        }
    }

    private static IEnumerable<Guid> CollectVariableRefs(ExpressionNode? node)
    {
        if (node is null) yield break;
        switch (node)
        {
            case VariableExpression v:
                yield return v.VariableId;
                break;
            case NotExpression n:
                foreach (var x in CollectVariableRefs(n.Operand)) yield return x;
                break;
            case AndExpression a:
                foreach (var op in a.Operands) foreach (var x in CollectVariableRefs(op)) yield return x;
                break;
            case OrExpression o:
                foreach (var op in o.Operands) foreach (var x in CollectVariableRefs(op)) yield return x;
                break;
            case CompareExpression c:
                foreach (var x in CollectVariableRefs(c.Left)) yield return x;
                foreach (var x in CollectVariableRefs(c.Right)) yield return x;
                break;
            case ArithmeticExpression ar:
                foreach (var x in CollectVariableRefs(ar.Left)) yield return x;
                foreach (var x in CollectVariableRefs(ar.Right)) yield return x;
                break;
            case EdgeExpression e:
                foreach (var x in CollectVariableRefs(e.Operand)) yield return x;
                break;
        }
    }
}

/// <summary>2. Escritor duplicado: dos reglas escriben la misma salida sin arbitraje declarado.</summary>
public sealed class DuplicateWriterValidationRule : IValidationRule
{
    public string Code => "DUPW";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        // Agrupamos reglas por output destino; un output con >1 escritor y misma prioridad es conflicto.
        var writers = new Dictionary<Guid, List<(LogicRule Rule, int Priority)>>();
        var ruleList = program.Rules.Where(r => r.Enabled).ToList();

        foreach (var rule in ruleList)
        {
            foreach (var outId in rule.Actions.OfType<SetOutputAction>().Select(a => a.VariableId).Distinct())
            {
                if (!writers.TryGetValue(outId, out var list))
                    writers[outId] = list = new List<(LogicRule, int)>();
                list.Add((rule, rule.Priority));
            }
        }

        foreach (var (outId, list) in writers)
        {
            // Conflicto si hay >1 escritor al mismo nivel de prioridad máxima.
            var maxPriority = list.Max(x => x.Priority);
            var competing = list.Where(x => x.Priority == maxPriority).ToList();
            if (competing.Count > 1)
            {
                var names = string.Join(", ", competing.Select(c => $"'{c.Rule.Name}'"));
                yield return new ValidationIssue
                {
                    DiagnosticId = "ATLAS-LOGIC-0003",
                    Severity = ValidationSeverity.Blocker,
                    Code = Code,
                    Category = ValidationCategory.Logic,
                    Message = "Salida con múltiples escritores a la misma prioridad (sin arbitraje declarado).",
                    Why = "Dos reglas a igual prioridad compiten por la misma salida; el orden de enumeración no es un criterio válido (spec §5).",
                    Evidence = $"Reglas competidoras: {names}",
                    Hint = "Asigna prioridades distintas o declara un interlock/failsafe explícito para esa salida.",
                    VariableId = outId,
                    ElementId = outId.ToString(),
                };
            }
        }
    }
}

/// <summary>3. SafeState de salida: toda salida SafetyCritical debe tener safe state declarado.</summary>
public sealed class OutputSafeStateValidationRule : IValidationRule
{
    public string Code => "SAFE";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        // IDs con SafeState declarado vienen del contexto (failsafe/SafeStates) vía variables no; aquí usamos
        // una convención: el contexto expone los safe states si el caller los inyecta. Como el contrato
        // IValidationRule no lleva SafeStates, validamos sobre propiedades de la VariableDefinition.
        foreach (var output in variables.Values.Where(v => v.Direction == VariableDirection.Output))
        {
            if (output.SafetyCritical)
            {
                yield return new ValidationIssue
                {
                    DiagnosticId = "ATLAS-SAFE-0004",
                    Severity = ValidationSeverity.Warning,
                    Code = Code,
                    Category = ValidationCategory.Safety,
                    Message = $"La salida '{output.DisplayName}' está marcada SafetyCritical sin un safe state explícito verificable.",
                    Why = "Una salida crítica de seguridad debe declarar su estado seguro (spec §6 'salida con safeState no garantizado').",
                    Evidence = $"VariableId={output.Id}, SafetyCritical=true",
                    Hint = "Declara un SafeState (failsafe) explícito para esta salida y un interlock de seguridad.",
                    VariableId = output.Id,
                };
            }
            else if (variables.Values.Count(v => v.Direction == VariableDirection.Output) > 0 && !HasWriter(program, output.Id))
            {
                yield return new ValidationIssue
                {
                    DiagnosticId = "ATLAS-LOGIC-0005",
                    Severity = ValidationSeverity.Blocker,
                    Code = Code,
                    Category = ValidationCategory.Logic,
                    Message = $"La salida '{output.DisplayName}' no tiene ningún escritor.",
                    Why = "Una salida sin escritor queda en un estado indeterminado (spec §5 'salidas sin escritor').",
                    Evidence = $"VariableId={output.Id} sin SetOutputAction",
                    Hint = "Agrega una regla que escriba esta salida, o declárala como Memory si es interna.",
                    VariableId = output.Id,
                };
            }
        }
    }

    private static bool HasWriter(LogicProgram program, Guid outputId) =>
        program.Rules.Any(r => r.Actions.OfType<SetOutputAction>().Any(a => a.VariableId == outputId));
}

/// <summary>4. Dominancia de interlock: un interlock de paro debe dominar el arranque de su salida.</summary>
public sealed class InterlockDominanceValidationRule : IValidationRule
{
    public string Code => "ILOCK";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        // Sin acceso a los interlocks del programa (están en AtlasIrDocument, no en LogicProgram),
        // validamos la dominancia paro>arranque dentro del propio LogicProgram: la regla de paro
        // (escribe false) debe tener prioridad mayor que la regla de marcha (escribe true).
        var rules = program.Rules.Where(r => r.Enabled).ToList();
        foreach (var outId in rules.SelectMany(r => r.Actions.OfType<SetOutputAction>()).Select(a => a.VariableId).Distinct())
        {
            var starters = rules.Where(r => r.Actions.OfType<SetOutputAction>().Any(a => a.VariableId == outId && a.Value.Equals("true", StringComparison.OrdinalIgnoreCase))).ToList();
            var stoppers = rules.Where(r => r.Actions.OfType<SetOutputAction>().Any(a => a.VariableId == outId && a.Value.Equals("false", StringComparison.OrdinalIgnoreCase))).ToList();

            // Si existe un starter, debe existir un stopper con prioridad >= starter (stop domina start, spec §5).
            if (starters.Count > 0)
            {
                var maxStart = starters.Max(r => r.Priority);
                var maxStop = stoppers.Count > 0 ? stoppers.Max(r => r.Priority) : int.MinValue;
                if (maxStop < maxStart)
                {
                    yield return new ValidationIssue
                    {
                        DiagnosticId = "ATLAS-SAFE-0006",
                        Severity = ValidationSeverity.Warning,
                        Code = Code,
                        Category = ValidationCategory.Safety,
                        Message = "El stop de la salida no domina al start.",
                        Why = "Una condición de paro con menor prioridad que el arranque puede ser ignorada (spec §5 'stop que no domina start').",
                        Evidence = $"Start priority={maxStart}, Stop priority={maxStop}",
                        Hint = "Eleva la prioridad de la regla de paro por encima de la de arranque.",
                        VariableId = outId,
                    };
                }
            }
        }
    }
}

/// <summary>5. Expresión booleana contradictoria: X AND NOT X (siempre falsa) u X OR NOT X (siempre verdadera).</summary>
public sealed class ContradictoryExpressionValidationRule : IValidationRule
{
    public string Code => "CONTRAD";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        foreach (var rule in program.Rules.Where(r => r.Enabled))
        {
            var result = AnalyzeContradiction(rule.Condition);
            if (result is null) continue;

            var (kind, evidence) = result.Value;
            var severity = kind == ContradictionKind.AlwaysFalse
                ? ValidationSeverity.Blocker
                : ValidationSeverity.Warning;

            yield return new ValidationIssue
            {
                DiagnosticId = kind == ContradictionKind.AlwaysFalse ? "ATLAS-LOGIC-0007" : "ATLAS-LOGIC-0008",
                Severity = severity,
                Code = Code,
                Category = ValidationCategory.Logic,
                Message = kind == ContradictionKind.AlwaysFalse
                    ? $"La condición de la regla '{rule.Name}' es contradictoria (siempre falsa)."
                    : $"La condición de la regla '{rule.Name}' es tautológica (siempre verdadera).",
                Why = "Una condición siempre falsa produce una rama inalcanzable; una siempre verdadera es señal de error de modelado.",
                Evidence = evidence,
                Hint = "Revisa la combinación X AND NOT X (o X OR NOT X) en la condición.",
                RuleId = rule.Id,
            };
        }
    }

    private enum ContradictionKind { AlwaysFalse, AlwaysTrue }

    private static (ContradictionKind, string)? AnalyzeContradiction(ExpressionNode? node)
    {
        if (node is null) return null;

        // X AND NOT X → siempre falso;  X OR NOT X → siempre verdadero.
        if (node is AndExpression and)
        {
            var present = new HashSet<Guid>();
            var negated = new HashSet<Guid>();
            foreach (var op in and.Operands)
            {
                if (op is VariableExpression v) present.Add(v.VariableId);
                else if (op is NotExpression { Operand: VariableExpression nv }) negated.Add(nv.VariableId);
            }
            var conflict = present.Intersect(negated).FirstOrDefault();
            if (conflict != default)
                return (ContradictionKind.AlwaysFalse, $"Variable {conflict} aparece como X y NOT X en el mismo AND");
        }
        else if (node is OrExpression or)
        {
            var present = new HashSet<Guid>();
            var negated = new HashSet<Guid>();
            foreach (var op in or.Operands)
            {
                if (op is VariableExpression v) present.Add(v.VariableId);
                else if (op is NotExpression { Operand: VariableExpression nv }) negated.Add(nv.VariableId);
            }
            var conflict = present.Intersect(negated).FirstOrDefault();
            if (conflict != default)
                return (ContradictionKind.AlwaysTrue, $"Variable {conflict} aparece como X y NOT X en el mismo OR");
        }

        // Recurrir en opers anidados.
        if (node is NotExpression not)
        {
            return AnalyzeContradiction(not.Operand) switch
            {
                (ContradictionKind.AlwaysFalse, var e) => (ContradictionKind.AlwaysTrue, e),
                (ContradictionKind.AlwaysTrue, var e) => (ContradictionKind.AlwaysFalse, e),
                _ => null,
            };
        }
        if (node is AndExpression and2)
        {
            foreach (var op in and2.Operands)
            {
                var r = AnalyzeContradiction(op);
                if (r is not null) return r;
            }
        }
        if (node is OrExpression or2)
        {
            foreach (var op in or2.Operands)
            {
                var r = AnalyzeContradiction(op);
                if (r is not null) return r;
            }
        }
        return null;
    }
}

/// <summary>6. Rama inalcanzable: una regla cuya condición es siempre falsa (por contradicción).</summary>
public sealed class UnreachableBranchValidationRule : IValidationRule
{
    public string Code => "UNREACH";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        // Una regla con condición contradictoria (siempre falsa) es inalcanzable.
        var contradictory = new ContradictoryExpressionValidationRule();
        foreach (var issue in contradictory.Validate(program, variables, context))
        {
            if (issue.DiagnosticId == "ATLAS-LOGIC-0007") // siempre falsa
            {
                yield return new ValidationIssue
                {
                    DiagnosticId = "ATLAS-LOGIC-0009",
                    Severity = ValidationSeverity.Blocker,
                    Code = Code,
                    Category = ValidationCategory.ControlFlow,
                    Message = $"La regla '{issue.RuleId}' es inalcanzable (condición siempre falsa).",
                    Why = "Una rama siempre falsa nunca produce salida y confunde la intención (spec §5 'ramas inalcanzables').",
                    Evidence = issue.Evidence,
                    Hint = "Elimina la contradicción o elimina la regla muerta.",
                    RuleId = issue.RuleId,
                };
            }
        }
    }
}