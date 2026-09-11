using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

/// <summary>
/// Explicaciones generadas desde el IR (sección 46). Sin texto hardcodeado.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ExplainerController : ControllerBase
{
    private readonly SimulationService _sim;

    public ExplainerController(SimulationService sim) => _sim = sim;

    /// <summary>Devuelve explicación humana de por qué una salida está en su estado actual.</summary>
    [HttpGet("output/{variableId}")]
    public IActionResult ExplainOutput(Guid variableId)
    {
        var program = _sim.ActiveProgram;
        if (program is null) return NotFound(new { error = "Sin programa activo." });

        var varDef = _sim.Variables.Values.FirstOrDefault(v => v.Id == variableId);
        if (varDef is null) return NotFound(new { error = "Variable no encontrada." });

        var explanations = new List<string>();
        foreach (var rule in program.Rules.Where(r => r.Enabled))
        {
            var setsThis = rule.Actions.OfType<SetOutputAction>()
                .Any(a => a.VariableId == variableId);
            if (!setsThis) continue;

            // Evaluar condición (simplificado: si la regla disparó en el último scan)
            var conditionText = DescribeCondition(rule.Condition);
            var actionText = rule.Actions.OfType<SetOutputAction>()
                .First(a => a.VariableId == variableId).Value;

            explanations.Add($"Regla \"{rule.Name}\": {conditionText} → salida = {actionText}");
        }

        return Ok(new
        {
            variable = varDef.DisplayName,
            active = explanations.Count > 0,
            reasons = explanations
        });
    }

    private static string DescribeCondition(ExpressionNode? node) =>
        node switch
        {
            null => "siempre",
            VariableExpression v => $"“{v.VariableKey}” es TRUE",
            NotExpression n => $"NO ({DescribeCondition(n.Operand)})",
            AndExpression a => string.Join(" Y ", a.Operands.Select(DescribeCondition)),
            OrExpression o => string.Join(" O ", o.Operands.Select(DescribeCondition)),
            CompareExpression c => $"{c.Left} {c.Operator} {c.Right}",
            _ => node.GetType().Name
        };
}