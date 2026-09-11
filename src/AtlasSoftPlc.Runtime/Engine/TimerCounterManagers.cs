using AtlasSoftPlc.Domain.Runtime;

namespace AtlasSoftPlc.Runtime.Engine;

/// <summary>Gestiona temporizadores del runtime, actualizados con tiempo monotónico.</summary>
public sealed class TimerManager
{
    private readonly Dictionary<Guid, TimerState> _timers = new();
    private readonly object _lock = new();

    /// <summary>Registra un timer si no existe (con preset inicial dado).</summary>
    public TimerState GetOrCreate(Guid timerId, string kind, double presetMs)
    {
        lock (_lock)
        {
            if (!_timers.TryGetValue(timerId, out var t))
            {
                t = new TimerState { TimerId = timerId, Kind = kind, PresetMs = presetMs };
                _timers[timerId] = t;
            }
            else
            {
                t.Kind = kind;
                t.PresetMs = presetMs;
            }
            return t;
        }
    }

    /// <summary>Fija la entrada y avanza todos los timers con el delta monotónico.</summary>
    public void SetInput(Guid timerId, bool input)
    {
        lock (_lock)
        {
            if (_timers.TryGetValue(timerId, out var t))
                t.Input = input;
        }
    }

    public void Reset(Guid timerId)
    {
        lock (_lock)
        {
            if (_timers.TryGetValue(timerId, out var t))
            {
                t.ElapsedMs = 0;
                t.Output = false;
                t.Done = false;
                t.Running = false;
            }
        }
    }

    /// <summary>Avanza todos los timers según el delta de tiempo (ms).</summary>
    public void AdvanceAll(double deltaMs)
    {
        lock (_lock)
        {
            foreach (var t in _timers.Values)
                t.Update(deltaMs);
        }
    }

    public bool TryRead(Guid timerId, string field, out double value)
    {
        lock (_lock)
        {
            if (_timers.TryGetValue(timerId, out var t))
            {
                value = field switch
                {
                    "Running" => t.Running ? 1 : 0,
                    "Done" => t.Done ? 1 : 0,
                    "Elapsed" => t.ElapsedMs,
                    "Output" => t.Output ? 1 : 0,
                    _ => 0
                };
                return true;
            }
        }
        value = 0;
        return false;
    }

    public IReadOnlyDictionary<Guid, TimerState> Snapshot()
    {
        lock (_lock)
        {
            return _timers.ToDictionary(k => k.Key, v => new TimerState
            {
                TimerId = v.Value.TimerId,
                Kind = v.Value.Kind,
                PresetMs = v.Value.PresetMs,
                ElapsedMs = v.Value.ElapsedMs,
                Input = v.Value.Input,
                Output = v.Value.Output,
                Running = v.Value.Running,
                Done = v.Value.Done,
                LastUpdatedTicks = v.Value.LastUpdatedTicks
            });
        }
    }
}

/// <summary>Gestiona contadores del runtime con disparo por flanco.</summary>
public sealed class CounterManager
{
    private readonly Dictionary<Guid, CounterState> _counters = new();
    private readonly object _lock = new();

    public CounterState GetOrCreate(Guid counterId, string kind, long preset)
    {
        lock (_lock)
        {
            if (!_counters.TryGetValue(counterId, out var c))
            {
                c = new CounterState { CounterId = counterId, Kind = kind, Preset = preset };
                if (kind == "CTD") c.Current = preset;
                _counters[counterId] = c;
            }
            else
            {
                c.Kind = kind;
                c.Preset = preset;
            }
            return c;
        }
    }

    public void Increment(Guid counterId)
    {
        lock (_lock)
        {
            if (_counters.TryGetValue(counterId, out var c))
                c.Update(true, false);
        }
    }

    public void Decrement(Guid counterId)
    {
        lock (_lock)
        {
            if (_counters.TryGetValue(counterId, out var c))
            {
                c.Current = System.Math.Max(0, c.Current - 1);
                c.Done = c.Current <= 0;
            }
        }
    }

    public void Reset(Guid counterId)
    {
        lock (_lock)
        {
            if (_counters.TryGetValue(counterId, out var c))
                c.Update(false, true);
        }
    }

    public bool TryRead(Guid counterId, string field, out long value)
    {
        lock (_lock)
        {
            if (_counters.TryGetValue(counterId, out var c))
            {
                value = field switch
                {
                    "Done" => c.Done ? 1 : 0,
                    "Current" => c.Current,
                    "Preset" => c.Preset,
                    _ => 0
                };
                return true;
            }
        }
        value = 0;
        return false;
    }

    public IReadOnlyDictionary<Guid, CounterState> Snapshot()
    {
        lock (_lock)
        {
            return _counters.ToDictionary(k => k.Key, v => new CounterState
            {
                CounterId = v.Value.CounterId,
                Kind = v.Value.Kind,
                Preset = v.Value.Preset,
                Current = v.Value.Current,
                Done = v.Value.Done,
                Reset = v.Value.Reset
            });
        }
    }
}