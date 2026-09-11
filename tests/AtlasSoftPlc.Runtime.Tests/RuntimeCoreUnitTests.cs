using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Snapshots;
using AtlasSoftPlc.Runtime.Virtual;
using Xunit;

namespace AtlasSoftPlc.Runtime.Tests;

public class OutputArbiterTests
{
    private static OutputProposal Prop(Guid id, object value, OutputPriority priority)
        => new() { VariableId = id, Value = value, Priority = priority };

    [Fact]
    public void Arbitrate_SingleProposal_ReturnsIt()
    {
        var id = Guid.NewGuid();
        var arbiter = new OutputArbiter();
        var decisions = arbiter.Arbitrate(new[] { Prop(id, true, OutputPriority.AutomaticControl) });

        Assert.Single(decisions);
        Assert.Equal(id, decisions[0].VariableId);
        Assert.True(decisions[0].Value.AsBool());
        Assert.False(decisions[0].Conflicted);
    }

    [Fact]
    public void Arbitrate_HigherPriorityWins()
    {
        var id = Guid.NewGuid();
        var arbiter = new OutputArbiter();
        var decisions = arbiter.Arbitrate(new[]
        {
            Prop(id, true, OutputPriority.AutomaticControl),
            Prop(id, false, OutputPriority.Interlock)
        });

        Assert.Single(decisions);
        Assert.False(decisions[0].Value.AsBool()); // Interlock priority higher
        Assert.False(decisions[0].Conflicted); // different priorities -> no conflict
    }

    [Fact]
    public void Arbitrate_EqualPriorityMarksConflict()
    {
        var id = Guid.NewGuid();
        var arbiter = new OutputArbiter();
        var decisions = arbiter.Arbitrate(new[]
        {
            Prop(id, true, OutputPriority.AutomaticControl),
            Prop(id, false, OutputPriority.AutomaticControl)
        });

        Assert.Single(decisions);
        Assert.True(decisions[0].Conflicted);
        Assert.Equal(2, decisions[0].CompetingProposals.Count);
    }

    [Fact]
    public void Arbitrate_GroupsByVariable()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var arbiter = new OutputArbiter();
        var decisions = arbiter.Arbitrate(new[]
        {
            Prop(id1, true, OutputPriority.AutomaticControl),
            Prop(id2, false, OutputPriority.AutomaticControl)
        });

