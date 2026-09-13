using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Web.Tests;

public sealed class GraphValidatorTests
{
    [Fact]
    public void RejectsEdgeToMissingNode()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Start" };
        var graph = new GraphDocument { Nodes = new() { input }, Edges = new() { new GraphEdge { FromNodeId = input.Id, ToNodeId = Guid.NewGuid() } } };
        var report = new GraphValidator().Validate(graph);
        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_EDGE_REF");
        Assert.False(report.IsValid);
    }

    [Fact]
    public void AcceptsValidInputAndOutputConnection()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Start" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Motor" };
        var graph = new GraphDocument { Nodes = new() { input, output }, Edges = new() { new GraphEdge { FromNodeId = input.Id, ToNodeId = output.Id } } };
        Assert.True(new GraphValidator().Validate(graph).IsValid);
    }
}
