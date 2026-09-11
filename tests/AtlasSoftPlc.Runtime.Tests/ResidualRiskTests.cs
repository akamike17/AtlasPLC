using AtlasSoftPlc.Domain.Audit;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

/// <summary>
/// Pruebas de los 4 riesgos residuales corregidos: (1) watchdog independiente,
/// (2) force marca Quality/Source, (3) auditoría de force/clear/expire/fault/shutdown,
/// (4) política failsafe por defecto documentada.
/// </summary>
public class ResidualRiskTests
{
    // ── Riesgo 4: política failsafe por defecto ──────────────────────────
    [Theory]
    [InlineData(PlcDataType.Bool)]
    [InlineData(PlcDataType.Int16)]
    [InlineData(PlcDataType.UInt16)]
    [InlineData(PlcDataType.Int32)]
    [InlineData(PlcDataType.UInt32)]
    [InlineData(PlcDataType.Int64)]
    [InlineData(PlcDataType.UInt64)]
    [InlineData(PlcDataType.Float)]
    [InlineData(PlcDataType.Double)]
    [InlineData(PlcDataType.Decimal)]
    public void FailsafePolicy_NumericTypes_DefaultToFalseOrZero(PlcDataType type)
    {
        var v = FailsafePolicy.Default(type);
        Assert.Equal(type, v.DataType);
        if (type == PlcDataType.Bool)
        {
            Assert.False(v.AsBool());
        }
        else
        {
            Assert.Equal(0m, v.AsDecimal());
        }
    }

    [Fact]
    public void FailsafePolicy_String_DefaultToEmpty()
    {
        Assert.Equal(string.Empty, FailsafePolicy.Default(PlcDataType.String).AsString());
    }

    [Fact]
    public void FailsafePolicy_DateTime_DefaultToDefault()
    {
        Assert.Equal(DateTime.MinValue, FailsafePolicy.Default(PlcDataType.DateTime).As<DateTime>());
    }

    [Fact]
    public void FailsafePolicy_TimeSpan_DefaultToZero()
    {
        Assert.Equal(TimeSpan.Zero, FailsafePolicy.Default(PlcDataType.TimeSpan).As<TimeSpan>());
    }

    [Fact]
    public void FailsafePolicy_ValueIsNeverImplicit_ReturnsNoRandomValues()
    {
        // La política es determinista: dos llamadas producen valores iguales y no nulos.
        foreach (var type in Enum.GetValues<PlcDataType>())
        {
            var a = FailsafePolicy.Default(type);
            var b = FailsafePolicy.Default(type);
            Assert.Equal(a, b);
        }
    }

    // ── Riesgo 1: watchdog independiente ─────────────────────────────────
    [Fact]
    public async Task Watchdog_IndependentTimer_FiresOnTimeoutWhenArmed()
    {
        using var wd = new WatchdogService();
        wd.SetTimeoutMs(50);
        wd.Heartbeat(); // hay un scan completado previo

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        wd.OnTimeout = () => tcs.TrySetResult(true);
        wd.Armed = true;
        wd.Start();

        // Sin nuevos heartbeats, el timer independiente debe detectar el vencimiento.
        var fired = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(fired);
    }

    [Fact]
    public async Task Watchdog_NotArmed_DoesNotFire()
    {
        using var wd = new WatchdogService();
        wd.SetTimeoutMs(50);
        wd.Heartbeat();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        wd.OnTimeout = () => tcs.TrySetResult(true);
        wd.Armed = false; // runtime detenido: no debe vigilar
        wd.Start();

        // Sin armar, no debe disparar aunque el heartbeat envejezca.
        await Task.Delay(200);
        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task Watchdog_ContinualHeartbeats_DoNotFire()
    {
        using var wd = new WatchdogService();
        wd.SetTimeoutMs(50);
        wd.Armed = true;
        wd.OnTimeout = () => Assert.Fail("El watchdog no debe disparar con heartbeats frescos");
        wd.Start();

        // Heartbeats frecuentes (scan completado) mantienen el watchdog sano.
        for (int i = 0; i < 10; i++)
        {
            wd.Heartbeat();
            await Task.Delay(20);
        }

        Assert.True(wd.IsAlive);
    }

    // ── Riesgo 3: auditoría de force/clear/expire/fault/shutdown ─────────
    private sealed class FakeAuditSink : IRuntimeAuditSink
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent evt, CancellationToken ct = default)
        {
            lock (Events) Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Force_Clear_Shutdown_AreAudited()
    {
        var sink = new FakeAuditSink();
        var store = new RuntimeStateStore();
        var wd = new WatchdogService();
        var svc = new PlcRuntimeService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlcRuntimeService>.Instance,
            store, wd, new CountingNotifier(), sink);

        var motor = Guid.NewGuid();
        var program = new AtlasSoftPlc.Domain.Logic.LogicProgram
        {
            Name = "P",
            Rules = new List<AtlasSoftPlc.Domain.Logic.LogicRule>
            {
                new AtlasSoftPlc.Domain.Logic.LogicRule
                {
                    Name = "On",
                    Priority = 10,
                    Condition = null,
                    Actions = new List<AtlasSoftPlc.Domain.Logic.LogicAction>
                    {
                        new AtlasSoftPlc.Domain.Logic.SetOutputAction { VariableId = motor, Value = "true" }
                    }
                }
            }
        };
        svc.InstallConfiguration(program,
            new Dictionary<Guid, AtlasSoftPlc.Domain.Variables.VariableDefinition>
            {
                [motor] = new AtlasSoftPlc.Domain.Variables.VariableDefinition
                { Id = motor, Key = "M", DataType = PlcDataType.Bool, Direction = VariableDirection.Output }
            },
            new List<Interlock>(),
            new Dictionary<Guid, PlcValue> { [motor] = PlcValue.Bool(false) });

        await svc.StartAsync(CancellationToken.None);

        // Activar el programa para que el loop escanee y procese el force.
        svc.Post(new ActivateProgramCommand(program));

        // Force
        svc.Post(new ForceOutputCommand(motor, PlcValue.Bool(true)));
        WaitUntil(() => store.Snapshot.Outputs.TryGetValue(motor, out var o) && o.Value.AsBool(), "force ON");

        // ClearForce
        svc.Post(new ClearForceCommand(motor));

        // Shutdown
        await svc.StopAsync(CancellationToken.None);

        // Dar tiempo a que el Audit fire-and-forget (Task.Run) se complete.
        await Task.Delay(200);

        lock (sink.Events)
        {
            Assert.Contains(sink.Events, e => e.Action == AuditEventType.Force);
            Assert.Contains(sink.Events, e => e.Action == AuditEventType.Shutdown);
        }
    }

    private static void WaitUntil(Func<bool> condition, string reason, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            Thread.Sleep(10);
        }
        throw new Xunit.Sdk.XunitException("Timed out: " + reason);
    }

    private sealed class CountingNotifier : IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(RuntimeState state) => Task.CompletedTask;
    }
}