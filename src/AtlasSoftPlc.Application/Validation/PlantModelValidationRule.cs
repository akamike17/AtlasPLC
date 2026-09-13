using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Validation;

/// <summary>Validación física mínima: referencias, requisitos, exclusiones y estados seguros.</summary>
public sealed class PlantModelValidationRule : IValidationRule
{
    public string Code => "PLANT";
    private readonly PlantModel _plant;

    public PlantModelValidationRule(PlantModel plant) => _plant = plant ?? throw new ArgumentNullException(nameof(plant));

    public IEnumerable<ValidationIssue> Validate(LogicProgram program, IReadOnlyDictionary<Guid, VariableDefinition> variables, ValidationContext context)
    {
        var byId = _plant.Components.ToDictionary(c => c.Id);
        foreach (var c in _plant.Components)
        {
            if (!variables.ContainsKey(c.VariableId))
                yield return Issue("ATLAS-PLANT-0001", ValidationSeverity.Blocker, "Referencia de planta inexistente.", $"ComponentId={c.Id}, VariableId={c.VariableId}", "Declara la variable o corrige el componente.", c.VariableId);

            if (c.RequiresPhysicalPermission && c.Requires.Count == 0)
                yield return Issue("ATLAS-PLANT-0006", ValidationSeverity.Blocker, "Actuador energizable sin requisito físico obligatorio.", $"ComponentId={c.Id}, VariableId={c.VariableId}, Requires vacío", "Declara al menos un componente requerido o desactiva explícitamente esta restricción.", c.VariableId);

            foreach (var requirement in c.Requires)
                if (!byId.ContainsKey(requirement))
                    yield return Issue("ATLAS-PLANT-0002", ValidationSeverity.Blocker, "Requisito físico inexistente.", $"ComponentId={c.Id}, Requires={requirement}", "Declara el componente requerido.", c.VariableId);

            foreach (var requirement in c.Requires)
            {
                if (!c.RequirementBindings.TryGetValue(requirement, out var permission))
                {
                    var severity = c.RequiresPhysicalPermission ? ValidationSeverity.Blocker : ValidationSeverity.Warning;
                    yield return Issue("ATLAS-PLANT-0011", severity, "Requisito físico declarado sin binding lógico verificable.", $"ComponentId={c.Id}, RequirementId={requirement}", "Vincula el requisito a una variable lógica verificable.", c.VariableId);
                    continue;
                }
                if (!variables.ContainsKey(permission))
                {
                    yield return Issue("ATLAS-PLANT-0007", ValidationSeverity.Blocker, "Binding lógico del requisito inexistente.", $"ComponentId={c.Id}, Requirement={requirement}, PermissionVariable={permission}", "Declara la variable de permiso o corrige el binding.", c.VariableId);
                    continue;
                }

                foreach (var rule in program.Rules.Where(r => r.Enabled && r.Actions.OfType<SetOutputAction>().Any(a => a.VariableId == c.VariableId && a.Value.Equals("true", StringComparison.OrdinalIgnoreCase))))
                {
                    var proof = ProveRequired(rule.Condition, permission);
                    if (proof == Proof.Missing)
                        yield return Issue("ATLAS-PLANT-0008", ValidationSeverity.Blocker, "Camino de energización ignora el permiso requerido.", $"ComponentId={c.Id}, RuleId={rule.Id}, PermissionVariable={permission}", "Incluye el permiso requerido en la condición de energización.", c.VariableId);
                    else if (proof == Proof.Indeterminate)
                        yield return Issue("ATLAS-PLANT-0009", ValidationSeverity.Warning, "No se pudo demostrar el permiso requerido en la condición.", $"ComponentId={c.Id}, RuleId={rule.Id}, PermissionVariable={permission}", "Reescribe la condición usando Variable, Not, And u Or para hacer verificable el permiso.", c.VariableId);
                }
            }

            if (c.RequiresSafeState && c.SafeState is null)
                yield return Issue("ATLAS-PLANT-0003", ValidationSeverity.Blocker, "Actuador sin estado seguro obligatorio.", $"ComponentId={c.Id}, VariableId={c.VariableId}", "Declara SafeState para el actuador.", c.VariableId);

            foreach (var other in c.MutuallyExclusiveWith)
                if (!byId.ContainsKey(other))
                    yield return Issue("ATLAS-PLANT-0004", ValidationSeverity.Blocker, "Exclusión mutua referencia un componente inexistente.", $"ComponentId={c.Id}, Exclusive={other}", "Corrige la referencia física.", c.VariableId);
        }

        foreach (var c in _plant.Components)
        foreach (var otherId in c.MutuallyExclusiveWith.Where(byId.ContainsKey).Distinct())
        {
            var other = byId[otherId];
            var left = EnergizingConditions(program, c.VariableId).ToList();
            var right = EnergizingConditions(program, other.VariableId).ToList();
            var indeterminate = left.Any(x => x is null) || right.Any(x => x is null);
            if (indeterminate)
                yield return Issue("ATLAS-PLANT-0010", ValidationSeverity.Warning, "No se pudo determinar si la exclusión mutua puede solaparse.", $"{c.Id}<->{other.Id}", "Usa condiciones booleanas del subset verificable o declara un interlock explícito.", c.VariableId);
            else if (left.Where(x => x is not null).SelectMany(x => right.Where(y => y is not null), (a, b) => (a!, b!)).SelectMany(pair => pair.Item1.SelectMany(a => pair.Item2.Select(b => (a, b)))).Any(pair => Compatible(pair.a, pair.b)))
                yield return Issue("ATLAS-PLANT-0005", ValidationSeverity.Blocker, "Actuadores mutuamente excluyentes pueden energizarse simultáneamente.", $"{c.Id}<->{other.Id}", "Agrega una condición/interlock que impida el camino simultáneo.", c.VariableId);
        }
    }

