using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Web.Tests;

public sealed class GraphLowererTests
{
    [Fact]
    public void LowersAndGraphToExecutableRule()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Start" };
        var and = new GraphNode { Kind = GraphNodeKind.And, Name = "Permit" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Motor" };
        var graph = new GraphDocument { Nodes = new() { input, and, output }, Edges = new() { new GraphEdge { FromNodeId = input.Id, ToNodeId = and.Id }, new GraphEdge { FromNodeId = and.Id, ToNodeId = output.Id } } };
        var validation = new GraphValidator().Validate(graph);
        var program = new GraphLowerer().Lower(graph, validation);
        Assert.True(validation.IsValid);
        Assert.Single(program.Logic.Rules);
        Assert.Equal("Motor", program.Variables.Single(x => x.Direction == AtlasSoftPlc.Domain.Common.VariableDirection.Output).Key);
    }
}
