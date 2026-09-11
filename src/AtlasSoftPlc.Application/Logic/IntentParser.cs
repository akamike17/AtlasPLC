using AtlasSoftPlc.Domain.Intent;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>
/// Parser de intención en lenguaje natural (sección 20). Heurístico y
/// determinista; cuando no puede interpretar, devuelve null para que el
/// asistente guiado tome el control. La IA (si existe) solo propone.
/// </summary>
public sealed class IntentParser
{
    private readonly IReadOnlyDictionary<Guid, VariableDefinition> _variables;

    public IntentParser(IReadOnlyDictionary<Guid, VariableDefinition> variables)
    {
        _variables = variables;
    }

    public AutomationIntent? Parse(string text, string user)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var intent = new AutomationIntent { RawText = text, CreatedBy = user };
        var lower = text.ToLowerInvariant();

        var variables = _variables.Values.ToList();

        // Detectar acciones: "enciende X" / "apaga X" / "activa X" - SOLO para OUTPUTS
        var actions = new List<string>();
        foreach (var v in variables.Where(x => x.Direction == Domain.Common.VariableDirection.Output))
        {
            if (string.IsNullOrWhiteSpace(v.DisplayName)) continue;
            var name = v.DisplayName.ToLowerInvariant();
            if (lower.Contains("enciend") && lower.Contains(name))
                actions.Add($"{v.DisplayName} = true");
            else if (lower.Contains("apag") && lower.Contains(name))
                actions.Add($"{v.DisplayName} = false");
            else if (lower.Contains("activ") && lower.Contains(name))
                actions.Add($"{v.DisplayName} = true");
            else if (lower.Contains("desactiv") && lower.Contains(name))
                actions.Add($"{v.DisplayName} = false");
        }

        // Detectar condiciones: "cuando X" / "si X" con referencias a INPUTS
        var conditions = new List<string>();
        var stopConditions = new List<string>();
        foreach (var v in variables.Where(x => x.Direction == Domain.Common.VariableDirection.Input))
        {
            if (string.IsNullOrWhiteSpace(v.DisplayName)) continue;
            var name = v.DisplayName.ToLowerInvariant();
            if (lower.Contains(name))
            {
                var negated = lower.Contains("no " + name) || lower.Contains(name + " no");
                var condition = negated ? $"not {v.DisplayName}" : v.DisplayName;
                
                // Check if this is part of a stop clause ("apaga si X", "para si X", "detente si X", etc.)
                var isStopClause = lower.Contains("apaga si " + name) 
                                 || lower.Contains("para si " + name)
                                 || lower.Contains("detente si " + name)
                                 || lower.Contains("apaga cuando " + name)
                                 || lower.Contains("para cuando " + name)
                                 || lower.Contains("detente cuando " + name)
                                 || lower.Contains("apaga el " + name + " cuando")
                                 || lower.Contains("para el " + name + " cuando")
                                 || lower.Contains("detente el " + name + " cuando")
                                 // Also check for "apaga [output] cuando [input]" pattern
                                 || lower.Contains("cuando " + name);
                
                if (isStopClause)
                    stopConditions.Add(condition);
                else
                    conditions.Add(condition);
            }
        }

        if (actions.Count == 0)
            return null; // no pudimos interpretar acción

        intent.Actions = actions.Distinct().ToList();
        intent.Conditions = conditions.Distinct().ToList();
        intent.StopConditions = stopConditions.Distinct().ToList();

        // trigger: primera condición que parezca un evento
        if (conditions.Count > 0)
            intent.Trigger = conditions[0];

        return intent;
    }
}