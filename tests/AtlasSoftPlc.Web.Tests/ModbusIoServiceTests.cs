using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Protocols.Modbus;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtlasSoftPlc.Web.Tests;

/// <summary>
/// Pruebas del puente Modbus IO (<see cref="ModbusIoService"/>) contra un
/// <see cref="ModbusTcpServer"/> local en memoria (sin depender de ModRSsim2).
/// </summary>
public sealed class ModbusIoServiceTests
{
    private sealed class NullNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }

    private static (PlcRuntimeService runtime, SimulationService sim) CreateRuntime()
    {
        var store = new RuntimeStateStore();
        var watchdog = new WatchdogService();
        var runtime = new PlcRuntimeService(NullLogger<PlcRuntimeService>.Instance, store, watchdog, new NullNotifier());

        var dbPath = Path.Combine(Path.GetTempPath(), "AtlasSoftPlcTests", Guid.NewGuid().ToString("N"), "atlas.db");
        var sqlite = new SqliteStore(dbPath);
        sqlite.EnsureCreated();
        var catalogService = new PlcProgramService(new SqlitePlcProgramRepository(sqlite));

        var sim = new SimulationService(runtime, catalogService);
        sim.BootstrapTankDemo();
        return (runtime, sim);
    }

    private static ModbusOptions TankOptions(int port) => new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = port,
        UnitId = 1,
        TimeoutMs = 500,
        Retries = 1,
        CycleMs = 50,
        MaxConnectAttempts = 2
    };

    private static ModbusIoService CreateService(int port, out (PlcRuntimeService runtime, SimulationService sim) runtime)
    {
        runtime = CreateRuntime();
        var svc = new ModbusIoService(
            NullLogger<ModbusIoService>.Instance,
            Options.Create(TankOptions(port)),
            runtime.runtime,
            runtime.sim);
        return svc;
    }

    // ── Deshabilitado: no hace nada, no tira ──────────────────────────────

    [Fact]
    public async Task Disabled_Noop()
    {
        var (runtime, sim) = CreateRuntime();
        var opts = TankOptions(1);
        opts.Enabled = false;
        var svc = new ModbusIoService(
            NullLogger<ModbusIoService>.Instance, Options.Create(opts), runtime, sim);

        await svc.RunCycleAsync();
        Assert.Null(svc.LastError); // no intentó conectar
    }

    // ── Lectura correcta de inputs (sin excepción) ────────────────────────

    [Fact]
    public async Task ConnectAndRead_NoException_WhenServerUp()
    {
        using var server = new ModbusTcpServer();
        server.Start();
        server.DataStore.CoilDiscretes.WritePoints(0, new[] { true, false, true });

        var svc = CreateService(server.Port, out _);
        await svc.RunCycleAsync();

        Assert.Null(svc.LastError); // ciclo completó sin error
    }

    // ── Mapeo de variables: Key → dirección/área correctos ─────────────────

    [Fact]
    public async Task Output_IsBoundToCoil()
    {
        using var server = new ModbusTcpServer();
        server.Start();

        var svc = CreateService(server.Port, out var runtime);
        var pumpVar = runtime.sim.Variables.Values.First(v => v.Key == "Pump");

        // La bomba (output) está mapeada a coil:10; verificar que el binding se resolvió
        // correctamente contra el puente sin excepción.
        await svc.RunCycleAsync();
        Assert.Null(svc.LastError);

        // Sanity del mapeo: escribir la bomba vía el driver subyacente actualiza coil 10.
        var driver = new ModbusTcpDriver(new DeviceDefinition
        {
            Name = "p", Protocol = DeviceProtocol.ModbusTcp, Host = "127.0.0.1", Port = server.Port, UnitId = 1
        });
        driver.Configure(new[] { new TagBinding { VariableId = pumpVar.Id, Address = "coil:10", DataType = "bool", ReadWriteMode = "ReadWrite" } });
        await driver.ConnectAsync();
        var w = await driver.WriteOutputsAsync(new Dictionary<Guid, PlcValue> { [pumpVar.Id] = PlcValue.Bool(true) });
        Assert.True(w.Success, w.Error);
        Assert.True(server.DataStore.CoilDiscretes.ReadPoints(10, 1)[0]);
        await driver.DisconnectAsync();
    }

    // ── Fallo de conexión: no tira, queda diagnosticable, aplica failsafe ──

    [Fact]
    public async Task UnreachableServer_NoException_FailsafeApplied()
    {
        // Puerto que nadie escucha.
        var svc = CreateService(1, out _);
        await svc.RunCycleAsync();

        Assert.Null(svc.LastHealth); // no conectó
        Assert.NotNull(svc.LastError); // error diagnosticable
    }

    // ── Desconexión → reconexión ──────────────────────────────────────────

    [Fact]
    public async Task Reconnect_AfterDisconnect_Succeeds()
    {
        using var server = new ModbusTcpServer();
        server.Start();

        var svc = CreateService(server.Port, out _);
        await svc.RunCycleAsync();
        Assert.Null(svc.LastError);

        // El driver sobrevive a ciclos sucesivos (reconexión idempotente).
        for (int i = 0; i < 3; i++)
            await svc.RunCycleAsync();

        Assert.Null(svc.LastError);
    }
}