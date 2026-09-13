using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Mantiene la biblioteca de programas PLC y el programa activo en memoria, alimenta el
/// runtime y expone la simulación (bloque C / sección 24). La persistencia real de la
/// biblioteca vive en SQLite vía <see cref="PlcProgramService"/>.
/// </summary>
public sealed class SimulationService
{
    private readonly PlcRuntimeService _runtime;
    private readonly PlcProgramService _catalogService;
    private readonly object _lock = new();

    private readonly List<PlcProgramDefinition> _catalog = new();
    private PlcProgramDefinition? _active;
    private readonly Dictionary<Guid, VariableDefinition> _variables = new();
    private LogicProgram? _program;
    private readonly Dictionary<Guid, RuntimeValue> _inputValues = new();
    private readonly List<ScanTrace> _timeline = new();

    private const int TimelineCapacity = 1000;

    public sealed class ScanTrace
    {
        public DateTime Time { get; set; }
        public long ScanNumber { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public SimulationService(PlcRuntimeService runtime, PlcProgramService catalogService)
    {
        _runtime = runtime;
        _catalogService = catalogService;
    }

    public Project? Project { get; private set; }
    public LogicProgram? ActiveProgram => _program;
    public PlcProgramDefinition? Active => _active;
    public IReadOnlyDictionary<Guid, VariableDefinition> Variables => _variables;
    public IReadOnlyList<PlcProgramDefinition> Catalog => _catalog;
    public IReadOnlyList<ScanTrace> Timeline { get { lock (_lock) return _timeline.ToList(); } }

    /// <summary>Mapa Modbus del programa activo (para el puente de I/O).</summary>
    public IReadOnlyDictionary<string, string> ActiveModbusMap =>
        _active?.ModbusMap ?? new Dictionary<string, string>();

    // ── Biblioteca ────────────────────────────────────────────────────────

    /// <summary>
    /// Carga la biblioteca desde persistencia y, si está vacía, la siembra con los
    /// programas iniciales. Idempotente. Se invoca al arrancar y ante requests.
    /// </summary>
    public void EnsureLibrary()
    {
        lock (_lock)
        {
            if (_catalog.Count > 0)
                return;

            _catalogService.EnsureSeededAsync().GetAwaiter().GetResult();
            var all = _catalogService.GetAllAsync().GetAwaiter().GetResult();
            _catalog.Clear();
            _catalog.AddRange(all);
        }
    }

    public IReadOnlyList<PlcProgramDefinition> GetLibrary()
    {
        lock (_lock)
        {
            return _catalog.ToList();
        }
    }

    /// <summary>
    /// Carga un programa de la biblioteca como activo. Flujo seguro (P0-2): el cambio de
    /// programa se delega al runtime como UNA operación transaccional (Stop → failsafe del
    /// saliente → clear forces → install → reset → start), no encadenando comandos asíncronos.
    /// Devuelve false si el programa no existe.
    /// </summary>
    public bool LoadProgram(Guid id)
    {
        lock (_lock)
        {
            var program = _catalog.FirstOrDefault(p => p.Id == id);
            if (program is null)
                return false;

            // 1. Actualizar estado local del servicio (catálogo en memoria de la UI).
            _active = program;
            _variables.Clear();
            foreach (var v in program.Variables)
                _variables[v.Id] = v;
            _program = program.Logic;
            _inputValues.Clear();
            foreach (var v in program.Variables.Where(x => x.Direction == VariableDirection.Input))
                _inputValues[v.Id] = Runtime(v.Id, false);

            Project = new Project
            {
                Name = program.Name,
                Description = program.Description,
                Mode = RuntimeMode.Simulation,
                LifecycleState = ProjectLifecycleState.SimulationReady
            };

            // 2. Reemplazo transaccional en el runtime (single-writer, atómico).
            var replaced = _runtime.ReplaceProgramAsync(program, autoStart: true).GetAwaiter().GetResult();

            return replaced;
        }
    }

    /// <summary>
    /// Bootstrap de compatibilidad: garantiza biblioteca y carga el programa inicial
    /// (Tanque) si no hay ninguno activo.
    /// </summary>
    public Project? BootstrapTankDemo()
    {
        EnsureLibrary();
        lock (_lock)
        {
            if (_active is not null)
                return Project;

            var tank = _catalog.FirstOrDefault(p => p.Name == "Tanque de agua") ?? _catalog.FirstOrDefault();
            if (tank is not null)
                LoadProgramCore(tank);

            return Project;
        }
    }

    private void LoadProgramCore(PlcProgramDefinition program)
    {
        _active = program;
        _variables.Clear();
        foreach (var v in program.Variables)
            _variables[v.Id] = v;
        _program = program.Logic;

        _inputValues.Clear();
        foreach (var v in program.Variables.Where(x => x.Direction == VariableDirection.Input))
            _inputValues[v.Id] = Runtime(v.Id, false);

        Project = new Project
        {
            Name = program.Name,
            Description = program.Description,
            Mode = RuntimeMode.Simulation,
            LifecycleState = ProjectLifecycleState.SimulationReady
        };

        _runtime.ReplaceProgramAsync(program, autoStart: true).GetAwaiter().GetResult();
    }

    private static RuntimeValue Runtime(Guid id, bool value) => new()
    {
        VariableId = id,
        Value = PlcValue.Bool(value),
        Quality = Quality.Simulated,
        Source = ValueSource.Simulation,
        TimestampUtc = DateTime.UtcNow
    };

    // ── Entradas / salidas (fuente de verdad: runtime) ────────────────────

    public bool TrySetInput(Guid variableId, bool value)
    {
        lock (_lock)
        {
            if (!_variables.TryGetValue(variableId, out var def) || def.Direction != VariableDirection.Input)
                return false;

            _inputValues[variableId] = new RuntimeValue
            {
                VariableId = variableId,
                Value = PlcValue.Bool(value),
                Quality = Quality.Simulated,
                Source = ValueSource.Manual,
                TimestampUtc = DateTime.UtcNow
            };

            var name = def.DisplayName;
            _timeline.Add(new ScanTrace
            {
                Time = DateTime.UtcNow,
                ScanNumber = 0,
                Description = $"Entrada '{name}' → {(value ? "ON" : "OFF")}"
            });
            TrimTimeline();

            _runtime.SetInputs(_inputValues);
            _runtime.Post(new SetManualInputCommand(variableId, PlcValue.Bool(value)));
            return true;
        }
    }

    public void Start() => _runtime.Post(new ResumeCommand());
    public void Stop() => _runtime.Post(new StopCommand());

    public Dictionary<string, object> GetInputsUi()
    {
        lock (_lock)
        {
            var realInputs = _runtime.GetInputs();
            var result = new Dictionary<string, object>();
            foreach (var v in _variables.Values.Where(x => x.Direction == VariableDirection.Input))
            {
                var val = realInputs.TryGetValue(v.Id, out var rv) && rv.Value.HasValue
                    ? rv.Value.AsBool()
                    : false;
                result[v.Id.ToString()] = new { id = v.Id, key = v.Key, displayName = v.DisplayName, value = val };
            }
            return result;
        }
    }

    public Dictionary<string, object> GetOutputsUi()
    {
        lock (_lock)
        {
            var realOutputs = _runtime.GetOutputs();
            var result = new Dictionary<string, object>();
            foreach (var v in _variables.Values.Where(x => x.Direction == VariableDirection.Output))
            {
                var val = realOutputs.TryGetValue(v.Id, out var rv) && rv.Value.HasValue
                    ? rv.Value.AsBool()
                    : false;
                result[v.Id.ToString()] = new { id = v.Id, key = v.Key, displayName = v.DisplayName, value = val };
            }
            return result;
        }
    }

    private void TrimTimeline()
    {
        if (_timeline.Count > TimelineCapacity)
            _timeline.RemoveRange(0, _timeline.Count - TimelineCapacity);
    }
}