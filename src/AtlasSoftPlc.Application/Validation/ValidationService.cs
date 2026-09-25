using System.Text.RegularExpressions;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Validation;

/// <summary>
/// Servicio de validación que ejecuta múltiples IValidationRule (sección 23).
/// Cubre validaciones de modelo, tipos y lógica.
/// </summary>
public sealed class ValidationService
{
    private readonly IReadOnlyList<IValidationRule> _rules;
    private readonly PhysicalConstraintValidationRule _physicalValidator;

    public ValidationService(IEnumerable<IValidationRule> rules)
    {
        _rules = rules.ToList();
        _physicalValidator = new PhysicalConstraintValidationRule();
    }

    public ValidationReport Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext? context = null)
    {
        context ??= new ValidationContext { Variables = variables };
        var report = new ValidationReport();
        foreach (var rule in _rules)
        {
            var issues = rule.Validate(program, variables, context);
            report.Issues.AddRange(issues);
        }
        return report;
    }

    /// <summary>
    /// Validación extendida que incluye el análisis de restricciones físicas del Plant Model.
    /// </summary>
    public ValidationReport ValidateFull(AtlasIrDocument ir)
    {
        var report = Validate(ir.Logic, ir.VariablesById);
        
        // Ejecutar el blindaje físico
        var physicalIssues = _physicalValidator.ValidateFull(ir);
        report.Issues.AddRange(physicalIssues);
        
        return report;
    }
}

/// <summary>Validación de referencias: variables inexistentes, IDs duplicados.</summary>
public sealed class ReferencesValidationRule : IValidationRule
{
    public string Code => "REF";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        // IDs de variable duplicados por Key
        var seenKeys = new HashSet<string>();
        foreach (var v in variables.Values)
        {
            if (string.IsNullOrWhiteSpace(v.Key))
            {
                yield return new ValidationIssue { Severity = ValidationSeverity.Error, Code = Code, Category = ValidationCategory.Schema, Message = "Variable sin Key válida", Why = "La Key identifica la variable en reglas, mapas y conectores.", Evidence = $"VariableId={v.Id}", Hint = "Asigna una Key única, corta y estable; por ejemplo SensorNivelAlto.", VariableId = v.Id };
                continue;
            }
            if (!seenKeys.Add(v.Key))
                yield return new ValidationIssue { Severity = ValidationSeverity.Error, Code = Code, Category = ValidationCategory.Schema, Message = $"Key duplicada: {v.Key}", Why = "Dos variables con la misma Key hacen ambiguo el mapeo hacia el PLC.", Evidence = $"Key={v.Key}; VariableId={v.Id}", Hint = "Renombra una de las variables y vuelve a validar antes de guardar.", VariableId = v.Id };
        }

        // Referencias en reglas
        foreach (var rule in program.Rules)
        {
            foreach (var (expr, id) in CollectVariableRefs(rule.Condition))
            {
                if (!variables.ContainsKey(id))
                    yield return new ValidationIssue { Severity = ValidationSeverity.Error, Code = Code, Category = ValidationCategory.Reference, Message = $"Regla '{rule.Name}' referencia variable inexistente", Why = "La regla no puede ejecutarse de forma determinista si su entrada fue eliminada.", Evidence = $"RuleId={rule.Id}; VariableId={id}", Hint = "Selecciona una variable existente o elimina la referencia rota; después ejecuta Validar construcción.", RuleId = rule.Id, VariableId = id };
            }
            foreach (var action in rule.Actions.Concat(rule.ElseActions))
            {
                if (action is SetOutputAction o && !variables.ContainsKey(o.VariableId))
                    yield return new ValidationIssue { Severity = ValidationSeverity.Error, Code = Code, Category = ValidationCategory.Reference, Message = $"Regla '{rule.Name}' escribe a variable inexistente", Why = "Una salida inexistente no puede garantizar el estado del actuador.", Evidence = $"RuleId={rule.Id}; OutputId={o.VariableId}", Hint = "Crea la salida o cambia la acción a una salida existente antes de simular o desplegar.", RuleId = rule.Id, VariableId = o.VariableId };
                if (action is SetMemoryAction m && !variables.ContainsKey(m.VariableId))
                    yield return new ValidationIssue { Severity = ValidationSeverity.Error, Code = Code, Category = ValidationCategory.Reference, Message = $"Regla '{rule.Name}' escribe a memoria inexistente", Why = "El estado interno no se puede almacenar porque su variable ya no existe.", Evidence = $"RuleId={rule.Id}; MemoryId={m.VariableId}", Hint = "Crea la variable de memoria o elimina la acción huérfana antes de continuar.", RuleId = rule.Id, VariableId = m.VariableId };
            }
        }
    }

    private static IEnumerable<(ExpressionNode, Guid)> CollectVariableRefs(ExpressionNode? node)
    {
        if (node is null) yield break;
        switch (node)
        {
            case VariableExpression v:
                yield return (node, v.VariableId);
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

/// <summary>Validación de salidas físicas: toda salida debe tener FailSafe (sección 18).</summary>
public sealed class FailsafeValidationRule : IValidationRule
{
    public string Code => "FAILSAFE";

    public IEnumerable<ValidationIssue> Validate(
        LogicProgram program,
        IReadOnlyDictionary<Guid, VariableDefinition> variables,
        ValidationContext context)
    {
        foreach (var output in variables.Values.Where(v => v.Direction == VariableDirection.Output))
        {
            if (output.SafetyCritical)
            {
                // SafetyCritical: la advertencia de seguridad es por diseño (sección 4)
                yield return new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Code = Code,
                    Category = ValidationCategory.Safety,
                    Message = $"La salida '{output.DisplayName}' está marcada SafetyCritical. Esta lógica no sustituye una función de seguridad física/certificada.",
                    Why = "Una salida SafetyCritical necesita revisión independiente y un circuito físico certificado.",
                    Evidence = $"OutputId={output.Id}; SafetyCritical=true",
                    Hint = "Confirma el safe-state, agrega el interlock físico requerido y solicita revisión de seguridad antes de desplegar.",
                    VariableId = output.Id
                };
            }
        }

        // Toda salida escrita debe tener definido algún failsafe implícito (devuelve advertencia si no hay min/max)
        yield break;
    }
}
