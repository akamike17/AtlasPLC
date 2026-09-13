using System.Text.Json;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Models;

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
            var templateNames = PlcProgramCatalog.BuildTemplateCatalog()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var program in _catalog)
                if (templateNames.Contains(program.Name))
                    program.IsTemplate = true;
        }
    }

    public IReadOnlyList<PlcProgramDefinition> GetLibrary()
    {
        lock (_lock)
        {
            return _catalog.ToList();
        }
    }

    public PlcProgramDefinition CreateFromZero(string name)
    {
        lock (_lock)
        {
            var program = new PlcProgramDefinition { Name = string.IsNullOrWhiteSpace(name) ? "Proyecto nuevo" : name.Trim(), Description = "Proyecto creado desde cero en el Workbench." };
            _catalog.Add(program);
            LoadProgramCore(program);
            _catalogService.SaveAsync(program).GetAwaiter().GetResult();
            return program;
        }
    }

    public PlcProgramDefinition? DuplicateProgram(Guid id, string? name = null)
    {
        lock (_lock)
        {
            var source = _catalog.FirstOrDefault(p => p.Id == id);
            if (source is null) return null;
            var copy = JsonSerializer.Deserialize<PlcProgramDefinition>(JsonSerializer.Serialize(source));
            if (copy is null) return null;
            copy.Id = Guid.NewGuid();
            copy.Name = string.IsNullOrWhiteSpace(name) ? $"{source.Name} — copia" : name.Trim();
            copy.IsTemplate = false;
            copy.Version = 1;
            copy.CreatedUtc = DateTime.UtcNow;
            copy.UpdatedUtc = copy.CreatedUtc;
            copy.Hash = string.Empty;
            _catalog.Add(copy);
            _catalogService.SaveAsync(copy).GetAwaiter().GetResult();
            return copy;
        }
    }

    public bool DeleteProgram(Guid id)
    {
        lock (_lock)
        {
            var program = _catalog.FirstOrDefault(p => p.Id == id);
            if (program is null || program.IsTemplate || _active?.Id == id) return false;
            _catalog.Remove(program);
            _catalogService.DeleteAsync(id).GetAwaiter().GetResult();
            return true;
        }
    }

    public bool RemoveComponent(Guid variableId)
    {
        lock (_lock)
        {
            if (_active is null) return false;
            var variable = _active.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable is null) return false;
            _active.Variables.Remove(variable);
            _active.Failsafe.Remove(variableId);
            _active.ModbusMap.Remove(variable.Key);
            _active.Logic.Rules.RemoveAll(rule =>
                rule.Condition is VariableExpression expression && expression.VariableId == variableId ||
                rule.Actions.OfType<SetOutputAction>().Any(action => action.VariableId == variableId) ||
                rule.ElseActions.OfType<SetOutputAction>().Any(action => action.VariableId == variableId));
            LoadProgramCore(_active);
            _catalogService.SaveAsync(_active).GetAwaiter().GetResult();
            return true;
        }
    }

    public bool RestoreProgramDefinition(string definitionJson)
    {
        lock (_lock)
        {
            if (_active is null) return false;
            var restored = JsonSerializer.Deserialize<PlcProgramDefinition>(definitionJson);
            if (restored is null || restored.Variables.Count == 0 || restored.Id != _active.Id) return false;
            restored.IsTemplate = false;
            restored.UpdatedUtc = DateTime.UtcNow;
            var index = _catalog.FindIndex(p => p.Id == restored.Id);
            if (index < 0) return false;
            _catalog[index] = restored;
            _catalogService.SaveAsync(restored).GetAwaiter().GetResult();
            LoadProgramCore(restored);
            return true;
        }
    }

    public bool ImportProgramDefinition(string definitionJson, string nameOverride)
    {
        lock (_lock)
        {
            var imported = JsonSerializer.Deserialize<PlcProgramDefinition>(definitionJson);
            if (imported is null || imported.Variables.Count == 0 || imported.Variables.Select(v => v.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != imported.Variables.Count) return false;
            imported.Id = Guid.NewGuid();
            imported.Name = string.IsNullOrWhiteSpace(nameOverride) ? $"{imported.Name} — importado" : nameOverride.Trim();
            imported.IsTemplate = false;
            imported.Version = 1;
            imported.CreatedUtc = DateTime.UtcNow;
            imported.UpdatedUtc = imported.CreatedUtc;
            imported.Hash = string.Empty;
            _catalog.Add(imported);
            _catalogService.SaveAsync(imported).GetAwaiter().GetResult();
            LoadProgramCore(imported);
            return true;
        }
    }

    public bool AddBooleanComponent(string key, VariableDirection direction)
    {
        lock (_lock)
        {
            if (_active is null || string.IsNullOrWhiteSpace(key) || _active.Variables.Any(v => v.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
            var variable = Var(key.Trim(), key.Trim(), direction);
            _active.Variables.Add(variable);
            if (direction == VariableDirection.Output) _active.Failsafe[variable.Id] = PlcValue.Bool(false);
            LoadProgramCore(_active);
            _catalogService.SaveAsync(_active).GetAwaiter().GetResult();
            return true;
        }
    }

    public bool ConnectInputToOutput(string inputKey, string outputKey)
    {
        lock (_lock)
        {
            if (_active is null) return false;
            var input = _active.Variables.FirstOrDefault(v => v.Key.Equals(inputKey, StringComparison.OrdinalIgnoreCase) && v.Direction == VariableDirection.Input);
            var output = _active.Variables.FirstOrDefault(v => v.Key.Equals(outputKey, StringComparison.OrdinalIgnoreCase) && v.Direction == VariableDirection.Output);
            if (input is null || output is null) return false;
            var isStop = input.Key.Contains("stop", StringComparison.OrdinalIgnoreCase) || input.Key.Contains("paro", StringComparison.OrdinalIgnoreCase) || input.Key.Contains("emergency", StringComparison.OrdinalIgnoreCase);
            _active.Logic.Rules.Add(new LogicRule { Name = isStop ? $"{input.Key} detiene {output.Key}" : $"{input.Key} activa {output.Key}", Priority = isStop ? 1000 : 100, Condition = new VariableExpression { VariableId = input.Id, VariableKey = input.Key }, Actions = new List<LogicAction> { new SetOutputAction { VariableId = output.Id, Value = isStop ? "false" : "true" } } });
            LoadProgramCore(_active);
            _catalogService.SaveAsync(_active).GetAwaiter().GetResult();
            return true;
        }
    }

    public bool AddTimedConnection(string inputKey, string outputKey, double presetMs)
    {
        lock (_lock)
        {
            if (_active is null || presetMs <= 0) return false;
            var input = _active.Variables.FirstOrDefault(v => v.Key.Equals(inputKey, StringComparison.OrdinalIgnoreCase) && v.Direction == VariableDirection.Input);
            var output = _active.Variables.FirstOrDefault(v => v.Key.Equals(outputKey, StringComparison.OrdinalIgnoreCase) && v.Direction == VariableDirection.Output);
            if (input is null || output is null) return false;
            var timerId = Guid.NewGuid();
            _active.Logic.TimerIds.Add(timerId);
            _active.Logic.Rules.Add(new LogicRule { Name = $"Pausa {presetMs:0}ms de {input.Key}", Priority = 100, Condition = new VariableExpression { VariableId = input.Id, VariableKey = input.Key }, Actions = new List<LogicAction> { new StartTimerAction { TimerId = timerId, PresetMs = presetMs } } });
            _active.Logic.Rules.Add(new LogicRule { Name = $"Temporizador activa {output.Key}", Priority = 90, Condition = new TimerStateExpression { TimerId = timerId, Field = "Done" }, Actions = new List<LogicAction> { new SetOutputAction { VariableId = output.Id, Value = "true" } }, ElseActions = new List<LogicAction> { new SetOutputAction { VariableId = output.Id, Value = "false" } } });
            LoadProgramCore(_active);
            _catalogService.SaveAsync(_active).GetAwaiter().GetResult();
            return true;
        }
    }

    private static VariableDefinition Var(string key, string display, VariableDirection direction) => new() { Key = key, DisplayName = display, DataType = PlcDataType.Bool, Direction = direction };

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
            // Reemplazar primero el runtime evita anunciar un programa activo que el
            // motor no pudo instalar. El estado de la UI se actualiza sólo después.
            var replaced = _runtime.ReplaceProgramAsync(program, autoStart: true).GetAwaiter().GetResult();
            if (!replaced) return false;
            SetActiveState(program);
            return true;
        }
    }

    /// <summary>
    /// Instala un candidato sólo en runtime y memoria. La unidad de trabajo de
    /// aplicación gráfica persiste después; si ese commit falla, este mismo método
    /// restaura el programa anterior sin escribir una compensación parcial en SQLite.
    /// </summary>
    public bool TryApplyRuntimeCandidate(PlcProgramDefinition program)
    {
        lock (_lock)
        {
            if (_catalog.All(p => p.Id != program.Id)) return false;
            try
            {
                if (!_runtime.ReplaceProgramAsync(program, autoStart: true).GetAwaiter().GetResult()) return false;
            }
            catch (Exception)
            {
                return false;
            }
            var index = _catalog.FindIndex(p => p.Id == program.Id);
            _catalog[index] = program;
            SetActiveState(program);
            return true;
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

            // Recuperar el último proyecto guardado para que Revisión y Simulación
            // sobrevivan a un reinicio; sólo usar Tanque como fallback inicial.
            // No confundir las plantillas sembradas con un proyecto trabajado
            // por el usuario: las plantillas también tienen UpdatedUtc.
            var lastSaved = _catalog
                .Where(p => !p.IsTemplate && !p.Name.StartsWith("Plantilla ", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.UpdatedUtc)
                .FirstOrDefault();
            var initial = lastSaved ?? _catalog.FirstOrDefault(p => p.Name == "Tanque de agua") ?? _catalog.FirstOrDefault();
            if (initial is not null)
                LoadProgramCore(initial);

            return Project;
        }
    }

    private void LoadProgramCore(PlcProgramDefinition program)
    {
        SetActiveState(program);

        _runtime.ReplaceProgramAsync(program, autoStart: true).GetAwaiter().GetResult();
    }

    private void SetActiveState(PlcProgramDefinition program)
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

    public IReadOnlyList<IoPointViewModel> GetTypedInputsUi() => GetInputsUi().Values.Select((value, _) =>
    {
        dynamic item = value;
        return new IoPointViewModel { Id = item.id, Key = item.key, DisplayName = item.displayName, Direction = "Input", Value = item.value, InitialValue = item.value, Writable = true };
    }).ToList();

    public IReadOnlyList<IoPointViewModel> GetTypedOutputsUi() => GetOutputsUi().Values.Select((value, _) =>
    {
        dynamic item = value;
        return new IoPointViewModel { Id = item.id, Key = item.key, DisplayName = item.displayName, Direction = "Output", Value = item.value, InitialValue = item.value };
    }).ToList();

    private void TrimTimeline()
    {
        if (_timeline.Count > TimelineCapacity)
            _timeline.RemoveRange(0, _timeline.Count - TimelineCapacity);
    }
}
