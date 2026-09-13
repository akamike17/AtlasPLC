using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

public sealed class RuntimeShutdownRaceTests
{
    private sealed class NullNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }

    [Fact]
    public async Task ReplaceStartedDuringShutdown_CompletesWithoutOrphanedCompletion()
    {
        var service = NewService();
        await service.StartAsync(CancellationToken.None);

        var replacement = service.ReplaceProgramAsync(BuildProgram(), autoStart: false);
        await service.StopAsync(CancellationToken.None);

        var completed = await Task.WhenAny(replacement, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(replacement, completed);
        var error = await Record.ExceptionAsync(() => replacement);
        Assert.True(error is null || error is InvalidOperationException,
            $"La sustitución debe completar o fallar de forma determinista: {error}");
    }

    [Fact]
    public async Task ReplaceAfterShutdown_FailsDeterministically()
    {
        var service = NewService();
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReplaceProgramAsync(BuildProgram(), autoStart: false));
        Assert.Contains("ciclo de vida", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PlcRuntimeService NewService() => new(
        NullLogger<PlcRuntimeService>.Instance,
        new RuntimeStateStore(),
        new WatchdogService(),
        new NullNotifier());

    private static PlcProgramDefinition BuildProgram() => new()
    {
        Name = "shutdown-race",
        Variables = new List<VariableDefinition>(),
        Logic = new LogicProgram { Name = "shutdown-race" }
    };
}
