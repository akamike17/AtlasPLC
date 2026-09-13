namespace AtlasSoftPlc.Domain.Graph;

public enum GraphNodeKind { Input, Output, And, Or, Not, Ton, Tof, Set, Reset, Latch, Unlatch, Compare, EmergencyStop, Interlock, Memory, Constant }

public sealed class GraphDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ProjectId { get; set; }
    public Guid ProgramId { get; set; }
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();
    public GraphLayout Layout { get; set; } = new();
}

public sealed class GraphNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public GraphNodeKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public GraphPosition Position { get; set; } = new();
}

public sealed class GraphEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNodeId { get; set; }
    public string FromPort { get; set; } = "out";
    public Guid ToNodeId { get; set; }
    public string ToPort { get; set; } = "in";
}

public sealed class GraphLayout
{
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
}

public sealed class GraphPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public enum GraphDiagnosticSeverity { Info, Warning, Error, Blocker }

public sealed record GraphDiagnostic(GraphDiagnosticSeverity Severity, string Code, string Message, string Hint, Guid? NodeId = null, Guid? EdgeId = null);

public sealed class GraphValidationReport
{
    public List<GraphDiagnostic> Diagnostics { get; } = new();
    public bool IsValid => Diagnostics.All(x => x.Severity is not (GraphDiagnosticSeverity.Error or GraphDiagnosticSeverity.Blocker));
}
