using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class TargetPluginContractTests
{
    [Fact]
    public void Registry_is_composed_from_plugins_and_resolves_by_descriptor_id()
    {
        var plugin = new FakePlugin("fake-target");
        var registry = new TargetPluginRegistry(new[] { plugin });

        Assert.Same(plugin, registry.Get("FAKE-TARGET"));
        Assert.Single(registry.GetAll());
        Assert.Equal("fake-target", ((ITargetPlugin)plugin).WorkflowProvider.Workflow.Id);
        Assert.Empty(((ITargetPlugin)plugin).WorkflowProvider.Workflow.Steps);
    }

    [Fact]
    public async Task TargetRegistry_uses_plugin_descriptor_and_status_without_hardcoded_catalog()
    {
        var plugin = new FakePlugin("fake-target");
        var registry = new TargetRegistry(new[] { plugin });

        Assert.Contains(registry.GetAll(), descriptor => descriptor.Id == "fake-target");
        Assert.Equal("Ready", (await registry.GetStatusAsync("fake-target")).State);
        Assert.Null(registry.Get("unknown-target"));
    }

    private sealed class FakePlugin(string id) : ITargetPlugin
    {
        public TargetDescriptor Descriptor { get; } = new() { Id = id, DisplayName = "Fake", Category = TargetCategory.DiagnosticOnly, Description = "Test" };
        public ITargetStatusProvider StatusProvider { get; } = new FakeStatusProvider();
        public IReadOnlyList<TargetActionDescriptor> Actions => Array.Empty<TargetActionDescriptor>();
        public IReadOnlyList<TargetConfigurationField> ConfigurationSchema => Array.Empty<TargetConfigurationField>();
    }

    private sealed class FakeStatusProvider : ITargetStatusProvider
    {
        public Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default)
            => Task.FromResult(new TargetRuntimeStatus("Ready", "fake"));
    }
}
