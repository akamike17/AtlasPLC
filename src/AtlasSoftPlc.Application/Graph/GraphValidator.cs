using AtlasSoftPlc.Domain.Graph;

namespace AtlasSoftPlc.Application.Graph;

public interface IGraphValidator { GraphValidationReport Validate(GraphDocument graph); }

public sealed class GraphValidator : IGraphValidator
{
    public GraphValidationReport Validate(GraphDocument graph)
    {
        var report = new GraphValidationReport();
        if (graph is null) { report.Diagnostics.Add(new(GraphDiagnosticSeverity.Blocker, "GRAPH_NULL", "No existe un gráfico para validar.", "Crea o carga un diagrama.")); return report; }
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes)
        {
            if (!ids.Add(node.Id)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_DUP_NODE", $"ID de nodo duplicado: {node.Id}.", "Regenera el ID del bloque.", node.Id));
            if (string.IsNullOrWhiteSpace(node.Name)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_NODE_NAME", "Hay un bloque sin nombre.", "Asigna un nombre único.", node.Id));
            else if (!names.Add(node.Name)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_DUP_NAME", $"Nombre duplicado: {node.Name}.", "Renombra uno de los bloques.", node.Id));
            if (node.Kind == GraphNodeKind.Ton && (!node.Properties.TryGetValue("PresetMs", out var preset) || !double.TryParse(preset, out var ms) || ms <= 0)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_TON_PRESET", $"El TON '{node.Name}' no tiene un preset válido.", "Configura un tiempo mayor que cero.", node.Id));
            if (node.Kind is GraphNodeKind.Tof or GraphNodeKind.Set or GraphNodeKind.Reset or GraphNodeKind.Latch or GraphNodeKind.Unlatch or GraphNodeKind.Compare or GraphNodeKind.Interlock or GraphNodeKind.Constant)
                report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_KIND_UNSUPPORTED", $"El bloque '{node.Name}' usa el tipo '{node.Kind}', que aún no tiene traducción ejecutable.", "Cámbialo por Entrada, AND, OR, NOT, TON, Memoria o Salida.", node.Id));
        }
        var validEdges = new List<GraphEdge>();
        foreach (var edge in graph.Edges)
        {
            if (!ids.Contains(edge.FromNodeId) || !ids.Contains(edge.ToNodeId))
            {
                report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_EDGE_REF", "Una conexión apunta a un bloque inexistente.", "Elimina la conexión rota o restaura el bloque.", EdgeId: edge.Id));
                continue;
            }
            validEdges.Add(edge);
            if (edge.FromNodeId == edge.ToNodeId) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_SELF_LOOP", "Un bloque no puede conectarse consigo mismo.", "Conecta el puerto a otro bloque.", NodeId: edge.FromNodeId, EdgeId: edge.Id));
        }
        DetectCycles(validEdges, ids, report);
        foreach (var input in graph.Nodes.Where(x => x.Kind == GraphNodeKind.Input))
            if (validEdges.Any(x => x.ToNodeId == input.Id)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_INPUT_WRITER", $"La entrada '{input.Name}' no puede ser escrita por otro bloque.", "Usa una entrada como fuente del diagrama.", input.Id));
        foreach (var output in graph.Nodes.Where(x => x.Kind == GraphNodeKind.Output))
        {
            var incoming = validEdges.Where(x => x.ToNodeId == output.Id).ToList();
            if (incoming.Count == 0) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_OUTPUT_UNDRIVEN", $"La salida '{output.Name}' no tiene una fuente.", "Conecta una lógica o entrada antes de aplicar el diseño.", output.Id));
            else if (incoming.Count > 1) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_MULTI_WRITER", $"La salida '{output.Name}' tiene múltiples drivers.", "Deja un solo bloque como fuente de la salida.", output.Id, incoming[1].Id));
        }
        foreach (var node in graph.Nodes.Where(x => x.Kind is GraphNodeKind.And or GraphNodeKind.Or or GraphNodeKind.Not or GraphNodeKind.Ton or GraphNodeKind.Tof))
        {
            var incoming = validEdges.Count(x => x.ToNodeId == node.Id);
            if (incoming == 0) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_NO_INPUT", $"El bloque '{node.Name}' no tiene entrada.", "Conecta una fuente válida.", node.Id));
            switch (node.Kind)
            {
                case GraphNodeKind.And when incoming < 2:
                    report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_AND_ARITY", $"AND '{node.Name}' requiere al menos dos entradas.", "Conecta dos o más señales al AND.", node.Id));
                    break;
                case GraphNodeKind.Or when incoming < 2:
                    report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_OR_ARITY", $"OR '{node.Name}' requiere al menos dos entradas.", "Conecta dos o más señales al OR.", node.Id));
                    break;
                case GraphNodeKind.Not when incoming != 1:
                    report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_NOT_ARITY", $"NOT '{node.Name}' requiere exactamente una entrada.", "Conecta un único puerto de entrada.", node.Id));
                    break;
                case GraphNodeKind.Ton when incoming != 1:
                    report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_TON_ARITY", $"TON '{node.Name}' requiere exactamente una entrada.", "Conecta un único bloque fuente al TON.", node.Id));
                    break;
                case GraphNodeKind.Tof when incoming != 1:
                    report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_TOF_ARITY", $"TOF '{node.Name}' requiere exactamente una entrada.", "Conecta un único bloque fuente al TOF.", node.Id));
                    break;
            }
        }
        return report;
    }

    private static void DetectCycles(IReadOnlyList<GraphEdge> edges, IReadOnlySet<Guid> nodeIds, GraphValidationReport report)
    {
        var adjacency = edges.Where(x => x.FromNodeId != x.ToNodeId)
            .GroupBy(x => x.FromNodeId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var colors = new Dictionary<Guid, byte>();
        var reportedEdges = new HashSet<Guid>();

        void Visit(Guid nodeId)
        {
            colors[nodeId] = 1;
            if (adjacency.TryGetValue(nodeId, out var outgoing))
                foreach (var edge in outgoing)
                {
                    if (!nodeIds.Contains(edge.ToNodeId)) continue;
                    colors.TryGetValue(edge.ToNodeId, out var color);
                    if (color == 1)
                    {
                        if (reportedEdges.Add(edge.Id))
                            report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_CYCLE", "El gráfico contiene un ciclo de dependencias.", "Rompe el cableado que regresa a un bloque anterior.", edge.ToNodeId, edge.Id));
                    }
                    else if (color == 0) Visit(edge.ToNodeId);
                }
            colors[nodeId] = 2;
        }

        foreach (var nodeId in nodeIds)
            if (!colors.ContainsKey(nodeId)) Visit(nodeId);
    }
}
