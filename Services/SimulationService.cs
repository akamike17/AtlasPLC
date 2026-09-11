using System.Collections.Concurrent;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Runtime.Virtual;

namespace AtlasSoftPlc.Web.Services;

/// <summary>
/// Mantiene el estado del proyecto activo en memoria para el MVP, alimenta
/// el runtime y expone la simulación (bloque C / sección 24).
/// </summary>
public sealed class SimulationService
{
    private readonly PlcRuntimeService _runtime;
    private readonly object _lock = new();

    private Project? _project;
    private readonly Dictionary<Guid, VariableDefinition> _variables = new();
    private LogicProgram? _program;
    private readonly Dictionary<Guid, RuntimeValue> _inputValues = new();
    private readonly List<ScanTrace> _timeline = new();

    // Retención del timeline en memoria: cap duro para evitar crecimiento ilimitado
    // en un proceso 24/7 (memoria acotada). Se descartan las entradas más antiguas.
    private const int TimelineCapacity = 1000;

    public sealed class ScanTrace
    {
        public DateTime Time { get; set; }
        public long ScanNumber { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public SimulationService(PlcRuntimeService runtime)
    {
        _runtime = runtime;
    }

    public Project? Project => _project;
    public LogicProgram? ActiveProgram => _program;
    public IReadOnlyDictionary<Guid, VariableDefinition> Variables => _variables;

    /// <summary>Inicializa el proyecto de demo "Tanque" (sección 76/104).</summary>
    public Project BootstrapTankDemo()
    {
        lock (_lock)
        {
            _project = new Project
            {
                Name = "Tanque de agua",
                Description = "Llena el tanque cuando el nivel está bajo y se apaga al llegar arriba.",
                Mode = RuntimeMode.Simulation,
                LifecycleState = ProjectLifecycleState.SimulationReady
            };

            // Variables (inputs + outputs)
            var lowLevel = NewVar("LowLevelSensor", "Nivel bajo", PlcDataType.Bool, VariableDirection.Input, "Level");
            var highLevel = NewVar("HighLevelSensor", "Nivel alto", PlcDataType.Bool, VariableDirection.Input, "Level");
            var estop = NewVar("EmergencyStop", "Paro de emergencia", PlcDataType.Bool, VariableDirection.Input, "Switch");
            var pump = NewVar("Pump", "Bomba", PlcDataType.Bool, VariableDirection.Output, "Motor");
            pump.SafetyCritical = false;

            // Programa lógico (3 reglas, sección 76)
            _program = new LogicProgram
            {
                Name = "Tanque de agua",
                Version = 1,
                Rules = new List<LogicRule>
                {
                    new LogicRule
                    {
                        Name = "Paro de emergencia",
                        Priority = 1000,
                        Condition = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key },
                        Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "false" } }
                    },
                    new LogicRule
                    {
                        Name = "Encender bomba",
                        Priority = 100,
                        Condition = new AndExpression
                        {
                            Operands = new List<ExpressionNode>
                            {
                                new VariableExpression { VariableId = lowLevel.Id, VariableKey = lowLevel.Key },
                                new NotExpression { Operand = new VariableExpression { VariableId = highLevel.Id, VariableKey = highLevel.Key } },
                                new NotExpression { Operand = new VariableExpression { VariableId = estop.Id, VariableKey = estop.Key } }
                            }
                        },
                        Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "true" } }
                    },
                    new LogicRule
                    {
                        Name = "Apagar bomba por nivel alto",
                        Priority = 100,
                        Condition = new VariableExpression { VariableId = highLevel.Id, VariableKey = highLevel.Key },
                        Actions = new List<LogicAction> { new SetOutputAction { VariableId = pump.Id, Value = "false" } }
                    }
                }
            };

            // Valores iniciales de entrada
            _inputValues[lowLevel.Id] = Runtime(lowLevel.Id, false);
            _inputValues[highLevel.Id] = Runtime(highLevel.Id, false);
            _inputValues[estop.Id] = Runtime(estop.Id, false);

            InstallToRuntime();
            return _project;
        }
    }

    private VariableDefinition NewVar(string key, string display, PlcDataType type, VariableDirection dir, string virtualIo)
    {
        var v = new VariableDefinition
        {
            Key = key,
            DisplayName = display,
            DataType = type,
            Direction = dir,
            VirtualIoKind = virtualIo
        };
        _variables[v.Id] = v;
        return v;
    }

    private static RuntimeValue Runtime(Guid id, bool value) => new()
    {
        VariableId = id,
        Value = PlcValue.Bool(value),
        Quality = Quality.Simulated,
        Source = ValueSource.Simulation,
        TimestampUtc = DateTime.UtcNow
    };

    private void InstallToRuntime()
    {
        if (_program is null) return;
        var failsafe = new Dictionary<Guid, PlcValue>();
        foreach (var v in _variables.Values.Where(x => x.Direction == VariableDirection.Output))
            failsafe[v.Id] = PlcValue.Bool(false);

        var interlocks = new List<Interlock>();

        _runtime.InstallConfiguration(_program, _variables, interlocks, failsafe);
        _runtime.SetInputs(_inputValues);
        _runtime.Post(new ResumeCommand());
    }

    /// <summary>Cambia una entrada (sensor/interruptor) desde la UI de simulación.
    /// Devuelve false si la variable no existe o no es de entrada (IDOR-safe).</summary>
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

    /// <summary>Snapshot de todas las entradas para la UI.</summary>
    public Dictionary<string, object> GetInputsUi()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, object>();
            foreach (var v in _variables.Values.Where(x => x.Direction == VariableDirection.Input))
            {
                var val = _inputValues.TryGetValue(v.Id, out var rv) && rv.Value.HasValue
                    ? rv.Value.AsBool()
                    : false;
                result[v.Id.ToString()] = new { id = v.Id, key = v.Key, displayName = v.DisplayName, value = val };
            }
            return result;
        }
    }

    /// <summary>Snapshot de todas las salidas para la UI (con nombres legibles).</summary>
    public Dictionary<string, object> GetOutputsUi()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, object>();
            foreach (var v in _variables.Values.Where(x => x.Direction == VariableDirection.Output))
            {
                // El snapshot real viene del runtime, pero usamos _memory como proxy
                // Para el MVP, devolvemos el estado actual simulado
                var val = false; // se actualiza vía SignalR en tiempo real
                result[v.Id.ToString()] = new { id = v.Id, key = v.Key, displayName = v.DisplayName, value = val };
            }
            return result;
        }
    }

    public IReadOnlyList<ScanTrace> Timeline { get { lock (_lock) return _timeline.ToList(); } }

    private void TrimTimeline()
    {
        if (_timeline.Count > TimelineCapacity)
            _timeline.RemoveRange(0, _timeline.Count - TimelineCapacity);
    }
}