    private enum Proof { Missing, Present, Indeterminate }
    private sealed record Literal(Guid Id, bool Positive);
    private static Proof ProveRequired(ExpressionNode? node, Guid id)
    {
        var terms = ToDnf(node);
        if (terms is null) return Proof.Indeterminate;
        return terms.All(term => term.Any(l => l.Id == id && l.Positive)) ? Proof.Present : Proof.Missing;
    }
    private static List<List<Literal>>? ToDnf(ExpressionNode? node) => node switch
    {
        VariableExpression v => Single(v.VariableId, true),
        NotExpression { Operand: VariableExpression v } => Single(v.VariableId, false),
        AndExpression a => And(a.Operands.Select(ToDnf)),
        OrExpression o => Or(o.Operands.Select(ToDnf)),
        ConstantExpression c when c.Value.Equals("true", StringComparison.OrdinalIgnoreCase) => new List<List<Literal>> { new() },
        ConstantExpression c when c.Value.Equals("false", StringComparison.OrdinalIgnoreCase) => new List<List<Literal>>(),
        _ => null
    };
    private static List<List<Literal>> Single(Guid id, bool positive) => new() { new List<Literal> { new(id, positive) } };
    private static List<List<Literal>>? And(IEnumerable<List<List<Literal>>?> parts)
    {
        var materialized = parts.ToList();
        if (materialized.Any(x => x is null)) return null;
        var result = new List<List<Literal>> { new() };
        foreach (var part in materialized.Cast<List<List<Literal>>>()) result = result.SelectMany(a => part.Select(b => a.Concat(b).ToList())).Where(IsConsistent).ToList();
        return result;
    }
    private static List<List<Literal>>? Or(IEnumerable<List<List<Literal>>?> parts)
    {
        var materialized = parts.ToList();
        return materialized.Any(x => x is null) ? null : materialized.SelectMany(x => x!).ToList();
    }
    private static bool IsConsistent(List<Literal> term) => term.GroupBy(x => x.Id).All(g => g.Select(x => x.Positive).Distinct().Count() == 1);
    private static bool Compatible(List<Literal> a, List<Literal> b) => IsConsistent(a.Concat(b).ToList());
    private static IEnumerable<List<List<Literal>>?> EnergizingConditions(LogicProgram p, Guid id) => p.Rules.Where(r => r.Enabled && r.Actions.OfType<SetOutputAction>().Any(a => a.VariableId == id && a.Value.Equals("true", StringComparison.OrdinalIgnoreCase))).Select(r => ToDnf(r.Condition));
    private static ValidationIssue Issue(string id, ValidationSeverity severity, string message, string evidence, string hint, Guid variableId) => new()
    {
        DiagnosticId = id, Severity = severity, Code = "PLANT", Category = ValidationCategory.Physical,
        Message = message, Why = "La instalación física debe poder demostrar sus restricciones antes de simular o desplegar.", Evidence = evidence, Hint = hint, VariableId = variableId
    };
}
