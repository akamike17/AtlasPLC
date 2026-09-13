using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class TargetRuntimeStatusServiceTests
{
    [Fact]
    public async Task StatusUsesSelectedInstanceConfiguration()
    {
        var instance = new TargetInstance
        {
            Id = "line-2",
            TargetPluginId = "fake",
            DisplayName = "Line 2",
            Configuration = new Dictionary<string, string> { ["endpoint"] = "10.10.10.22" }
        };
        var repository = new FakeInstanceRepository(instance);
        var provider = new CaptureStatusProvider();
        var plugin = new FakePlugin(provider);
        var service = new TargetRuntimeStatusService(repository, new TargetPluginRegistry(new[] { plugin }));

        await service.GetStatusAsync("line-2");

        Assert.Same(instance, provider.Instance);
        Assert.Equal("10.10.10.22", provider.Instance!.Configuration["endpoint"]);
    }

    private sealed class FakeInstanceRepository(TargetInstance instance) : ITargetInstanceRepository
    {
        public Task<IReadOnlyList<TargetInstance>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetInstance>>(new[] { instance });
        public Task<TargetInstance?> GetAsync(string instanceId, CancellationToken ct = default) => Task.FromResult<TargetInstance?>(instance.Id == instanceId ? instance : null);
        public Task SaveAsync(TargetInstance value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string instanceId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CaptureStatusProvider : ITargetStatusProvider
    {
        public TargetInstance? Instance { get; private set; }
        public Task<TargetRuntimeStatus> GetStatusAsync(TargetInstance instance, CancellationToken ct = default)
        {
            Instance = instance;
            return Task.FromResult(new TargetRuntimeStatus("Ready"));
        }
    }

    private sealed class FakePlugin(CaptureStatusProvider provider) : ITargetPlugin
    {
        public TargetDescriptor Descriptor { get; } = new() { Id = "fake", DisplayName = "Fake", Category = TargetCategory.DiagnosticOnly, Description = "test" };
        public ITargetStatusProvider StatusProvider { get; } = provider;
        public IReadOnlyList<TargetActionDescriptor> Actions => Array.Empty<TargetActionDescriptor>();
        public IReadOnlyList<TargetConfigurationField> ConfigurationSchema => Array.Empty<TargetConfigurationField>();
    }
}