        Assert.Equal(2, decisions.Count);
    }

    [Fact]
    public void Arbitrate_PreservesTypeFromValue()
    {
        var id = Guid.NewGuid();
        var arbiter = new OutputArbiter();
        var decisions = arbiter.Arbitrate(new[] { Prop(id, 42, OutputPriority.AutomaticControl) });

        Assert.Single(decisions);
        Assert.Equal(PlcDataType.Int32, decisions[0].Value.DataType);
        Assert.Equal(42, decisions[0].Value.As<int>());
    }

    [Fact]
    public void Arbitrate_EmptyCollection_ReturnsEmpty()
    {
        var arbiter = new OutputArbiter();
        var decisions = arbiter.Arbitrate(Array.Empty<OutputProposal>());
        Assert.Empty(decisions);
    }

    [Fact]
    public void ApplyInterlocksAndFailsafe_ActiveInterlockOverrides()
    {
        var id = Guid.NewGuid();
        var interlockId = Guid.NewGuid();
        var interlock = new Interlock
        {
            Id = interlockId,
            Name = "DoorOpen",
            AffectedOutputs = new List<Guid> { id },
            SafeValue = false
        };

        var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), false, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new[] { interlock },
                    new Dictionary<Guid, bool> { [interlockId] = true },
                    new Dictionary<Guid, PlcValue>(),
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool });

                Assert.Single(result.Decisions);
                Assert.Empty(result.Errors);
                Assert.False(result.Decisions[0].Value.AsBool());
            }

            [Fact]
            public void ApplyInterlocksAndFailsafe_InactiveInterlockDoesNotOverride()
            {
                var id = Guid.NewGuid();
                var interlockId = Guid.NewGuid();
                var interlock = new Interlock
                {
                    Id = interlockId,
                    Name = "DoorOpen",
                    AffectedOutputs = new List<Guid> { id },
                    SafeValue = false
                };

                var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), false, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new[] { interlock },
                    new Dictionary<Guid, bool> { [interlockId] = false }, // inactive
                    new Dictionary<Guid, PlcValue>(),
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool });

                Assert.Single(result.Decisions);
                Assert.True(result.Decisions[0].Value.AsBool()); // unchanged
            }

            [Fact]
            public void ApplyInterlocksAndFailsafe_UnresolvedInterlock_FailClosed()
            {
                // Un interlock no evaluable debe forzar SafeValue (fail-closed, P0-2),
                // no darlo por inactivo.
                var id = Guid.NewGuid();
                var interlockId = Guid.NewGuid();
                var interlock = new Interlock
                {
                    Id = interlockId,
                    Name = "DoorOpen",
                    AffectedOutputs = new List<Guid> { id },
                    SafeValue = false
                };

                var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), false, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new[] { interlock },
                    new Dictionary<Guid, bool>(), // not evaluated
                    new Dictionary<Guid, PlcValue> { [id] = PlcValue.Bool(true) }, // failsafe = ON (irrelevant)
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool },
                    new Dictionary<Guid, string> { [interlockId] = "variable faltante" });

                Assert.Single(result.Decisions);
                Assert.False(result.Decisions[0].Value.AsBool()); // fail-closed -> SafeValue OFF
            }

            [Fact]
            public void ApplyInterlocksAndFailsafe_InterlockNotAffectingOutput()
            {
                var id = Guid.NewGuid();
                var interlockId = Guid.NewGuid();
                var otherOutput = Guid.NewGuid();
                var interlock = new Interlock
                {
                    Id = interlockId,
                    Name = "DoorOpen",
                    AffectedOutputs = new List<Guid> { otherOutput },
                    SafeValue = false
                };

                var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), false, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new[] { interlock },
                    new Dictionary<Guid, bool> { [interlockId] = true },
                    new Dictionary<Guid, PlcValue>(),
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool });

                Assert.Single(result.Decisions);
                Assert.True(result.Decisions[0].Value.AsBool());
            }

            [Fact]
            public void ApplyInterlocksAndFailsafe_ConflictUsesFailsafe()
            {
                var id = Guid.NewGuid();
                var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), true, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new List<Interlock>(),
                    new Dictionary<Guid, bool>(),
                    new Dictionary<Guid, PlcValue> { [id] = PlcValue.Bool(false) },
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool });

                Assert.Single(result.Decisions);
                Assert.Empty(result.Errors);
                Assert.False(result.Decisions[0].Value.AsBool()); // failsafe applied
            }

            [Fact]
            public void ApplyInterlocksAndFailsafe_ConflictWithoutFailsafe_ReportsError()
            {
                // Conflicto irresoluble sin failsafe: NO "primero gana", se reporta error (P1-2).
                var id = Guid.NewGuid();
                var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), true, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new List<Interlock>(),
                    new Dictionary<Guid, bool>(),
                    new Dictionary<Guid, PlcValue>(), // no failsafe
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool });

                Assert.Empty(result.Decisions); // sin decisión operable
                Assert.Single(result.Errors);   // error de configuración
            }

            [Fact]
            public void ApplyInterlocksAndFailsafe_InterlockTakesPrecedenceOverFailsafe()
            {
                var id = Guid.NewGuid();
                var interlockId = Guid.NewGuid();
                var interlock = new Interlock
                {
                    Id = interlockId,
                    Name = "DoorOpen",
                    AffectedOutputs = new List<Guid> { id },
                    SafeValue = false
                };

                // conflicted + interlock active -> interlock wins (safe value), not failsafe
                var decision = new OutputArbiter.ArbitrationDecision(id, PlcValue.Bool(true), true, new List<OutputProposal>());
                var arbiter = new OutputArbiter();

                var result = arbiter.ApplyInterlocksAndFailsafe(
                    new[] { decision },
                    new[] { interlock },
                    new Dictionary<Guid, bool> { [interlockId] = true },
                    new Dictionary<Guid, PlcValue> { [id] = PlcValue.Bool(true) }, // failsafe = ON (irrelevant, interlock wins)
                    new Dictionary<Guid, PlcDataType> { [id] = PlcDataType.Bool });

                Assert.Single(result.Decisions);
                Assert.False(result.Decisions[0].Value.AsBool());
            }
        }

