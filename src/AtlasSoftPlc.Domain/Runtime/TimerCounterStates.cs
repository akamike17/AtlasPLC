namespace AtlasSoftPlc.Domain.Runtime;

/// <summary>Bloque de temporizador IEC (sección 14). Actualizado con tiempo monotónico.</summary>
public sealed class TimerState
{
    public Guid TimerId { get; set; }
    public string Kind { get; set; } = "TON"; // TON | TOF | TP
    public double PresetMs { get; set; }
    public double ElapsedMs { get; set; }
    public bool Input { get; set; }
    public bool Output { get; set; }
    public bool Running { get; set; }
    public bool Done { get; set; }
    public long LastUpdatedTicks { get; set; }

    /// <summary>Avanza el temporizador dado el delta de tiempo en ms desde la última actualización.</summary>
    public void Update(double deltaMs)
    {
        switch (Kind)
        {
            case "TON": UpdateTon(deltaMs); break;
            case "TOF": UpdateTof(deltaMs); break;
            case "TP": UpdateTp(deltaMs); break;
        }
        LastUpdatedTicks = DateTime.UtcNow.Ticks;
    }

    private void UpdateTon(double deltaMs)
    {
        if (Input)
        {
            Ran(deltaMs);
            Output = ElapsedMs >= PresetMs;
            Done = Output;
            if (Done) ElapsedMs = PresetMs;
        }
        else
        {
            ElapsedMs = 0;
            Running = false;
            Output = false;
            Done = false;
        }
    }

    private void UpdateTof(double deltaMs)
    {
        if (Input)
        {
            ElapsedMs = 0;
            Running = false;
            Output = true;
            Done = false;
        }
        else
        {
            Ran(deltaMs);
            Output = ElapsedMs < PresetMs;
            Done = !Output;
            if (Done) ElapsedMs = PresetMs;
        }
    }

    private void UpdateTp(double deltaMs)
    {
        if (Input)
        {
            Ran(deltaMs);
            Output = ElapsedMs < PresetMs;
            Done = !Output;
            if (Done) ElapsedMs = PresetMs;
        }
        else
        {
            // TP no se resetea al caer la entrada; mantiene su periodo terminado.
            Running = false;
            Output = false;
        }
    }

    private void Ran(double deltaMs)
    {
        Running = true;
        ElapsedMs += deltaMs;
    }
}

/// <summary>Bloque contador IEC (sección 15). Disparo por flanco.</summary>
public sealed class CounterState
{
    public Guid CounterId { get; set; }
    public string Kind { get; set; } = "CTU"; // CTU | CTD | CTUD
    public long Preset { get; set; }
    public long Current { get; set; }
    public bool Done { get; set; }
    public bool Reset { get; set; }
    public bool InputDown { get; set; }

    private bool _lastInput;

    /// <summary>Procesa flancos ascendentes (count up) y reset.</summary>
    public void Update(bool input, bool reset)
    {
        if (reset)
        {
            Current = Kind == "CTD" ? Preset : 0;
            Done = false;
            _lastInput = input;
            return;
        }

        if (input && !_lastInput)
        {
            Current = Kind switch
            {
                "CTD" => System.Math.Max(0, Current - 1),
                _ => Current + 1
            };
        }

        Done = Kind switch
        {
            "CTD" => Current <= 0,
            _ => Current >= Preset
        };

        _lastInput = input;
    }
}