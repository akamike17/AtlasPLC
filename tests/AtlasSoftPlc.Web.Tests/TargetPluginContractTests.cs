using AtlasSoftPlc.Targets;

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
    }

    private sealed class FakePlugin(string id) : ITargetPlugin
    {
        public TargetDescriptor Descriptor { get; } = new() { Id = id, DisplayName = "Fake", Category = TargetCategory.DiagnosticOnly, Description = "Test" };
        public ITargetStatusProvider StatusProvider => throw new NotImplementedException();
        public IReadOnlyList<TargetActionDescriptor> Actions => Array.Empty<TargetActionDescriptor>();
        public IReadOnlyList<TargetConfigurationField> ConfigurationSchema => Array.Empty<TargetConfigurationField>();
    }
}