public class TimerStateTests
{
    [Fact]
    public void Ton_Accumulates_UntilPreset()
    {
        var timer = new TimerState { Kind = "TON", PresetMs = 1000, Input = true };
        timer.Update(400);
        Assert.False(timer.Done);
        Assert.False(timer.Output);
        Assert.True(timer.Running);
        Assert.Equal(400, timer.ElapsedMs);

        timer.Update(700);
        Assert.True(timer.Done);
        Assert.True(timer.Output);
        Assert.Equal(1000, timer.ElapsedMs); // clamped to preset
    }

    [Fact]
    public void Ton_Resets_WhenInputFalls()
    {
        var timer = new TimerState { Kind = "TON", PresetMs = 1000, Input = true };
        timer.Update(500);
        timer.Input = false;
        timer.Update(100);

        Assert.Equal(0, timer.ElapsedMs);
        Assert.False(timer.Output);
        Assert.False(timer.Done);
        Assert.False(timer.Running);
    }

    [Fact]
    public void Tof_OutputTrueWhileInputHigh()
    {
        var timer = new TimerState { Kind = "TOF", PresetMs = 1000 };
        timer.Input = true;
        timer.Update(100);

        Assert.True(timer.Output);
        Assert.False(timer.Done);
        Assert.Equal(0, timer.ElapsedMs);
    }

    [Fact]
    public void Tof_CountsDownAfterInputFalls()
    {
        var timer = new TimerState { Kind = "TOF", PresetMs = 1000 };
        timer.Input = true;
        timer.Update(100);
        timer.Input = false;

        timer.Update(400);
        Assert.True(timer.Output); // still within preset window
        Assert.False(timer.Done);

        timer.Update(700);
        Assert.False(timer.Output); // elapsed >= preset
        Assert.True(timer.Done);
    }

    [Fact]
    public void Tp_OutputPulseWhileInputHigh()
    {
        var timer = new TimerState { Kind = "TP", PresetMs = 1000, Input = true };
        timer.Update(400);
        Assert.True(timer.Output);
        Assert.False(timer.Done);

        timer.Update(700);
        Assert.False(timer.Output);
        Assert.True(timer.Done);
        Assert.Equal(1000, timer.ElapsedMs);
    }

    [Fact]
    public void Tp_DoesNotResetWhenInputFalls()
    {
        var timer = new TimerState { Kind = "TP", PresetMs = 1000, Input = true };
        timer.Update(1200); // complete
        Assert.True(timer.Done);

        timer.Input = false;
        timer.Update(100);
        Assert.False(timer.Output);
        Assert.False(timer.Running);
        // elapsed stays at preset (not reset)
        Assert.Equal(1000, timer.ElapsedMs);
    }

    [Fact]
    public void Update_UnknownKind_IsNoOp()
    {
        var timer = new TimerState { Kind = "BAD", PresetMs = 1000, Input = true };
        timer.Update(500);
        Assert.False(timer.Done);
        Assert.False(timer.Output);
        Assert.False(timer.Running);
    }
}

public class CounterStateTests
{
    [Fact]
    public void Ctu_CountsUp_RisingEdgeOnly()
    {
        var counter = new CounterState { Kind = "CTU", Preset = 3 };
        counter.Update(true, false);
        counter.Update(true, false); // no edge - should not increment
        counter.Update(false, false);
        counter.Update(true, false); // rising edge

        Assert.Equal(2, counter.Current);
        Assert.False(counter.Done);
    }

    [Fact]
    public void Ctu_ReachesPreset_SetsDone()
    {
        var counter = new CounterState { Kind = "CTU", Preset = 2 };
        counter.Update(true, false);
        counter.Update(false, false);
        counter.Update(true, false);

        Assert.Equal(2, counter.Current);
        Assert.True(counter.Done);
    }

