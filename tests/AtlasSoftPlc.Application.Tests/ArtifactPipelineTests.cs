using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Application.Packages;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Targets;

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

    [Fact]
    public void RejectsArtifactKindNotSupportedByTarget()
    {
        var plugin = new FakePlugin(new TargetDescriptor
        {
            Id = "xml-only", DisplayName = "XML only", Category = TargetCategory.EngineeringExport,
            Description = "test", SupportedArtifactKinds = new[] { "PlcOpenXml" },
            Capabilities = new TargetCapabilities(new[] { TargetCapability.GenerateProject })
        });
        var pipeline = CreateTargetPipeline(plugin);
        var result = pipeline.Generate(AtlasIrFixtures.BuildTank().ToProgramDefinition(), Instance("xml-only"), "StructuredText");
        Assert.False(result.Succeeded);
        Assert.Contains("no soporta", result.Diagnostics[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsGenerateForModbusOnline()
    {
        var plugin = new FakePlugin(new TargetDescriptor
        {
            Id = "modbus-online", DisplayName = "Modbus Online", Category = TargetCategory.OnlineIo,
            Description = "test", Capabilities = new TargetCapabilities(new[] { TargetCapability.ReadLiveData })
        });
        var result = CreateTargetPipeline(plugin).Generate(AtlasIrFixtures.BuildTank().ToProgramDefinition(), Instance("modbus-online"), "StructuredText");
        Assert.False(result.Succeeded);
        Assert.Contains("no soporta", result.Diagnostics[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeneratesSTForIecStTarget()
    {
        var plugin = new FakePlugin(new TargetDescriptor
        {
            Id = "iec-st", DisplayName = "IEC ST", Category = TargetCategory.EngineeringExport,
            Description = "test", SupportedArtifactKinds = new[] { "StructuredText" },
            Capabilities = new TargetCapabilities(new[] { TargetCapability.GenerateSource })
        });
        var input = new VariableDefinition { Key = "Start", DisplayName = "Start", Direction = VariableDirection.Input, DataType = PlcDataType.Bool };
        var output = new VariableDefinition { Key = "Motor", DisplayName = "Motor", Direction = VariableDirection.Output, DataType = PlcDataType.Bool };
        var program = new PlcProgramDefinition
        {
            Name = "Simple",
            Variables = new() { input, output },
            Logic = new LogicProgram
            {
                Rules = new()
                {
                    new LogicRule
                    {
                        Name = "Start motor",
                        Condition = new VariableExpression { VariableId = input.Id, VariableKey = input.Key },
                        Actions = new List<LogicAction> { new SetOutputAction { VariableId = output.Id, Value = "true" } },
                        ElseActions = new List<LogicAction> { new SetOutputAction { VariableId = output.Id, Value = "false" } }
                    }
                }
            }
        };
        var result = CreateTargetPipeline(plugin).Generate(program, Instance("iec-st"), "st");
        Assert.True(result.Succeeded, string.Join(" | ", result.Diagnostics));
        Assert.NotEmpty(result.Content);
    }

    private static ArtifactPipeline CreateTargetPipeline(ITargetPlugin plugin) =>
        new(new FakePipeline(true), new StructuredTextEmitter(), new PlcOpenXmlEmitter(), new TargetPluginRegistry(new[] { plugin }));

    private static TargetInstance Instance(string pluginId) => new() { Id = pluginId + "-local", TargetPluginId = pluginId, DisplayName = pluginId };

    private sealed class FakePlugin(TargetDescriptor descriptor) : ITargetPlugin
    {
        public TargetDescriptor Descriptor { get; } = descriptor;
        public ITargetStatusProvider StatusProvider { get; } = new ReadyStatusProvider();
        public IReadOnlyList<TargetActionDescriptor> Actions => Array.Empty<TargetActionDescriptor>();
        public IReadOnlyList<TargetConfigurationField> ConfigurationSchema => Array.Empty<TargetConfigurationField>();
    }

    private sealed class ReadyStatusProvider : ITargetStatusProvider
    {
        public Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default) => Task.FromResult(new TargetRuntimeStatus("Ready"));
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
