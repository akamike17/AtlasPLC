using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Graph;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class GraphApplicationServiceTests
{
    [Fact]
    public async Task FailedRuntimeReplaceLeavesPersistenceUntouched()
    {
        var context = CreateContext(new BrokenLowerer());
        var result = await context.Service.ApplyAsync(BuildGraph(context.Simulation.Active!.Id));
        Assert.False(result.Succeeded);
        Assert.Equal("RUNTIME_REJECTED", result.Code);
        Assert.False(context.UnitOfWork.Called);
    }

    [Fact]
    public async Task FailedPersistenceCommitRestoresRuntime()
    {
        var context = CreateContext(new GraphLowerer(), new ThrowingUnitOfWork());
        var previous = context.Simulation.Active!;
        var result = await context.Service.ApplyAsync(BuildGraph(previous.Id));
        Assert.False(result.Succeeded);
        Assert.Equal("PERSISTENCE_FAILED", result.Code);
        Assert.Equal(previous.Logic.Rules.Count, context.Simulation.Active!.Logic.Rules.Count);
    }

    [Fact]
    public async Task SuccessfulApplyPersistsGraphAndProgram()
    {
        var context = CreateContext(new GraphLowerer());
        var result = await context.Service.ApplyAsync(BuildGraph(context.Simulation.Active!.Id));
        Assert.True(result.Succeeded);
        Assert.True(context.UnitOfWork.Called);
        Assert.NotNull(context.UnitOfWork.Graph);
        Assert.NotNull(context.UnitOfWork.Program);
        Assert.NotNull(context.UnitOfWork.Version);
    }

    private static GraphDocument BuildGraph(Guid programId)
    {
        var input = new GraphNode { Kind = GraphNodeKind.Input, Name = "GraphInput" };
        var output = new GraphNode { Kind = GraphNodeKind.Output, Name = "GraphOutput" };
        return new GraphDocument
        {
            ProgramId = programId,
            Nodes = new() { input, output },
            Edges = new() { new GraphEdge { FromNodeId = input.Id, ToNodeId = output.Id } }
        };
    }

    private static Context CreateContext(IGraphLowerer lowerer, IGraphApplyUnitOfWork? unitOfWork = null)
    {
        var runtimeStore = new RuntimeStateStore();
        var runtime = new PlcRuntimeService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlcRuntimeService>.Instance,
            runtimeStore,
            new WatchdogService(),
            new NullNotifier());
        var db = new SqliteStore(Path.Combine(Path.GetTempPath(), "AtlasSoftPlcTests", Guid.NewGuid().ToString("N"), "atlas.db"));
        db.EnsureCreated();
        var catalog = new PlcProgramService(new SqlitePlcProgramRepository(db));
        var simulation = new SimulationService(runtime, catalog);
        simulation.BootstrapTankDemo();
        var uow = unitOfWork ?? new RecordingUnitOfWork();
        var service = new GraphApplicationService(
            new GraphValidator(),
            lowerer,
            new AllowingPipeline(),
            uow,
            new InMemoryVersions(),
            simulation);
        return new Context(service, simulation, (RecordingUnitOfWork)uow);
    }

    private sealed record Context(GraphApplicationService Service, SimulationService Simulation, RecordingUnitOfWork UnitOfWork);

    private class RecordingUnitOfWork : IGraphApplyUnitOfWork
    {
        public bool Called { get; private set; }
        public GraphDocument? Graph { get; private set; }
        public PlcProgramDefinition? Program { get; private set; }
        public ProgramVersion? Version { get; private set; }
        public virtual Task CommitAsync(GraphDocument graph, PlcProgramDefinition program, ProgramVersion version, CancellationToken ct = default)
        {
            Called = true; Graph = graph; Program = program; Version = version;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingUnitOfWork : RecordingUnitOfWork
    {
        public override Task CommitAsync(GraphDocument graph, PlcProgramDefinition program, ProgramVersion version, CancellationToken ct = default) => throw new InvalidOperationException("commit failure");
    }

    private sealed class InMemoryVersions : IProgramVersionRepository
    {
        private readonly List<ProgramVersion> _versions = new();
        public Task<IReadOnlyList<ProgramVersion>> GetByProgramAsync(Guid programId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProgramVersion>>(_versions.Where(x => x.ProgramId == programId).ToArray());
        public Task SaveAsync(ProgramVersion version, CancellationToken ct = default) { _versions.Add(version); return Task.CompletedTask; }
        public Task<ProgramVersion?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_versions.FirstOrDefault(x => x.Id == id));
    }

    private sealed class AllowingPipeline : IProgramValidationPipeline
    {
        public PipelineResult Validate(LogicProgram program, IReadOnlyDictionary<Guid, VariableDefinition> variables, ValidationOperation operation, ValidationContext? context = null)
            => new(new ValidationReport(), ValidationPolicy.For(operation));
    }

    private sealed class BrokenLowerer : IGraphLowerer
    {
        public PlcProgramDefinition Lower(GraphDocument graph, GraphValidationReport validation)
        {
            var duplicate = Guid.NewGuid();
            return new PlcProgramDefinition
            {
                Variables = new()
                {
                    new VariableDefinition { Id = duplicate, Key = "A", DisplayName = "A", Direction = VariableDirection.Input, DataType = PlcDataType.Bool },
                    new VariableDefinition { Id = duplicate, Key = "B", DisplayName = "B", Direction = VariableDirection.Output, DataType = PlcDataType.Bool }
                }
            };
        }
    }

    private sealed class NullNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }
}
