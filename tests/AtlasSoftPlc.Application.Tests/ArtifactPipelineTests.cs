using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Application.Packages;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Tests;

public sealed class ArtifactPipelineTests
{
    [Fact]
    public void Unsupported_format_returns_diagnostic_without_content()
    {
        var pipeline = new ArtifactPipeline(new FakePipeline(true), new StructuredTextEmitter(), new PlcOpenXmlEmitter());
        var result = pipeline.Generate(AtlasIrFixtures.BuildTank().ToProgramDefinition(), "tia-project");
        Assert.False(result.Succeeded);
        Assert.Empty(result.Content);
        Assert.Contains("no soportado", result.Diagnostics[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validation_gate_blocks_artifact_generation()
    {
        var pipeline = new ArtifactPipeline(new FakePipeline(false), new StructuredTextEmitter(), new PlcOpenXmlEmitter());
        var result = pipeline.Generate(AtlasIrFixtures.BuildTank().ToProgramDefinition(), "st");
        Assert.False(result.Succeeded);
        Assert.Empty(result.Content);
        Assert.Contains("bloqueado", result.Diagnostics[0], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakePipeline(bool allowed) : IProgramValidationPipeline
    {
        public PipelineResult Validate(AtlasSoftPlc.Domain.Logic.LogicProgram program, IReadOnlyDictionary<Guid, AtlasSoftPlc.Domain.Variables.VariableDefinition> variables, ValidationOperation operation, ValidationContext? context = null)
        {
            var report = new ValidationReport();
            if (!allowed) report.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, Message = "Gate bloqueado para prueba." });
            return new(report, ValidationPolicy.For(operation));
        }
    }
}
