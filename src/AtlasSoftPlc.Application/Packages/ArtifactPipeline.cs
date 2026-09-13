using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Packages;

public sealed record ArtifactResult(string Kind, byte[] Content, string Hash, IReadOnlyList<string> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}

/// <summary>Único punto de generación: valida primero y sólo emite formatos soportados.</summary>
public sealed class ArtifactPipeline(IProgramValidationPipeline validation, StructuredTextEmitter st, PlcOpenXmlEmitter xml)
{
    public ArtifactResult Generate(PlcProgramDefinition program, string kind)
    {
        var vars = program.Variables.ToDictionary(v => v.Id);
        var gate = validation.Validate(program.Logic, vars, ValidationOperation.Generate, new ValidationContext { SafeStates = program.Failsafe });
        if (!gate.Allowed) return new(kind, Array.Empty<byte>(), "", gate.Report.Issues.Select(i => i.Message).ToArray());
        return kind.ToLowerInvariant() switch
        {
            "structuredtext" or "st" => FromText(st.Emit(program)),
            "plcopenxml" or "plcopen-xml" => FromXml(xml.Emit(program)),
            _ => new(kind, Array.Empty<byte>(), "", new[] { $"Formato de artifact no soportado: {kind}." })
        };
    }

    private static ArtifactResult FromText(StructuredTextArtifact artifact) => new("StructuredText", System.Text.Encoding.UTF8.GetBytes(artifact.Source), artifact.Hash, artifact.Diagnostics.Select(x => x.Message).ToArray());
    private static ArtifactResult FromXml(PlcOpenXmlArtifact artifact) => new("PlcOpenXml", System.Text.Encoding.UTF8.GetBytes(artifact.Xml), artifact.Hash, artifact.Diagnostics.Select(x => x.Message).ToArray());
}
