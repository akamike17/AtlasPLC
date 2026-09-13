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
            if (CanWrite(program, c.VariableId, true) && CanWrite(program, other.VariableId, true))
                yield return Issue("ATLAS-PLANT-0005", ValidationSeverity.Blocker, "Actuadores mutuamente excluyentes pueden energizarse simultáneamente.", $"{c.Id}<->{other.Id}", "Agrega una condición/interlock que impida el camino simultáneo.", c.VariableId);
        }
    }

    private static bool CanWrite(LogicProgram p, Guid id, bool value) => p.Rules.Any(r => r.Enabled && r.Actions.OfType<SetOutputAction>().Any(a => a.VariableId == id && string.Equals(a.Value, value ? "true" : "false", StringComparison.OrdinalIgnoreCase)));
    private static ValidationIssue Issue(string id, ValidationSeverity severity, string message, string evidence, string hint, Guid variableId) => new()
    {
        DiagnosticId = id, Severity = severity, Code = "PLANT", Category = ValidationCategory.Physical,
        Message = message, Why = "La instalación física debe poder demostrar sus restricciones antes de simular o desplegar.", Evidence = evidence, Hint = hint, VariableId = variableId
    };
}
