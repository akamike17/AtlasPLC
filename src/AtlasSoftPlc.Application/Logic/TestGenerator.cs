using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>
/// Genera casos de prueba a partir de las reglas evitando explosión
/// combinatoria (sección 25).
/// </summary>
public sealed class TestGenerator
{
    public sealed class TestCase
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<Guid, bool> Inputs { get; set; } = new();
        public bool ExpectedOutput { get; set; }
        public Guid OutputVariableId { get; set; }
    }

    private readonly IReadOnlyDictionary<Guid, VariableDefinition> _variables;

    public TestGenerator(IReadOnlyDictionary<Guid, VariableDefinition> variables)
    {
        _variables = variables;
    }

    public List<TestCase> Generate(LogicProgram program)
    {
        var cases = new List<TestCase>();
        foreach (var rule in program.Rules)
        {
            var outputActions = rule.Actions.OfType<SetOutputAction>().ToList();
            if (outputActions.Count == 0) continue;

            var inputVars = CollectVariables(rule.Condition);
            var outputVar = outputActions[0].VariableId;

            // caso base: todas las entradas true (condición simple satisfecha)
            var allTrue = new Dictionary<Guid, bool>();
            foreach (var iv in inputVars) allTrue[iv] = true;
            cases.Add(new TestCase { Name = $"{rule.Name} — todas activas", Inputs = allTrue, ExpectedOutput = true, OutputVariableId = outputVar });

            // flip one at a time
            foreach (var iv in inputVars)
            {
                var one = new Dictionary<Guid, bool>();
                foreach (var v in inputVars) one[v] = v == iv ? false : true;
                cases.Add(new TestCase { Name = $"{rule.Name} — {iv:N} apagada", Inputs = one, ExpectedOutput = false, OutputVariableId = outputVar });
            }
        }
        return cases;
    }

    private static List<Guid> CollectVariables(ExpressionNode? node)
    {
        var result = new List<Guid>();
        if (node is null) return result;

        switch (node)
        {
            case VariableExpression v:
                result.Add(v.VariableId);
                break;
            case NotExpression n:
                result.AddRange(CollectVariables(n.Operand));
                break;
            case AndExpression a:
                foreach (var op in a.Operands) result.AddRange(CollectVariables(op));
                break;
            case OrExpression o:
                foreach (var op in o.Operands) result.AddRange(CollectVariables(op));
                break;
            case CompareExpression c:
                result.AddRange(CollectVariables(c.Left));
                result.AddRange(CollectVariables(c.Right));
                break;
            case EdgeExpression e:
                result.AddRange(CollectVariables(e.Operand));
                break;
        }
        return result.Distinct().ToList();
    }
}