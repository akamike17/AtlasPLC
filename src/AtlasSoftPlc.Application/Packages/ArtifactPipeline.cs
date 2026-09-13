using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Application.Packages;

public sealed record ArtifactResult(string Kind, byte[] Content, string Hash, IReadOnlyList<string> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}

/// <summary>Único punto de generación: valida primero y sólo emite formatos soportados.</summary>
public sealed class ArtifactPipeline(
    IProgramValidationPipeline validation,
    StructuredTextEmitter st,
    PlcOpenXmlEmitter xml,
    ITargetPluginRegistry? plugins = null)
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

    /// <summary>
    /// Generación vinculada a una instancia persistida. No permite emitir un
    /// artefacto para un target que no lo declara como compatible.
    /// </summary>
    public ArtifactResult Generate(PlcProgramDefinition program, TargetInstance instance, string kind)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var plugin = plugins?.Get(instance.TargetPluginId);
        if (plugin is null)
            return Blocked(kind, $"El target '{instance.TargetPluginId}' no tiene un plugin registrado.");

        var normalized = NormalizeKind(kind);
        if (normalized is null)
            return Generate(program, kind);
        if (!plugin.Descriptor.SupportedArtifactKinds.Any(k => string.Equals(NormalizeKind(k), normalized, StringComparison.OrdinalIgnoreCase)))
            return Blocked(kind, $"El target '{instance.DisplayName}' no soporta el artefacto {normalized}.");

        var required = normalized.Equals("StructuredText", StringComparison.OrdinalIgnoreCase)
            ? TargetCapability.GenerateSource
            : TargetCapability.GenerateProject;
        if (!plugin.Descriptor.Capabilities.Supports(required))
            return Blocked(kind, $"El target '{instance.DisplayName}' no declara la capacidad {required}; generación detenida.");

        return Generate(program, normalized);
    }

    private static ArtifactResult Blocked(string kind, string message) =>
        new(kind, Array.Empty<byte>(), string.Empty, new[] { message });

    private static string? NormalizeKind(string kind) => kind.ToLowerInvariant() switch
    {
        "structuredtext" or "st" => "StructuredText",
        "plcopenxml" or "plcopen-xml" => "PlcOpenXml",
        _ => null
    };

    private static ArtifactResult FromText(StructuredTextArtifact artifact) => new("StructuredText", System.Text.Encoding.UTF8.GetBytes(artifact.Source), artifact.Hash, artifact.Diagnostics.Select(x => x.Message).ToArray());
    private static ArtifactResult FromXml(PlcOpenXmlArtifact artifact) => new("PlcOpenXml", System.Text.Encoding.UTF8.GetBytes(artifact.Xml), artifact.Hash, artifact.Diagnostics.Select(x => x.Message).ToArray());
}
