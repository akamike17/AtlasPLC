using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Web.Tests;

public sealed class GraphLowererTests
{
    [Fact]
    public void LowersAndGraphToExecutableRule()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Start" };
        var input2 = new GraphNode { Kind = GraphNodeKind.Input, Name = "Enable" };
        var and = new GraphNode { Kind = GraphNodeKind.And, Name = "Permit" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Motor" };
        var graph = new GraphDocument { Nodes = new() { input, input2, and, output }, Edges = new() { new GraphEdge { FromNodeId = input.Id, ToNodeId = and.Id }, new GraphEdge { FromNodeId = input2.Id, ToNodeId = and.Id }, new GraphEdge { FromNodeId = and.Id, ToNodeId = output.Id } } };
        var validation = new GraphValidator().Validate(graph);
        var program = new GraphLowerer().Lower(graph, validation);
        Assert.True(validation.IsValid);
        Assert.Single(program.Logic.Rules);
        Assert.Equal("Motor", program.Variables.Single(x => x.Direction == AtlasSoftPlc.Domain.Common.VariableDirection.Output).Key);
    }

    [Fact]
    public void LowersTonGraphToTimerStartAndDoneRule()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Start" };
        var ton = new GraphNode { Kind = GraphNodeKind.Ton, Name = "Delay", Properties = new Dictionary<string, string> { ["PresetMs"] = "750" } };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Motor" };
        var graph = new GraphDocument
        {
            Nodes = new() { input, ton, output },
            Edges = new() { new GraphEdge { FromNodeId = input.Id, ToNodeId = ton.Id }, new GraphEdge { FromNodeId = ton.Id, ToNodeId = output.Id } }
        };

        var validation = new GraphValidator().Validate(graph);
        var program = new GraphLowerer().Lower(graph, validation);

        Assert.True(validation.IsValid);
        Assert.Single(program.Logic.TimerIds);
        Assert.Equal(2, program.Logic.Rules.Count);
        Assert.Contains(program.Logic.Rules.SelectMany(r => r.Actions), action => action is AtlasSoftPlc.Domain.Logic.StartTimerAction timer && timer.PresetMs == 750);
        Assert.Contains(program.Logic.Rules, rule => rule.Condition is AtlasSoftPlc.Domain.Logic.TimerStateExpression);
    }

    [Fact]
    public void SharedInputAcrossBranchesDoesNotTriggerCycle()
    {
        var shared = new GraphNode { Kind = GraphNodeKind.Input, Name = "Shared" };
        var other = new GraphNode { Kind = GraphNodeKind.Input, Name = "Other" };
        var and = new GraphNode { Kind = GraphNodeKind.And, Name = "And" };
        var or = new GraphNode { Kind = GraphNodeKind.Or, Name = "Or" };
        var outAnd = new GraphNode { Kind = GraphNodeKind.Output, Name = "AndOutput" };
        var outOr = new GraphNode { Kind = GraphNodeKind.Output, Name = "OrOutput" };
        var graph = new GraphDocument
        {
            Nodes = new() { shared, other, and, or, outAnd, outOr },
            Edges = new()
            {
                new() { FromNodeId = shared.Id, ToNodeId = and.Id },
                new() { FromNodeId = other.Id, ToNodeId = and.Id },
                new() { FromNodeId = shared.Id, ToNodeId = or.Id },
                new() { FromNodeId = other.Id, ToNodeId = or.Id },
                new() { FromNodeId = and.Id, ToNodeId = outAnd.Id },
                new() { FromNodeId = or.Id, ToNodeId = outOr.Id }
            }
        };
        var validation = new GraphValidator().Validate(graph);
        var program = new GraphLowerer().Lower(graph, validation);
        Assert.True(validation.IsValid);
        Assert.Equal(2, program.Logic.Rules.Count);
    }

    [Fact]
    public void IndirectCycleIsRejected()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Input" };
        var extra = new GraphNode { Kind = GraphNodeKind.Input, Name = "Extra" };
        var a = new GraphNode { Kind = GraphNodeKind.And, Name = "A" };
        var b = new GraphNode { Kind = GraphNodeKind.And, Name = "B" };
        var graph = new GraphDocument
        {
            Nodes = new() { input, extra, a, b },
            Edges = new()
            {
                new() { FromNodeId = input.Id, ToNodeId = a.Id },
                new() { FromNodeId = extra.Id, ToNodeId = a.Id },
                new() { FromNodeId = a.Id, ToNodeId = b.Id },
                new() { FromNodeId = extra.Id, ToNodeId = b.Id },
                new() { FromNodeId = b.Id, ToNodeId = a.Id }
            }
        };
        var validation = new GraphValidator().Validate(graph);
        Assert.False(validation.IsValid);
        Assert.Throws<InvalidOperationException>(() => new GraphLowerer().Lower(graph, validation));
    }

    [Fact]
    public void ApplySameGraphTwiceDoesNotDuplicateRules()
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "Input" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "Output" };
        var graph = new GraphDocument { Nodes = new() { input, output }, Edges = new() { new() { FromNodeId = input.Id, ToNodeId = output.Id } } };
        var lowerer = new GraphLowerer();
        var validation = new GraphValidator().Validate(graph);
        var first = lowerer.Lower(graph, validation);
        var second = lowerer.Lower(graph, validation);
        Assert.Single(first.Logic.Rules);
        Assert.Single(second.Logic.Rules);
    }
}