    [Fact]
    public void Ctu_ResetZeroes()
    {
        var counter = new CounterState { Kind = "CTU", Preset = 2 };
        counter.Update(true, false);
        counter.Update(false, false);
        counter.Update(true, false); // current = 2, done

        counter.Update(false, true); // reset
        Assert.Equal(0, counter.Current);
        Assert.False(counter.Done);
    }

    [Fact]
    public void Ctd_CountsDown_FromPreset()
    {
        var counter = new CounterState { Kind = "CTD", Preset = 3 };
        counter.Update(false, true); // reset -> current = preset
        Assert.Equal(3, counter.Current);

        counter.Update(true, false); // rising edge -> decrement
        Assert.Equal(2, counter.Current);
        Assert.False(counter.Done);
    }

    [Fact]
    public void Ctd_ReachesZero_SetsDone()
    {
        var counter = new CounterState { Kind = "CTD", Preset = 1 };
        counter.Update(false, true); // reset -> current = 1
        counter.Update(true, false); // -> 0

        Assert.Equal(0, counter.Current);
        Assert.True(counter.Done);
    }
}

public class TimerManagerTests
{
    [Fact]
    public void GetOrCreate_CreatesNewTimer()
    {
        var mgr = new TimerManager();
        var id = Guid.NewGuid();
        var timer = mgr.GetOrCreate(id, "TON", 1000);

        Assert.Equal(id, timer.TimerId);
        Assert.Equal("TON", timer.Kind);
        Assert.Equal(1000, timer.PresetMs);
    }

    [Fact]
    public void GetOrCreate_ReusesExisting_UpdatesKindPreset()
    {
        var mgr = new TimerManager();
        var id = Guid.NewGuid();
        var t1 = mgr.GetOrCreate(id, "TON", 1000);
        var t2 = mgr.GetOrCreate(id, "TOF", 2000);

        Assert.Same(t1, t2);
        Assert.Equal("TOF", t1.Kind);
        Assert.Equal(2000, t1.PresetMs);
    }

    [Fact]
    public void SetInput_UpdatesTimer()
    {
        var mgr = new TimerManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "TON", 1000);
        mgr.SetInput(id, true);

        Assert.True(mgr.TryRead(id, "Running", out _));
        var snap = mgr.Snapshot();
        Assert.True(snap[id].Input);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var mgr = new TimerManager();
        var id = Guid.NewGuid();
        var timer = mgr.GetOrCreate(id, "TON", 1000);
        timer.Input = true;
        timer.Update(500);
        Assert.True(timer.Running);

        mgr.Reset(id);
        Assert.False(timer.Running);
        Assert.False(timer.Output);
        Assert.False(timer.Done);
        Assert.Equal(0, timer.ElapsedMs);
    }

    [Fact]
    public void AdvanceAll_AdvancesAllTimers()
    {
        var mgr = new TimerManager();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        mgr.GetOrCreate(id1, "TON", 1000).Input = true;
        mgr.GetOrCreate(id2, "TON", 1000).Input = true;

        mgr.AdvanceAll(600);

        Assert.True(mgr.TryRead(id1, "Elapsed", out var e1));
        Assert.Equal(600, e1);
    }

    [Fact]
    public void TryRead_ReturnsFalseForUnknown()
    {
        var mgr = new TimerManager();
        Assert.False(mgr.TryRead(Guid.NewGuid(), "Done", out _));
    }

    [Fact]
    public void TryRead_ReadsFields()
    {
        var mgr = new TimerManager();
        var id = Guid.NewGuid();
        var timer = mgr.GetOrCreate(id, "TON", 500);
        timer.Input = true;
        timer.Update(600); // done

        Assert.True(mgr.TryRead(id, "Done", out var done));
        Assert.Equal(1, done);
        Assert.True(mgr.TryRead(id, "Running", out var running));
        Assert.Equal(1, running);
        Assert.True(mgr.TryRead(id, "Output", out var output));
        Assert.Equal(1, output);
        // unknown field -> 0
        Assert.True(mgr.TryRead(id, "Bogus", out var bogus));
        Assert.Equal(0, bogus);
    }

    [Fact]
    public void Snapshot_ReturnsCopy()
    {
        var mgr = new TimerManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "TON", 1000);

        var snap1 = mgr.Snapshot();
        var snap2 = mgr.Snapshot();
        Assert.NotSame(snap1, snap2);
        Assert.Single(snap1);
        Assert.Equal(id, snap1[id].TimerId);
    }

    [Fact]
    public void SetInput_UnknownTimer_IsNoOp()
    {
        var mgr = new TimerManager();
        mgr.SetInput(Guid.NewGuid(), true); // should not throw
    }

    [Fact]
    public void Reset_UnknownTimer_IsNoOp()
    {
        var mgr = new TimerManager();
        mgr.Reset(Guid.NewGuid()); // should not throw
    }
}

