using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Graph;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Graph;

public interface IGraphLowerer
{
    PlcProgramDefinition Lower(GraphDocument graph, GraphValidationReport validation);
}

/// <summary>Traduce el documento visual a IR; no ejecuta código generado desde la UI.</summary>
public sealed class GraphLowerer : IGraphLowerer
{
    public PlcProgramDefinition Lower(GraphDocument graph, GraphValidationReport validation)
    {
        if (!validation.IsValid) throw new InvalidOperationException("No se puede traducir un gráfico inválido.");
        var result = new PlcProgramDefinition { Id = graph.ProgramId == Guid.Empty ? Guid.NewGuid() : graph.ProgramId, Name = "Programa gráfico", Variables = new() };
        var variableByNode = new Dictionary<Guid, VariableDefinition>();
        foreach (var node in graph.Nodes.Where(x => x.Kind is GraphNodeKind.Input or GraphNodeKind.Output or GraphNodeKind.Memory))
        {
            var variable = new VariableDefinition { Id = node.Id, Key = node.Name, DisplayName = node.Name, Direction = node.Kind == GraphNodeKind.Input ? VariableDirection.Input : node.Kind == GraphNodeKind.Output ? VariableDirection.Output : VariableDirection.Memory, DataType = PlcDataType.Bool };
            result.Variables.Add(variable); variableByNode[node.Id] = variable;
            if (variable.Direction == VariableDirection.Output) result.Failsafe[variable.Id] = AtlasSoftPlc.Domain.Values.PlcValue.Bool(false);
        }
        foreach (var output in graph.Nodes.Where(x => x.Kind == GraphNodeKind.Output))
        {
            var source = graph.Edges.FirstOrDefault(x => x.ToNodeId == output.Id);
            if (source is null || !variableByNode.TryGetValue(output.Id, out var target)) continue;
            var expression = BuildExpression(source.FromNodeId, graph, variableByNode, new HashSet<Guid>());
            result.Logic.Rules.Add(new LogicRule { Name = $"{source.FromNodeId} activa {output.Name}", Priority = 100, Condition = expression, Actions = new List<LogicAction> { new SetOutputAction { VariableId = target.Id, Value = "true" } }, ElseActions = new List<LogicAction> { new SetOutputAction { VariableId = target.Id, Value = "false" } }, SourceIntent = "GraphEditor" });
        }
        return result;
    }

    private static ExpressionNode BuildExpression(Guid nodeId, GraphDocument graph, IReadOnlyDictionary<Guid, VariableDefinition> variables, HashSet<Guid> visiting)
    {
        if (!visiting.Add(nodeId)) throw new InvalidOperationException("El gráfico contiene un ciclo inválido.");
        var node = graph.Nodes.Single(x => x.Id == nodeId);
        if (variables.TryGetValue(nodeId, out var variable)) return new VariableExpression { VariableId = variable.Id, VariableKey = variable.Key };
        var incoming = graph.Edges.Where(x => x.ToNodeId == nodeId).ToList();
        ExpressionNode result = node.Kind switch
        {
            GraphNodeKind.Not when incoming.Count == 1 => new NotExpression { Operand = BuildExpression(incoming[0].FromNodeId, graph, variables, visiting) },
            GraphNodeKind.And => new AndExpression { Operands = incoming.Select(x => BuildExpression(x.FromNodeId, graph, variables, visiting)).ToList() },
            GraphNodeKind.Or => new OrExpression { Operands = incoming.Select(x => BuildExpression(x.FromNodeId, graph, variables, visiting)).ToList() },
            _ => throw new InvalidOperationException($"El tipo de nodo '{node.Kind}' aún no tiene lowering implementado.")
        };
        visiting.Remove(nodeId); return result;
    }
}
