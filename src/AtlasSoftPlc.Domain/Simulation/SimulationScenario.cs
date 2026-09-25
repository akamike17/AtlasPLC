using System;
using System.Collections.Generic;
using AtlasSoftPlc.Domain.Ir;

namespace AtlasSoftPlc.Domain.Simulation;

/// <summary>
/// Define un escenario de prueba determinista para validar la lógica de la IR.
/// Un escenario es una secuencia de pasos: Estado Inicial -> Evento -> Verificación.
/// </summary>
public sealed class SimulationScenario
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<ScenarioStep> Steps { get; init; } = new();
    public List<SimulationInvariant> Invariants { get; init; } = new();
}

public sealed class ScenarioStep
{
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Valores de entrada forzados para este paso.
    /// Guid de VariableId -> Valor esperado.
    /// </summary>
    public Dictionary<Guid, bool> InputOverrides { get; init; } = new();

    /// <summary>
    /// Verificaciones que deben cumplirse al final del paso.
    /// Guid de VariableId -> Valor esperado.
    /// </summary>
    public Dictionary<Guid, bool> ExpectedOutputs { get; init; } = new();

    /// <summary>
    /// Tiempo de espera en ms para que el sistema estabilice (timers, etc).
    /// </summary>
    public double SettleTimeMs { get; init; } = 0;
}

/// <summary>
/// Una regla que NUNCA debe romperse durante la ejecución de un escenario,
/// independientemente del paso actual.
/// </summary>
public sealed class SimulationInvariant
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Expresión lógica que debe ser SIEMPRE verdadera.
    /// </summary>
    public string Condition { get; init; } = string.Empty;
}