public class CounterManagerTests
{
    [Fact]
    public void GetOrCreate_Ctu_StartsAtZero()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        var counter = mgr.GetOrCreate(id, "CTU", 5);
        Assert.Equal(0, counter.Current);
    }

    [Fact]
    public void GetOrCreate_Ctd_StartsAtPreset()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        var counter = mgr.GetOrCreate(id, "CTD", 5);
        Assert.Equal(5, counter.Current);
    }

    [Fact]
    public void Increment_CountsUp_OnRisingEdgeOnly()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "CTU", 5);

        // Increment uses Update(true, false) which needs a rising edge (via _lastInput)
        mgr.Increment(id); // first: _lastInput was false -> edge -> count 1
        mgr.Increment(id); // second: _lastInput now true -> no edge -> stays 1

        Assert.True(mgr.TryRead(id, "Current", out var current));
        Assert.Equal(1, current);
    }

    [Fact]
    public void Decrement_CountsDown_NotBelowZero()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "CTU", 5);
        mgr.Decrement(id);
        mgr.Decrement(id);

        Assert.True(mgr.TryRead(id, "Current", out var current));
        Assert.Equal(0, current);
    }

    [Fact]
    public void Reset_ResetsCounter()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "CTU", 5);
        mgr.Increment(id);
        mgr.Increment(id);
        mgr.Reset(id);

        Assert.True(mgr.TryRead(id, "Current", out var current));
        Assert.Equal(0, current);
    }

    [Fact]
    public void TryRead_ReadsFields()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "CTU", 5);
        mgr.Increment(id); // count = 1 (rising edge)

        Assert.True(mgr.TryRead(id, "Current", out var current));
        Assert.Equal(1, current);
        Assert.True(mgr.TryRead(id, "Preset", out var preset));
        Assert.Equal(5, preset);
        Assert.True(mgr.TryRead(id, "Done", out var done));
        Assert.Equal(0, done); // CTU done only when Current >= Preset (1 < 5)
    }

    [Fact]
    public void TryRead_UnknownCounter_ReturnsFalse()
    {
        var mgr = new CounterManager();
        Assert.False(mgr.TryRead(Guid.NewGuid(), "Current", out _));
    }

    [Fact]
    public void Snapshot_ReturnsCopy()
    {
        var mgr = new CounterManager();
        var id = Guid.NewGuid();
        mgr.GetOrCreate(id, "CTU", 5);
        var snap = mgr.Snapshot();
        Assert.Single(snap);
        Assert.Equal(id, snap[id].CounterId);
    }
}

public class SnapshotTests
{
    [Fact]
    public void InputSnapshot_Get_ReturnsValue()
    {
        var id = Guid.NewGuid();
        var val = new RuntimeValue { VariableId = id, Value = PlcValue.Bool(true) };
        var snap = new InputSnapshot(new Dictionary<Guid, RuntimeValue> { [id] = val });

        Assert.NotNull(snap.Get(id));
        Assert.Null(snap.Get(Guid.NewGuid()));
        Assert.Single(snap.Values);
    }

    [Fact]
    public void InputSnapshot_Empty_IsEmpty()
    {
        Assert.Empty(InputSnapshot.Empty.Values);
        Assert.Null(InputSnapshot.Empty.Get(Guid.NewGuid()));
    }

