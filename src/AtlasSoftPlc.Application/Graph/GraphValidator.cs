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
        }
        foreach (var edge in graph.Edges)
        {
            if (!ids.Contains(edge.FromNodeId) || !ids.Contains(edge.ToNodeId)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_EDGE_REF", "Una conexión apunta a un bloque inexistente.", "Elimina la conexión rota o restaura el bloque.", EdgeId: edge.Id));
            if (edge.FromNodeId == edge.ToNodeId) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_SELF_LOOP", "Un bloque no puede conectarse consigo mismo.", "Conecta el puerto a otro bloque.", EdgeId: edge.Id));
        }
        var writers = graph.Edges.GroupBy(x => (x.ToNodeId, x.ToPort)).Where(g => g.Count() > 1);
        foreach (var writer in writers) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_MULTI_WRITER", "Hay múltiples conexiones al mismo puerto de entrada.", "Deja un solo bloque como fuente.", EdgeId: writer.First().Id));
        foreach (var input in graph.Nodes.Where(x => x.Kind == GraphNodeKind.Input))
            if (graph.Edges.Any(x => x.ToNodeId == input.Id)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_INPUT_WRITER", $"La entrada '{input.Name}' no puede ser escrita por otro bloque.", "Usa una entrada como fuente del diagrama.", input.Id));
        foreach (var output in graph.Nodes.Where(x => x.Kind == GraphNodeKind.Output))
            if (!graph.Edges.Any(x => x.ToNodeId == output.Id)) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_OUTPUT_UNDRIVEN", $"La salida '{output.Name}' no tiene una fuente.", "Conecta una lógica o entrada antes de aplicar el diseño.", output.Id));
        foreach (var node in graph.Nodes.Where(x => x.Kind is GraphNodeKind.And or GraphNodeKind.Or or GraphNodeKind.Not or GraphNodeKind.Ton or GraphNodeKind.Tof))
        {
            var incoming = graph.Edges.Count(x => x.ToNodeId == node.Id);
            if (incoming == 0) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_NO_INPUT", $"El bloque '{node.Name}' no tiene entrada.", "Conecta una fuente válida.", node.Id));
            if (node.Kind == GraphNodeKind.Not && incoming != 1) report.Diagnostics.Add(new(GraphDiagnosticSeverity.Error, "GRAPH_NOT_ARITY", $"NOT '{node.Name}' requiere exactamente una entrada.", "Conecta un único puerto de entrada.", node.Id));
        }
        return report;
    }
}
