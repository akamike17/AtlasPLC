using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Application.Services;
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

    [Fact]
    public async Task UnsupportedDeployDoesNotReachAdapter()
    {
        var instance = new TargetInstance { Id = "vendor-instance", TargetPluginId = "vendor", DisplayName = "Vendor" };
        var workflow = new TargetDeploymentWorkflow(
            new[] { new CountingAdapter() },
            new AllowingPipeline(),
            new TargetPluginRegistry(new[] { new VendorPlugin() }),
            new OneInstanceRepository(instance));
        var request = new DeploymentRequest
        {
            TargetManufacturer = "Vendor", TargetFamily = "Family", TargetModel = "Model",
            ProjectId = Guid.NewGuid(), ProjectVersion = 1, ProjectHash = "hash", ConfirmationToken = "token"
        };
        var result = await workflow.DeployAsync(instance.Id, new AtlasSoftPlc.Domain.Projects.PlcProgramDefinition(), request);
        Assert.Equal("Unsupported", result.State);
    }

    private sealed class AllowingPipeline : IProgramValidationPipeline
    {
        public PipelineResult Validate(AtlasSoftPlc.Domain.Logic.LogicProgram program, IReadOnlyDictionary<Guid, AtlasSoftPlc.Domain.Variables.VariableDefinition> variables, ValidationOperation operation, ValidationContext? context = null)
            => new(new ValidationReport(), ValidationPolicy.For(operation));
    }

    private sealed class OneInstanceRepository(TargetInstance instance) : ITargetInstanceRepository
    {
        public Task<IReadOnlyList<TargetInstance>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetInstance>>(new[] { instance });
        public Task<TargetInstance?> GetAsync(string instanceId, CancellationToken ct = default) => Task.FromResult<TargetInstance?>(instanceId == instance.Id ? instance : null);
        public Task SaveAsync(TargetInstance value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string instanceId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class VendorPlugin : ITargetPlugin
    {
        public TargetDescriptor Descriptor { get; } = new() { Id = "vendor", DisplayName = "Vendor", Manufacturer = "Vendor", Family = "Family", Model = "Model", Category = TargetCategory.PhysicalPlc, Description = "test", Capabilities = new TargetCapabilities(new[] { TargetCapability.DeployProgram }) };
        public ITargetStatusProvider StatusProvider { get; } = new StatusProvider();
        public IReadOnlyList<TargetActionDescriptor> Actions => Array.Empty<TargetActionDescriptor>();
        public IReadOnlyList<TargetConfigurationField> ConfigurationSchema => Array.Empty<TargetConfigurationField>();
    }

    private sealed class StatusProvider : ITargetStatusProvider
    {
        public Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default) => Task.FromResult(new TargetRuntimeStatus("Ready"));
    }

    private sealed class CountingAdapter : PlcTargetAdapterBase
    {
        public CountingAdapter() : base(new TargetIdentity { Manufacturer = "Other", Family = "Other", Model = "Other" }, new[] { TargetCapability.DeployProgram }) { }
    }
}