    [Fact]
    public void MemorySnapshot_Get_ReturnsValue()
    {
        var id = Guid.NewGuid();
        var val = new RuntimeValue { VariableId = id, Value = PlcValue.Int32(42) };
        var snap = new MemorySnapshot(new Dictionary<Guid, RuntimeValue> { [id] = val });

        Assert.NotNull(snap.Get(id));
        Assert.Equal(42, snap.Get(id)!.Value.As<int>());
    }

    [Fact]
    public void MemorySnapshot_Empty_IsEmpty()
    {
        Assert.Empty(MemorySnapshot.Empty.Values);
    }

    [Fact]
    public void OutputSnapshot_Get_ReturnsValue()
    {
        var id = Guid.NewGuid();
        var val = new RuntimeValue { VariableId = id, Value = PlcValue.Bool(true) };
        var snap = new OutputSnapshot(new Dictionary<Guid, RuntimeValue> { [id] = val });

        Assert.NotNull(snap.Get(id));
        Assert.Single(snap.Values);
    }

    [Fact]
    public void OutputSnapshot_Empty_IsEmpty()
    {
        Assert.Empty(OutputSnapshot.Empty.Values);
    }

    [Fact]
    public void Snapshot_ScanNumber_IsInitiable()
    {
        var snap = new InputSnapshot(new Dictionary<Guid, RuntimeValue>()) { ScanNumber = 42 };
        Assert.Equal(42, snap.ScanNumber);
    }
}

public class VirtualDeviceDriverTests
{
    [Fact]
    public void SetInput_StoresValue()
    {
        var driver = new VirtualDeviceDriver(Guid.NewGuid());
        var id = Guid.NewGuid();
        driver.SetInput(id, PlcValue.Bool(true));

        var inputs = driver.SnapshotInputs();
        Assert.Single(inputs);
        Assert.True(inputs[id].Value.AsBool());
        Assert.Equal(Quality.Simulated, inputs[id].Quality);
        Assert.Equal(ValueSource.Simulation, inputs[id].Source);
    }

    [Fact]
    public void SetInput_BoolOverload()
    {
        var driver = new VirtualDeviceDriver(Guid.NewGuid());
        var id = Guid.NewGuid();
        driver.SetInput(id, true);

        Assert.True(driver.SnapshotInputs()[id].Value.AsBool());
    }

    [Fact]
    public void SetInput_OverwritesExisting()
    {
        var driver = new VirtualDeviceDriver(Guid.NewGuid());
        var id = Guid.NewGuid();
        driver.SetInput(id, true);
        driver.SetInput(id, false);

        Assert.False(driver.SnapshotInputs()[id].Value.AsBool());
        Assert.Single(driver.SnapshotInputs());
    }

    [Fact]
    public void ReadInputs_OnlyReturnsRequested()
    {
        var driver = new VirtualDeviceDriver(Guid.NewGuid());
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        driver.SetInput(id1, true);
        driver.SetInput(id2, false);
        driver.SetInput(id3, true);

        var result = driver.ReadInputs(new[] { id1, id3 });
        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey(id1));
        Assert.True(result.ContainsKey(id3));
        Assert.False(result.ContainsKey(id2));
    }

    [Fact]
    public void ReadInputs_MissingIds_AreSkipped()
    {
        var driver = new VirtualDeviceDriver(Guid.NewGuid());
        var result = driver.ReadInputs(new[] { Guid.NewGuid(), Guid.NewGuid() });
        Assert.Empty(result);
    }

    [Fact]
    public void DriverId_And_DeviceId()
    {
        var deviceId = Guid.NewGuid();
        var driver = new VirtualDeviceDriver(deviceId);
        Assert.Equal(deviceId, driver.DeviceId);
        Assert.Equal($"virtual-{deviceId:N}", driver.DriverId);
        Assert.Equal("Dispositivo virtual", driver.Name);
        driver.Name = "Custom";
        Assert.Equal("Custom", driver.Name);
    }

    [Fact]
    public void SnapshotInputs_ReturnsCopy()
    {
        var driver = new VirtualDeviceDriver(Guid.NewGuid());
        var id = Guid.NewGuid();
        driver.SetInput(id, true);

        var snap1 = driver.SnapshotInputs();
        var snap2 = driver.SnapshotInputs();
        Assert.NotSame(snap1, snap2);
        Assert.Single(snap1);
    }
}