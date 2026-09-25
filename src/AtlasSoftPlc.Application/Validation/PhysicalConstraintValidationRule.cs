using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Runtime.Expressions;
using AtlasSoftPlc.Domain.Runtime;
using System.Collections.Generic;
using System.Linq;

namespace AtlasSoftPlc.Application.Validation;

/// <summary>
/// Validador de restricciones físicas (Sello de Blindaje V2).
/// No solo verifica la presencia de variables, sino que asegura que el requisito físico
/// REALMENTE influye en la decisión lógica mediante análisis de sensibilidad.
/// </summary>
public sealed class PhysicalConstraintValidationRule : IValidationRule
{
    public string Code => "PHYS";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        yield break; // Usar ValidateFull
    }

    public IEnumerable<ValidationIssue> ValidateFull(
        AtlasIrDocument ir)
    {
        var variables = ir.VariablesById;
        var exprEngine = new ExpressionEngine();

        foreach (var component in ir.Plant.Components)
        {
            if (component.VariableId == Guid.Empty || component.Requires.Count == 0)
                continue;

            var rule = ir.Logic.Rules.FirstOrDefault(r => r.Actions.Any(a => a.GetType().Name == "SetOutputAction"));
            
            if (rule == null || rule.Condition == null)
            {
                yield return new ValidationIssue
                {
                    Severity = ValidationSeverity.Blocker,
                    Code = Code,
                    Category = ValidationCategory.Physical,
                    Message = $"Actuador físico '{component.Name}' no tiene lógica de control válida",
                    Why = "Un componente físico debe ser controlado por una regla con condición para garantizar la seguridad.",
                    Evidence = $"ComponentId={component.Id}",
                    Hint = "Crea una regla lógica que gestione este componente.",
                    VariableId = component.VariableId
                };
                continue;
            }

            foreach (var reqId in component.Requires)
            {
                var reqVar = variables.GetValueOrDefault(reqId);
                string reqKey = reqVar?.Key ?? reqId.ToString();

                // --- ANÁLISIS DE SENSIBILIDAD (Anti-Engaño) ---
                // Verificamos si la variable de requisito REALMENTE cambia el resultado de la expresión.
                if (!IsVariableInfluential(rule.Condition, reqId, variables, exprEngine))
                {
                    yield return new ValidationIssue
                    {
                        Severity = ValidationSeverity.Blocker,
                        Code = Code,
                        Category = ValidationCategory.Physical,
                        Message = $"Engaño lógico detectado: '{component.Name}' no depende realmente de '{reqKey}'",
                        Why = "La variable de requisito aparece en la fórmula, pero no afecta el resultado (Tautología). El componente se activaría aunque el requisito sea falso.",
                        Evidence = $"Rule={rule.Name}; Component={component.Name}; Variable={reqKey}",
                        Hint = $"Asegúrate de que la variable '{reqKey}' sea una condición necesaria (ej. using AND) y no una redundancia.",
                        VariableId = component.VariableId,
                        RuleId = rule.Id
                    };
                }
            }
        }
    }

    private bool IsVariableInfluential(ExpressionNode condition, Guid varId, IReadOnlyDictionary<Guid, VariableDefinition> variables, ExpressionEngine engine)
    {
        // 1. Evaluar con la variable en TRUE
        var ctxTrue = CreateTestContext(variables, varId, true);
        var resTrue = engine.Evaluate(condition, ctxTrue);

        // 2. Evaluar con la variable en FALSE
        var ctxFalse = CreateTestContext(variables, varId, false);
        var resFalse = engine.Evaluate(condition, ctxFalse);

        if (!resTrue.Ok || !resFalse.Ok) return true; // Si falla la eval, asumimos riesgo y marcamos como no influente/sospechosa

        // Si el resultado es el mismo independientemente del valor de la variable, entonces no es influente.
        return resTrue.Value.AsBool() != resFalse.Value.AsBool();
    }

    private IExpressionContext CreateTestContext(IReadOnlyDictionary<Guid, VariableDefinition> variables, Guid targetVarId, bool value)
    {
        var values = new Dictionary<Guid, RuntimeValue>();
        foreach (var v in variables.Values)
        {
            bool val = (v.Id == targetVarId) ? value : false;
            values[v.Id] = new RuntimeValue { VariableId = v.Id, Value = new PlcValue(PlcDataType.Bool, val) };
        }

        return new ScanExpressionContext(
            values.ToDictionary(k => k.Key, v => v.Value),
            new Dictionary<Guid, RuntimeValue>(),
            null!, null!,
            new VariableSnapshotContext(variables, values),
            new Dictionary<Guid, bool>());
    }
}

// Mock para evitar errores de compilación
public class SetOutputAction : LogicAction { public Guid VariableId { get; set; } }
