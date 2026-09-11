using AtlasSoftPlc.Domain.Runtime;

namespace AtlasSoftPlc.Domain.Tests;

public class TimerStateTests
{
    [Fact]
    public void Ton_OutputAssertedWhenPresetReached()
    {
        var t = new TimerState { Kind = "TON", PresetMs = 1000, Input = true };
        t.Update(600);
        Assert.True(t.Running);
        Assert.False(t.Done);
        Assert.False(t.Output);

        t.Update(400);
        Assert.True(t.Done);
        Assert.True(t.Output);
    }

    [Fact]
    public void Ton_ResetsWhenInputDrops()
    {
        var t = new TimerState { Kind = "TON", PresetMs = 1000, Input = true };
        t.Update(500);
        t.Input = false;
        t.Update(10);
        Assert.Equal(0, t.ElapsedMs);
        Assert.False(t.Output);
    }

    [Fact]
    public void Ton_DoneClampsElapsedToPreset()
    {
        var t = new TimerState { Kind = "TON", PresetMs = 100, Input = true };
        t.Update(500);
        Assert.Equal(100, t.ElapsedMs);
    }

    [Fact]
    public void Tof_OutputTrueWhileInputHigh_ThenTimesOut()
    {
        var t = new TimerState { Kind = "TOF", PresetMs = 1000, Input = true };
        t.Update(100);
        Assert.True(t.Output);
        Assert.False(t.Done);

        t.Input = false;
        t.Update(600);
        Assert.True(t.Output);
        Assert.False(t.Done);

        t.Update(400);
        Assert.False(t.Output);
        Assert.True(t.Done);
    }

    [Fact]
    public void Tp_OutputPulsesForPresetThenDrops()
    {
        var t = new TimerState { Kind = "TP", PresetMs = 500, Input = true };
        t.Update(300);
        Assert.True(t.Output);
        Assert.False(t.Done);

        t.Update(300);
        Assert.False(t.Output);
        Assert.True(t.Done);
    }
}

public class CounterStateTests
{
    [Fact]
    public void Ctu_CountsEachRisingEdgePrecisely()
    {
        var c = new CounterState { Kind = "CTU", Preset = 3 };
        c.Update(true, false);   // 1
        c.Update(false, false);
        c.Update(true, false);   // 2
        c.Update(false, false);
        c.Update(true, false);   // 3 -> Done
        Assert.Equal(3, c.Current);
        Assert.True(c.Done);
    }

    [Fact]
    public void Ctu_ResetZeroesCurrent()
    {
        var c = new CounterState { Kind = "CTU", Preset = 3 };
        c.Update(true, false);
        c.Update(false, false);
        c.Update(true, false);
        Assert.Equal(2, c.Current);
        c.Update(false, true); // reset while input low
        Assert.Equal(0, c.Current);
        Assert.False(c.Done);
    }

    [Fact]
    public void Ctd_DecrementsFromPreset()
    {
        var c = new CounterState { Kind = "CTD", Preset = 3, Current = 3 };
        c.Update(true, false);  // -> 2
        c.Update(false, false);
        c.Update(true, false);  // -> 1
        c.Update(false, false);
        c.Update(true, false);  // -> 0 done
        Assert.Equal(0, c.Current);
        Assert.True(c.Done);
    }

    [Fact]
    public void NoCount_OnFallingEdge()
    {
        var c = new CounterState { Kind = "CTU", Preset = 5 };
        c.Update(true, false);
        c.Update(false, false); // falling edge, no count
        Assert.Equal(1, c.Current);
    }
}