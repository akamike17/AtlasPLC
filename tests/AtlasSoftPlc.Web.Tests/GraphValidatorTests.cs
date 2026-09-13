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

    [Fact]
    public void RejectsUnsupportedVisualBlockBeforeLowering()
    {
        var graph = new GraphDocument { Nodes = new() { new GraphNode { Kind = GraphNodeKind.Compare, Name = "Comparador" } } };

        var report = new GraphValidator().Validate(graph);

        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_KIND_UNSUPPORTED");
        Assert.False(report.IsValid);
    }

    [Fact]
    public void DetectsIndirectCycle()
    {
        var a = new GraphNode { Kind = GraphNodeKind.And, Name = "A" };
        var b = new GraphNode { Kind = GraphNodeKind.And, Name = "B" };
        var one = new GraphNode { Kind = GraphNodeKind.Input, Name = "One" };
        var two = new GraphNode { Kind = GraphNodeKind.Input, Name = "Two" };
        var graph = new GraphDocument
        {
            Nodes = new() { one, two, a, b },
            Edges = new()
            {
                new() { FromNodeId = one.Id, ToNodeId = a.Id },
                new() { FromNodeId = two.Id, ToNodeId = a.Id },
                new() { FromNodeId = a.Id, ToNodeId = b.Id },
                new() { FromNodeId = one.Id, ToNodeId = b.Id },
                new() { FromNodeId = b.Id, ToNodeId = a.Id }
            }
        };
        Assert.Contains(new GraphValidator().Validate(graph).Diagnostics, x => x.Code == "GRAPH_CYCLE");
    }

    [Fact]
    public void AndRequiresTwoInputs()
    {
        var source = new GraphNode { Kind = GraphNodeKind.Input, Name = "Input" };
        var and = new GraphNode { Kind = GraphNodeKind.And, Name = "And" };
        var report = new GraphValidator().Validate(new GraphDocument
        {
            Nodes = new() { source, and },
            Edges = new() { new() { FromNodeId = source.Id, ToNodeId = and.Id } }
        });
        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_AND_ARITY");
    }

    [Fact]
    public void OrRequiresTwoInputs()
    {
        var source = new GraphNode { Kind = GraphNodeKind.Input, Name = "Input" };
        var or = new GraphNode { Kind = GraphNodeKind.Or, Name = "Or" };
        var report = new GraphValidator().Validate(new GraphDocument
        {
            Nodes = new() { source, or },
            Edges = new() { new() { FromNodeId = source.Id, ToNodeId = or.Id } }
        });
        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_OR_ARITY");
    }

    [Fact]
    public void NotRequiresExactlyOneInput()
    {
        var one = new GraphNode { Kind = GraphNodeKind.Input, Name = "One" };
        var two = new GraphNode { Kind = GraphNodeKind.Input, Name = "Two" };
        var not = new GraphNode { Kind = GraphNodeKind.Not, Name = "Not" };
        var report = new GraphValidator().Validate(new GraphDocument
        {
            Nodes = new() { one, two, not },
            Edges = new() { new() { FromNodeId = one.Id, ToNodeId = not.Id }, new() { FromNodeId = two.Id, ToNodeId = not.Id } }
        });
        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_NOT_ARITY");
    }

    [Fact]
    public void TonRequiresExactlyOneInput()
    {
        var one = new GraphNode { Kind = GraphNodeKind.Input, Name = "One" };
        var two = new GraphNode { Kind = GraphNodeKind.Input, Name = "Two" };
        var ton = new GraphNode { Kind = GraphNodeKind.Ton, Name = "Ton", Properties = new Dictionary<string, string> { ["PresetMs"] = "100" } };
        var report = new GraphValidator().Validate(new GraphDocument
        {
            Nodes = new() { one, two, ton },
            Edges = new() { new() { FromNodeId = one.Id, ToNodeId = ton.Id }, new() { FromNodeId = two.Id, ToNodeId = ton.Id } }
        });
        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_TON_ARITY");
    }

    [Fact]
    public void AndAllowsMultipleIncomingEdges()
    {
        var one = new GraphNode { Kind = GraphNodeKind.Input, Name = "One" };
        var two = new GraphNode { Kind = GraphNodeKind.Input, Name = "Two" };
        var three = new GraphNode { Kind = GraphNodeKind.Input, Name = "Three" };
        var and = new GraphNode { Kind = GraphNodeKind.And, Name = "And" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Output" };
        var report = new GraphValidator().Validate(new GraphDocument
        {
            Nodes = new() { one, two, three, and, output },
            Edges = new()
            {
                new() { FromNodeId = one.Id, ToNodeId = and.Id },
                new() { FromNodeId = two.Id, ToNodeId = and.Id },
                new() { FromNodeId = three.Id, ToNodeId = and.Id },
                new() { FromNodeId = and.Id, ToNodeId = output.Id }
            }
        });
        Assert.True(report.IsValid);
    }

    [Fact]
    public void OutputRejectsMultipleDrivers()
    {
        var one = new GraphNode { Kind = GraphNodeKind.Input, Name = "One" };
        var two = new GraphNode { Kind = GraphNodeKind.Input, Name = "Two" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Output" };
        var report = new GraphValidator().Validate(new GraphDocument
        {
            Nodes = new() { one, two, output },
            Edges = new() { new() { FromNodeId = one.Id, ToNodeId = output.Id }, new() { FromNodeId = two.Id, ToNodeId = output.Id } }
        });
        Assert.Contains(report.Diagnostics, x => x.Code == "GRAPH_MULTI_WRITER");
    }
}
