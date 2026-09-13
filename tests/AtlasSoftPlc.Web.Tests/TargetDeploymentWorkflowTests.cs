using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class TargetDeploymentWorkflowTests
{
    [Fact]
    public async Task Vendor_without_registered_adapter_is_never_reported_as_deployed()
    {
        var workflow = new TargetDeploymentWorkflow(Array.Empty<IPlcTargetAdapter>(), new AllowingPipeline());
        var request = new DeploymentRequest
        {
            TargetManufacturer = "Siemens", TargetFamily = "S7", TargetModel = "1500",
            ProjectId = Guid.NewGuid(), ProjectVersion = 1, ProjectHash = "hash", ConfirmationToken = "token"
        };
        var result = await workflow.DeployAsync("siemens-s7", null!, request);
        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.State);
    }

    private sealed class AllowingPipeline : IProgramValidationPipeline
    {
        public PipelineResult Validate(AtlasSoftPlc.Domain.Logic.LogicProgram program, IReadOnlyDictionary<Guid, AtlasSoftPlc.Domain.Variables.VariableDefinition> variables, ValidationOperation operation, ValidationContext? context = null)
            => new(new ValidationReport(), ValidationPolicy.For(operation));
    }
}
