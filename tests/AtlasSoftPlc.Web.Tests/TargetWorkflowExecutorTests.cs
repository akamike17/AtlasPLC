using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class TargetWorkflowExecutorTests
{
    [Fact]
    public async Task UnknownStepFails()
    {
        var context = Create(new FakePlugin("fake", Array.Empty<TargetActionDescriptor>(), true));
        var result = await context.Executor.ExecuteAsync("instance", "missing", new TargetActionRequest(new Dictionary<string, string>()));
        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.State);
    }

    [Fact]
    public async Task MissingCapabilityBlocks()
    {
        var action = new TargetActionDescriptor("write", "Write", "write") { RequiredCapabilities = new[] { TargetCapability.WriteLiveData } };
        var context = Create(new FakePlugin("fake", new[] { action }, true));
        var result = await context.Executor.ExecuteAsync("instance", "write", new TargetActionRequest(new Dictionary<string, string>()));
        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.State);
    }

    [Fact]
    public async Task PluginCanAddActionWithoutChangingController()
    {
        var action = new TargetActionDescriptor("diagnostics", "Diagnostics", "diagnostics")
        {
            RequiredState = "Ready",
            RequiredCapabilities = new[] { TargetCapability.ReadDiagnostics }
        };
        var context = Create(new FakePlugin("new-plugin", new[] { action }, true, TargetCapability.ReadDiagnostics));
        var result = await context.Executor.ExecuteAsync("instance", "diagnostics", new TargetActionRequest(new Dictionary<string, string>()));
        Assert.True(result.Succeeded);
        Assert.Equal("Completed", result.State);
    }

    private static Context Create(FakePlugin plugin)
    {
        var repository = new InstanceRepository(new TargetInstance { Id = "instance", TargetPluginId = plugin.Descriptor.Id, DisplayName = "Instance" });
        var registry = new TargetPluginRegistry(new[] { plugin });
        var statuses = new TargetRuntimeStatusService(repository, registry);
        return new Context(new TargetWorkflowExecutor(repository, registry, statuses));
    }

    private sealed record Context(ITargetWorkflowExecutor Executor);

    private sealed class InstanceRepository(TargetInstance instance) : ITargetInstanceRepository
    {
        public Task<IReadOnlyList<TargetInstance>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetInstance>>(new[] { instance });
        public Task<TargetInstance?> GetAsync(string instanceId, CancellationToken ct = default) => Task.FromResult<TargetInstance?>(instanceId == instance.Id ? instance : null);
        public Task SaveAsync(TargetInstance value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string instanceId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePlugin : ITargetPlugin
    {
        public FakePlugin(string id, IReadOnlyList<TargetActionDescriptor> actions, bool ready, params TargetCapability[] capabilities)
        {
            Descriptor = new TargetDescriptor
            {
                Id = id, DisplayName = id, Category = TargetCategory.DiagnosticOnly, Description = "test",
                Capabilities = new TargetCapabilities(capabilities)
            };
            Actions = actions;
            StatusProvider = new StatusProvider(ready ? "Ready" : "NotConfigured");
            ActionProvider = new ActionProvider();
        }
        public TargetDescriptor Descriptor { get; }
        public ITargetStatusProvider StatusProvider { get; }
        public IReadOnlyList<TargetActionDescriptor> Actions { get; }
        public IReadOnlyList<TargetConfigurationField> ConfigurationSchema => Array.Empty<TargetConfigurationField>();
        public ITargetActionProvider ActionProvider { get; }
    }

    private sealed class StatusProvider(string state) : ITargetStatusProvider
    {
        public Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default) => Task.FromResult(new TargetRuntimeStatus(state));
    }

    private sealed class ActionProvider : ITargetActionProvider
    {
        public Task<TargetActionResult> ExecuteAsync(TargetInstance instance, string actionId, TargetActionRequest request, CancellationToken ct = default) => Task.FromResult(new TargetActionResult(true, actionId));
    }
}
